Library Loans and Fines
Assignment 6: Handling Temporal Coupling
Introduction to Functional Programming, 2026/2027
1. TOPIC, USERS, TEAM

Topic: a library desk. Members borrow books, return them, and pay a fine for late returns.
Intended users: librarians who process checkouts and returns and need a fine report at a given date.
The project continues Assignment 5. The fine pipeline from Assignment 5 (SelectOverdue, ApplyFines, BuildReport and the two policies Standard and Strict) is kept unchanged and is now fed by the new checkout and return workflow.

Team members:
Zholdas Aliya, group IT-2505
Ismagul Nuray, group IT-2505
Abdurakhim Aliya, group IT-2505
Nurlanov Adil, group IT-2505


2. WORKFLOW OVERVIEW

The chosen operation is "checking a book out" and the operations around it (return, register member, add book).
A script file lists operations in order. Each operation is parsed, then applied to an immutable library state. Every operation returns a new state or a clear rejection, and the old state is never modified. At the end, the loans of the final state go through the Assignment 5 fine pipeline.

Operations (one per line of the script, comma separated):
MEMBER,memberId
BOOK,bookId,copies
CHECKOUT,loanId,memberId,bookId,loanDate,days
RETURN,loanId,returnDate
Dates use the format yyyy-MM-dd.


3. TASK 1: THE HIDDEN DEPENDENCY (BEFORE AND AFTER)

UNSAFE EXAMPLE (documentation only, it is not part of the project and not on any application path):

    var desk = new CheckoutDesk();
    desk.LoadCatalog(catalog);
    desk.SelectMember("Beck");
    desk.ChooseDays(14);
    desk.Confirm("L101", "Clean Code");

CheckoutDesk is a mutable object. LoadCatalog, SelectMember and ChooseDays fill private fields, and Confirm silently reads those fields.

Prerequisites and required order: LoadCatalog first, SelectMember second, ChooseDays third, Confirm last.

What goes wrong when the order is changed or a step is omitted:
If LoadCatalog is forgotten, the stock field is still empty, so Confirm either crashes or issues a book that does not exist.
If SelectMember is forgotten, the member field is still null and the loan is issued to nobody.
If ChooseDays is forgotten, the days field is still 0, so the due date equals the loan date and the member is charged a fine from the next day for a loan that never had a real period.
If Confirm is called twice, the copy counter goes down twice and the stock becomes negative.
Nothing in the method signatures shows these requirements, so the compiler cannot help and the mistakes appear as wrong loans and wrong amounts.

SAFE VERSION (the real code, LibraryWorkflow.ApplyCheckout):

    var checkout = ValidatedCheckout.Create(request, state);
    return checkout switch
    {
        CheckoutOutcome.Valid valid => state.Issue(valid.Value),
        CheckoutOutcome.Invalid invalid => rejected with the reasons,
    };

Issue accepts only a ValidatedCheckout, which cannot be created without passing the factory, so the prerequisite is visible in the signature. The state is explicit: Issue works on one immutable LibraryState and returns the next one. Repeated and invalid calls get a clear rejection.


4. INPUT FILES

data/operations.csv: the main demonstration script (18 operations, valid and invalid).
data/operations_invalid.csv: a script with malformed lines, rejected by the parser with line numbers.
data/loans.csv, data/loans_empty.csv, data/loans_invalid.csv: the Assignment 5 files for the fines mode.


5. DOMAIN RULES

A checkout is allowed only if all of these hold:
1. The loan id is not empty and not used before.
2. The member is registered.
3. The member has fewer than 3 active loans (LibraryRules.MaxActiveLoans).
4. The book exists in the catalog.
5. At least one copy of the book is available (stock minus active loans of that book).
6. The loan period is from 1 to 30 days inclusive (LibraryRules.MinLoanDays and MaxLoanDays).
The due date is the loan date plus the number of days.
A return is allowed only if the loan exists, is still active, and the return date is not before the loan date. A return on the loan date itself is allowed.
Fines (Assignment 5): Standard is 50 per day with a cap of 1000, Strict is 100 per day with a cap of 3000. A loan is late only if the end date (return date, or the explicit date "as of" if not returned) is after the due date.


6. TASK 2: THE FACTORY

