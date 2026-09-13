#ifndef AUSBEUTE_LLM_ENGINE_H
#define AUSBEUTE_LLM_ENGINE_H

#if defined(_WIN32)
#define AUSBEUTE_LLM_API __declspec(dllexport)
#else
#define AUSBEUTE_LLM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

AUSBEUTE_LLM_API void *ausbeute_llm_open(const char *model_path, int n_ctx, int n_threads, char **err);
AUSBEUTE_LLM_API int ausbeute_llm_complete(void *h, const char *prompt, const char *grammar, int max_tokens, char **out, char **err);
AUSBEUTE_LLM_API void ausbeute_llm_abort(void *h);
AUSBEUTE_LLM_API void ausbeute_llm_close(void *h);
AUSBEUTE_LLM_API void ausbeute_llm_free(char *p);

#ifdef __cplusplus
}
#endif

#endif
