namespace LibraryLoans.Core;

public enum ErrorCode
{
    Unknown,
    EmptyValue,
    InvalidPeriod,
    InvalidCopies,
    MemberNotFound,
    BookNotFound,
    LoanNotFound,
    DuplicateLoanId,
    DuplicateMember,
    DuplicateBook,
    NoCopyAvailable,
    LoanLimitReached,
    AlreadyReturned,
    ReturnBeforeLoan,
    LoanOverdue,
    RenewalLimitReached,
    BookReserved,
    AlreadyReserved,
    CopyAvailable,
    MemberBlocked,
    InvalidPayment,
    FileNotFound,
    FileUnreadable,
    MalformedContent,
    ExportFailed,
    NativeLibraryUnavailable
}

public enum ErrorKind
{
    Domain,
    Operational
}
