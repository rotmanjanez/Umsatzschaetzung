#include <stdlib.h>
#include <string.h>

#include "llama.h"

#include "engine.h"

#define AL_BATCH 512
#define AL_DONE 0
#define AL_ABORTED 1
#define AL_ERR_TOKENIZE -1
#define AL_ERR_PROMPT_TOO_LONG -2
#define AL_ERR_GRAMMAR -3
#define AL_ERR_DECODE -4
#define AL_ERR_PIECE -5
#define AL_ERR_MEMORY -6

#if defined(_MSC_VER)
#include <intrin.h>
static long al_load(volatile long *p) { return _InterlockedCompareExchange(p, 0, 0); }
static void al_store(volatile long *p, long v) { _InterlockedExchange(p, v); }
#else
static long al_load(volatile long *p) { return __atomic_load_n(p, __ATOMIC_SEQ_CST); }
static void al_store(volatile long *p, long v) { __atomic_store_n(p, v, __ATOMIC_SEQ_CST); }
#endif

struct al_ctx {
	struct llama_model *model;
	struct llama_context *lctx;
	const struct llama_vocab *vocab;
	int n_ctx;
	volatile long aborted;
};

struct al_buf {
	char *data;
	size_t len;
	size_t cap;
};

static void al_log_silent(enum ggml_log_level level, const char *text, void *user) {
	(void) level; (void) text; (void) user;
}

static char *al_strdup(const char *s) {
	size_t n = strlen(s) + 1;
	char *p = malloc(n);
	if (p) memcpy(p, s, n);
	return p;
}

static void al_set_err(char **err, const char *msg) {
	if (err) *err = al_strdup(msg);
}

static bool al_abort_cb(void *data) {
	return al_load(&((struct al_ctx *) data)->aborted) != 0;
}

static int al_buf_append(struct al_buf *b, const char *s, size_t n) {
	if (b->len + n + 1 > b->cap) {
		size_t cap = b->cap ? b->cap * 2 : 1024;
		while (cap < b->len + n + 1) cap *= 2;
		char *p = realloc(b->data, cap);
		if (!p) return -1;
		b->data = p;
		b->cap = cap;
	}
	memcpy(b->data + b->len, s, n);
	b->len += n;
	b->data[b->len] = 0;
	return 0;
}

static int al_backend_ready = 0;

void *umsatzschaetzung_llm_open(const char *model_path, int n_ctx, int n_threads, char **err) {
	if (err) *err = NULL;
	if (!al_backend_ready) {
		llama_log_set(al_log_silent, NULL);
		llama_backend_init();
		al_backend_ready = 1;
	}

	struct llama_model_params mp = llama_model_default_params();
	mp.n_gpu_layers = -1;

	struct llama_model *model = llama_model_load_from_file(model_path, mp);
	if (!model) {
		al_set_err(err, "llama_model_load_from_file failed");
		return NULL;
	}

	struct llama_context_params cp = llama_context_default_params();
	cp.n_ctx = (unsigned) n_ctx;
	cp.n_batch = AL_BATCH;
	cp.n_ubatch = AL_BATCH;
	cp.n_threads = n_threads;
	cp.n_threads_batch = n_threads;

	struct al_ctx *c = calloc(1, sizeof(struct al_ctx));
	if (!c) {
		llama_model_free(model);
		al_set_err(err, "out of memory");
		return NULL;
	}
	cp.abort_callback = al_abort_cb;
	cp.abort_callback_data = c;

	struct llama_context *lctx = llama_init_from_model(model, cp);
	if (!lctx) {
		free(c);
		llama_model_free(model);
		al_set_err(err, "llama_init_from_model failed");
		return NULL;
	}

	c->model = model;
	c->lctx = lctx;
	c->vocab = llama_model_get_vocab(model);
	c->n_ctx = (int) llama_n_ctx(lctx);
	return c;
}

