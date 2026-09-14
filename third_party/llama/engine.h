#ifndef UMSATZSCHAETZUNG_LLM_ENGINE_H
#define UMSATZSCHAETZUNG_LLM_ENGINE_H

#if defined(_WIN32)
#define UMSATZSCHAETZUNG_LLM_API __declspec(dllexport)
#else
#define UMSATZSCHAETZUNG_LLM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

UMSATZSCHAETZUNG_LLM_API void *umsatzschaetzung_llm_open(const char *model_path, int n_ctx, int n_threads, char **err);
UMSATZSCHAETZUNG_LLM_API int umsatzschaetzung_llm_complete(void *h, const char *prompt, const char *grammar, int max_tokens, char **out, char **err);
UMSATZSCHAETZUNG_LLM_API void umsatzschaetzung_llm_abort(void *h);
UMSATZSCHAETZUNG_LLM_API void umsatzschaetzung_llm_close(void *h);
UMSATZSCHAETZUNG_LLM_API void umsatzschaetzung_llm_free(char *p);

#ifdef __cplusplus
}
#endif

#endif