ValidatedCheckout.Create(CheckoutRequest request, LibraryState library) returns a CheckoutOutcome.
It runs these checks and collects every failed reason, not only the first: empty loan id, member id or book id, loan period outside 1 to 30, loan id already used, member not registered, member at the loan limit, book not in the catalog, no copy available.
Only if no reason was found does it call the private constructor and return CheckoutOutcome.Valid. Otherwise it returns CheckoutOutcome.Invalid with all the reasons, so a half-built value never escapes.

Access restrictions:
ValidatedCheckout is a sealed class with a private constructor and read-only properties. Outside the class nobody can call new ValidatedCheckout, set a property, or use a with-expression to copy it with other values (it is a class, not a record).
The only way to get one is the factory method inside the class, which checks the rules first.
The factory is used by the real workflow (LibraryWorkflow.ApplyCheckout) for every CHECKOUT operation.


7. TASK 3: THE DISTINCT CASES

CheckoutOutcome (closed hierarchy): Valid carries a ValidatedCheckout. Invalid carries the list of reasons.
LoanState (closed hierarchy): Active carries the validated checkout of a loan that is still out. Returned carries the validated checkout and the return date.
TransitionOutcome (closed hierarchy): Applied carries the next LibraryState and a message. Rejected carries the reason.
Operation (closed hierarchy): RegisterMember, AddBook, Checkout, Return, each with its own data.
Every hierarchy is closed: the base constructor is private protected, so no other case can be added from outside. Each case carries only the data that belongs to it, and there are no boolean flags with unrelated editable fields. The constructors of Active and Returned are internal, so only the transitions inside the library can create them.


8. TASK 4: PATTERN MATCHING IN THE REAL PATHS

LibraryWorkflow.ApplyCheckout: switch on the CheckoutOutcome. Valid goes to Issue, Invalid becomes a rejection with the reasons.
LibraryWorkflow.Apply: switch on the Operation (all four cases), then switch on the TransitionOutcome. Applied produces the next state, Rejected keeps the old state. The fallback arm of every switch rejects an unexpected case and never treats it as valid.
LibraryState.Return: switch on the LoanState. Returned is rejected as a repeat, Active with an early return date is rejected, Active with a good date becomes Returned.
LoanState.ToLoan: switch on the state to build the Loan record for the fine pipeline.
No casts and no null-forgiving operator are used to read the data of a case.


9. TASK 5: SAFE OPERATIONS

Explicit prerequisites: Issue takes a ValidatedCheckout, not a raw request. Return and the other transitions check their prerequisites and return Rejected with a reason.
Issue checks the state again, because a value could have been validated against an older state (for example two validated checkouts for the last copy). The second Issue is rejected.
Pure transitions: LibraryState uses immutable collections and every transition returns a new LibraryState. Nothing is changed during validation, and all prerequisites are checked before the new state is built.
Failure keeps the state: a rejected operation returns the same state object that was passed in.

Repeated operations (documented safe rules):
Issue the same validated checkout twice: the second is rejected (loan id already used).
Return the same loan twice: the second is rejected (already returned), the first return date stays.
Register the same member twice or add the same book twice: rejected, state unchanged.


10. EXPECTED RESULTS (written before the first run)

Command: library data/operations.csv 2026-10-20
The script has 18 operations. Expected: 11 accepted and 7 rejected.
Line 1 MEMBER Aigerim: accepted.
Line 2 MEMBER Beck: accepted.
Line 3 MEMBER Dana: accepted.
Line 4 MEMBER Beck: rejected, already registered (repeated operation).
Line 5 BOOK Clean Code, 1 copy: accepted.
Line 6 BOOK Dune, 2 copies: accepted.
Line 7 BOOK SICP, 1 copy: accepted.
Line 8 CHECKOUT L101 Aigerim, Clean Code, 2026-10-01, 14 days: accepted, due 2026-10-15.
Line 9 CHECKOUT L102 Beck, Clean Code: rejected, no copy available.
Line 10 CHECKOUT L103 Beck, Dune, 2026-10-02, 30 days: accepted (boundary of the period), due 2026-11-01.
Line 11 CHECKOUT L104 Dana, SICP, 31 days: rejected, period must be between 1 and 30 (boundary).
Line 12 CHECKOUT L105 Zhan, Dune: rejected, member not registered.
Line 13 CHECKOUT L107 Dana, SICP, 2026-10-01, 7 days: accepted, due 2026-10-08.
Line 14 RETURN L103 on 2026-10-01: rejected, before the loan date 2026-10-02.
Line 15 RETURN L101 on 2026-10-20: accepted (5 days late).
Line 16 RETURN L101 on 2026-10-21: rejected, already returned on 2026-10-20 (repeated operation).
Line 17 RETURN L999: rejected, the loan does not exist (missing prerequisite).
Line 18 CHECKOUT L106 Aigerim, Clean Code, 2026-10-20, 14 days: accepted (the copy is back after line 15), due 2026-11-03.