static int al_decode(struct al_ctx *c, llama_token *toks, int n) {
	for (int i = 0; i < n; i += AL_BATCH) {
		if (al_load(&c->aborted)) return AL_ABORTED;
		int m = n - i < AL_BATCH ? n - i : AL_BATCH;
		int rc = llama_decode(c->lctx, llama_batch_get_one(toks + i, m));
		if (rc == 2) return AL_ABORTED;
		if (rc != 0) return AL_ERR_DECODE;
	}
	return AL_DONE;
}

int umsatzschaetzung_llm_complete(void *h, const char *prompt, const char *grammar, int max_tokens, char **out, char **err) {
	struct al_ctx *c = h;
	*out = NULL;
	if (err) *err = NULL;
	al_store(&c->aborted, 0);

	llama_memory_clear(llama_get_memory(c->lctx), true);

	int prompt_len = (int) strlen(prompt);
	int cap = prompt_len + 64;
	llama_token *toks = malloc((size_t) cap * sizeof(llama_token));
	if (!toks) {
		al_set_err(err, "out of memory");
		return AL_ERR_MEMORY;
	}
	int n = llama_tokenize(c->vocab, prompt, prompt_len, toks, cap, true, true);
	if (n < 0) {
		free(toks);
		al_set_err(err, "llama_tokenize failed");
		return AL_ERR_TOKENIZE;
	}
	if (n >= c->n_ctx) {
		free(toks);
		al_set_err(err, "prompt exceeds context");
		return AL_ERR_PROMPT_TOO_LONG;
	}

	struct llama_sampler_chain_params sp = llama_sampler_chain_default_params();
	sp.no_perf = true;
	struct llama_sampler *smpl = llama_sampler_chain_init(sp);
	if (grammar && grammar[0]) {
		struct llama_sampler *g = llama_sampler_init_grammar(c->vocab, grammar, "root");
		if (!g) {
			free(toks);
			llama_sampler_free(smpl);
			al_set_err(err, "grammar rejected");
			return AL_ERR_GRAMMAR;
		}
		llama_sampler_chain_add(smpl, g);
	}
	llama_sampler_chain_add(smpl, llama_sampler_init_greedy());

	int rc = al_decode(c, toks, n);
	free(toks);
	if (rc != AL_DONE) {
		llama_sampler_free(smpl);
		if (rc != AL_ABORTED) al_set_err(err, "llama_decode failed");
		return rc;
	}
	int n_used = n;

	struct al_buf text = {0};
	char piece[256];
	for (int i = 0; i < max_tokens && n_used < c->n_ctx; i++) {
		if (al_load(&c->aborted)) {
			rc = AL_ABORTED;
			break;
		}
		llama_token id = llama_sampler_sample(smpl, c->lctx, -1);
		if (llama_vocab_is_eog(c->vocab, id)) break;

		int k = llama_token_to_piece(c->vocab, id, piece, (int) sizeof piece, 0, false);
		if (k < 0) {
			rc = AL_ERR_PIECE;
			al_set_err(err, "llama_token_to_piece failed");
			break;
		}
		if (al_buf_append(&text, piece, (size_t) k) != 0) {
			rc = AL_ERR_MEMORY;
			al_set_err(err, "out of memory");
			break;
		}

		rc = al_decode(c, &id, 1);
		if (rc != AL_DONE) {
			if (rc != AL_ABORTED) al_set_err(err, "llama_decode failed");
			break;
		}
		n_used++;
	}

	llama_sampler_free(smpl);
	if (rc != AL_DONE) {
		free(text.data);
		return rc;
	}
	*out = text.data ? text.data : al_strdup("");
	return AL_DONE;
}

void umsatzschaetzung_llm_abort(void *h) {
	if (h) al_store(&((struct al_ctx *) h)->aborted, 1);
}

void umsatzschaetzung_llm_close(void *h) {
	struct al_ctx *c = h;
	if (!c) return;
	llama_free(c->lctx);
	llama_model_free(c->model);
	free(c);
}

void umsatzschaetzung_llm_free(char *p) { free(p); }
