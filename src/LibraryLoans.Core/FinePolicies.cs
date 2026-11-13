namespace LibraryLoans.Core;

public static class FinePolicies
{
    public const int StandardPerDay = 50;
    public const int StandardCap = 1000;
    public const int StrictPerDay = 100;
    public const int StrictCap = 3000;

    public static int Standard(int daysLate) => Math.Min(daysLate * StandardPerDay, StandardCap);

    public static int Strict(int daysLate) => Math.Min(daysLate * StrictPerDay, StrictCap);

    public static bool TryGet(string name, out Func<int, int> policy)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "standard": policy = Standard; return true;
            case "strict": policy = Strict; return true;
            default: policy = Standard; return false;
        }
    }
}
