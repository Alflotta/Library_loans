namespace LibraryLoans.Core;

public static class LibraryRules
{
    public const int MinLoanDays = 1;
    public const int MaxLoanDays = 30;
    public const int MaxActiveLoans = 3;
    public const int MaxRenewals = 2;
    public const int RenewalDays = 7;
    public const int BlockingBalance = 500;
}