Final state: loans L101 (returned), L103 (active), L106 (active), L107 (active).
Fines as of 2026-10-20:
Standard: L107 12 days 600, L101 5 days 250. Total 850.
Strict: L107 12 days 1200, L101 5 days 500. Total 1700.
L103 and L106 are not late.

Command: library data/operations_invalid.csv 2026-10-20
Expected: the file is rejected, nothing is processed, exit code 1, with 5 errors:
Line 2 Copies 'two' is not a whole number.
Line 3 Date '10/10/2026' is not a valid yyyy-MM-dd date.
Line 4 RETURN expects 3 fields but found 2.
Line 5 Unknown command 'FLY'.
Line 6 Days 'seven' is not a whole number.


11. BUILD, RUN, TEST

SDK: .NET 10 SDK (target framework net10.0). Check with: dotnet --version
Working directory: the folder that contains LibraryLoans.sln.

Build:
    dotnet build LibraryLoans.sln

Test:
    dotnet test LibraryLoans.sln

Run the new workflow (valid and invalid operations, then fines):
    dotnet run --project src/LibraryLoans.App -- library data/operations.csv 2026-10-20

Failure demonstrations:
    dotnet run --project src/LibraryLoans.App -- library data/operations_invalid.csv 2026-10-20
    dotnet run --project src/LibraryLoans.App -- library data/does_not_exist.csv 2026-10-20

Assignment 5 fines mode (kept):
    dotnet run --project src/LibraryLoans.App -- fines data/loans.csv 2026-10-20
    dotnet run --project src/LibraryLoans.App -- fines data/loans_invalid.csv 2026-10-20
    dotnet run --project src/LibraryLoans.App -- fines data/loans_empty.csv 2026-10-20

Arguments: mode (library or fines), CSV path, date in yyyy-MM-dd. If the date is omitted, today's date is used.


12. CALLS PREVENTED AT COMPILE TIME

These calls do not compile, so they are described here and not included in the project:
new ValidatedCheckout(...) from outside the class: the constructor is private.
Setting a property of a validated checkout, or copying it with a with-expression: the properties are read-only and the type is a class.
LibraryState.Issue(request) with a raw CheckoutRequest: Issue accepts only a ValidatedCheckout.
new LoanState.Active(...) or new LoanState.Returned(...) outside the Core project: the constructors are internal.
Adding a new case to CheckoutOutcome, LoanState, TransitionOutcome or Operation from outside: the base constructors are private protected.
Adding or removing items of LibraryState.Loans, Members or Stock in place: they are immutable collections without public mutators.
The runtime checks (factory rules, transition rejections, no public constructors) are covered by tests.


13. FUNCTIONAL CORE AND I/O

LibraryLoans.Core has no Console, file, clock or random access. All decisions and calculations are in pure functions and immutable data. The date "as of" is passed as a parameter.
LibraryLoans.App/Program.cs does all I/O: arguments, DateTime.Today (only when no date is given), reading the file, printing.
Nothing is written to disk. The state lives in memory and failed operations leave it unchanged.


14. REQUIREMENT MAP

Task 1: section 3 of this file.
Task 2: Requests.cs, LibraryRules.cs, ValidatedCheckout.cs (ValidatedCheckout.Create).
Task 3: ValidatedCheckout.cs (CheckoutOutcome), LoanState.cs, TransitionOutcome.cs, Operation.cs.
Task 4: LibraryWorkflow.cs (Apply, ApplyCheckout), LibraryState.cs (Return), LoanState.cs (ToLoan).
Task 5: LibraryState.cs (Issue, Return, RegisterMember, AddBook, StateViolations), LibraryWorkflow.cs (Run).
Parsing: OperationParser.cs. Console demonstration: Program.cs. Assignment 5 pipeline: FineStages.cs, FinePolicies.cs, FinePipeline.cs, LoanCsvParser.cs, Models.cs.


15. TESTS

Tests (xUnit, 33 in total: 16 from Assignment 5 in FinePipelineTests.cs and 17 new in LibraryWorkflowTests.cs).

