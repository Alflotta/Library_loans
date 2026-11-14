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

/* Version of the binary interface. The C# adapter refuses a library with another value. */
#define LL_ABI_VERSION 1

LL_EXPORT int ll_abi_version(void);

/*
 * Late fee with a grace period.
 *   days_late   days after the due date (>= 0)
 *   per_day     fee for each chargeable day (>= 0)
 *   cap         maximum fee (>= 0)
 *   grace_days  first days that are free (>= 0)
 * Returns min((days_late - grace_days) * per_day, cap), or 0 when days_late <= grace_days.
 * Returns -1 when any argument is negative.
 * The multiplication uses 64 bits, so large inputs cannot overflow.
 */
LL_EXPORT int ll_fine(int days_late, int per_day, int cap, int grace_days);

#ifdef __cplusplus
}
#endif

#endif
