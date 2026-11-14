#ifndef LIBRARYLOANS_NATIVE_H
#define LIBRARYLOANS_NATIVE_H

#ifdef _WIN32
#define LL_EXPORT __declspec(dllexport)
#else
#define LL_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define LL_ABI_VERSION 1

LL_EXPORT int ll_abi_version(void);

LL_EXPORT int ll_fine(int days_late, int per_day, int cap, int grace_days);

#ifdef __cplusplus
}
#endif

#endif