New tests and the required case they cover:
Factory success: Factory_ValidRequest_CreatesUsableValidatedCheckout
Factory rejection: Factory_RejectsInvalidDays_AndAcceptsBoundaryDays, Factory_RejectsUnknownMemberUnknownBookAndEmptyId, Factory_RejectsWhenNoCopyIsAvailable, Factory_EnforcesMemberLoanLimit_AtBoundary
No unchecked construction: ValidatedValuesAndStates_HaveNoPublicConstructors
Distinct cases: DistinctCases_AreReachableAndMatched, OperationParser_ParsesAllCommandsInOrder
Normal operation: NormalOperation_ScriptProducesDocumentedStatesAndFines
Missing prerequisite: MissingPrerequisite_ReturnOfUnknownLoan_IsRejected, StaleValidation_IssueChecksPrerequisitesAgain
Repeated operation: RepeatedOperations_FollowDocumentedRules
Boundary value: Factory_RejectsInvalidDays_AndAcceptsBoundaryDays (1 and 30 days), Factory_EnforcesMemberLoanLimit_AtBoundary (third and fourth loan), Return_BeforeLoanDate_IsRejected_ReturnOnLoanDateIsAccepted
State preservation: FailedOperations_PreserveOriginalState, Return_BeforeLoanDate_IsRejected_ReturnOnLoanDateIsAccepted
Catalog rules: Catalog_AddBookRejectsInvalidCopiesEmptyIdAndDuplicates
Parser: OperationParser_RejectsMalformedLinesWithLineNumbers, OperationParser_RejectsNullInput_AndAcceptsEmptyInput

Latest test outcome:Passed 33, Failed 0.


16. CONTRIBUTIONS

Zholdas Aliya (group IT-2505)
Implemented logic: CheckoutRequest and ReturnRequest (Requests.cs), LibraryRules.cs, the factory ValidatedCheckout.Create and the CheckoutOutcome cases Valid and Invalid (ValidatedCheckout.cs). Explains why the constructor is private and how the factory collects every reason before returning a value.
Tests: Factory_ValidRequest_CreatesUsableValidatedCheckout, Factory_RejectsInvalidDays_AndAcceptsBoundaryDays, Factory_RejectsUnknownMemberUnknownBookAndEmptyId, Factory_RejectsWhenNoCopyIsAvailable, Factory_EnforcesMemberLoanLimit_AtBoundary, ValidatedValuesAndStates_HaveNoPublicConstructors.
Documentation: sections 5, 6 and 12.

Ismagul Nuray (group IT-2505)
Implemented logic: LoanState (Active, Returned, ToLoan) in LoanState.cs, and the transitions LibraryState.Issue and LibraryState.Return, including the pattern matching on LoanState and the rules for repeated and early returns.
Tests: MissingPrerequisite_ReturnOfUnknownLoan_IsRejected, Return_BeforeLoanDate_IsRejected_ReturnOnLoanDateIsAccepted, RepeatedOperations_FollowDocumentedRules, StaleValidation_IssueChecksPrerequisitesAgain.
Documentation: sections 7 and 9.

Abdurakhim Aliya (group IT-2505)
Implemented logic: Operation (Operation.cs), OperationParser (OperationParser.cs), LibraryWorkflow with Apply, ApplyCheckout and Run (pattern matching on Operation, CheckoutOutcome and TransitionOutcome), the console modes in Program.cs, and the files data/operations.csv and data/operations_invalid.csv.
Tests: DistinctCases_AreReachableAndMatched, OperationParser_ParsesAllCommandsInOrder, OperationParser_RejectsMalformedLinesWithLineNumbers, OperationParser_RejectsNullInput_AndAcceptsEmptyInput.
Documentation: sections 2, 4, 8 and 11.

Nurlanov Adil (group IT-2505)
Implemented logic: TransitionOutcome (TransitionOutcome.cs), the immutable LibraryState with Empty, RegisterMember, AddBook, AvailableCopies, ActiveLoanCount, StateViolations and ToLoans (the link to the Assignment 5 fine pipeline).
Tests: NormalOperation_ScriptProducesDocumentedStatesAndFines, FailedOperations_PreserveOriginalState, Catalog_AddBookRejectsInvalidCopiesEmptyIdAndDuplicates.
Documentation: sections 1, 3, 10, 13, 14 and 15.

