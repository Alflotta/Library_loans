#include "libraryloans_native.h"

int ll_abi_version(void)
{
    return LL_ABI_VERSION;
}

int ll_fine(int days_late, int per_day, int cap, int grace_days)
{
    long long chargeable;
    long long fee;

    if (days_late < 0 || per_day < 0 || cap < 0 || grace_days < 0)
        return -1;

    chargeable = (long long)days_late - (long long)grace_days;
    if (chargeable <= 0)
        return 0;

    fee = chargeable * (long long)per_day;
    if (fee > (long long)cap)
        fee = (long long)cap;

    return (int)fee;
}
