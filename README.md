Library Loans and Fines
Introduction to Functional Programming, 2026/2027


1. TOPIC, USERS, TEAM

Topic: calculating library fines for overdue book loans.
Intended users: librarians who need a fine report for all overdue loans at a given date.

Team members:
Zholdas Aliya, group IT-2505, models, LoanCsvParser, parser and validation tests.
Ismagul Nuray, group IT-2505, stage 1 SelectOverdue, boundary tests.
Abdurakhim Aliya, group IT-2505, stage 3 BuildReport, FinePipeline, console Program, empty dataset test.
Nurlanov Adil, group IT-2505, stage 2 ApplyFines, FinePolicies, purity tests, README and dataset.

2. INPUT FIELDS (CSV file data/loans.csv)

Header: LoanId,Member,Book,DueDate,ReturnDate

LoanId, Member, Book: non-empty text, no commas inside values. LoanId must be unique.
DueDate: date in format yyyy-MM-dd.
ReturnDate: date in format yyyy-MM-dd, or empty. Empty means the book is still out. This is a legitimate value, not an error.
The "today" date (asOf) is passed as a program argument. The pure code never reads the clock.


3. BUSINESS RULES

1. A loan is overdue only if its end date is strictly after the due date. End date is ReturnDate, or asOf if the book is not returned yet. Returning exactly on the due date is not overdue.
2. DaysLate = end date minus due date, in whole days.
3. The fine for DaysLate days is decided by a policy (a Func<int,int>):
   Standard: 50 KZT per day, capped at 1000.
   Strict: 100 KZT per day, capped at 3000.
4. The report is ordered by fine descending, ties broken by LoanId, and contains the total fine.


4. DATASET AND EXPECTED RESULTS (written before the first run)

asOf = 2026-10-20

L001, due 2026-10-10, returned 2026-10-15: 5 days late. Standard 250, Strict 500. Normal overdue case.
L002, due 2026-10-12, returned 2026-10-12: 0 days. Rejected by both policies. Boundary: returned exactly on the due date.
L003, due 2026-10-05, not returned: 15 days late. Standard 750, Strict 1500. Uses asOf.
L004, due 2026-09-01, not returned: 49 days late. Standard 1000 (cap), Strict 3000 (cap). Policies differ most here.
L005, due 2026-10-18, returned 2026-10-16: 0 days. Rejected by both policies. Returned early.
L006, due 2026-10-19, not returned: 1 day late. Standard 50, Strict 100. Boundary: minimum overdue.

Standard report: L004 1000, L003 750, L001 250, L006 50. Total 2050.
Strict report: L004 3000, L003 1500, L001 500, L006 100. Total 5100.
Empty dataset (data/loans_empty.csv, header only): empty report, total 0.
Invalid dataset (data/loans_invalid.csv): rejected with line-numbered errors, no report.


5. PIPELINE STAGES

Workflow (FinePipeline.Run): BuildReport(ApplyFines(SelectOverdue(loans, asOf), policy))

Stage 1: FineStages.SelectOverdue
Input: IEnumerable<Loan> and DateOnly asOf. Output: ImmutableArray<OverdueLoan>.
Rule: keep loans with DaysLate of at least 1.
Example: L001 (due 10-10, returned 10-15) becomes OverdueLoan(L001, 5). L002 is dropped.

Stage 2: FineStages.ApplyFines
Input: ImmutableArray<OverdueLoan> and Func<int,int>. Output: ImmutableArray<FineLine>.
Rule: fine = policy(DaysLate).
Example: 5 days with the Standard policy gives FineLine(L001, ..., 250).

Stage 3: FineStages.BuildReport
Input: ImmutableArray<FineLine>. Output: FineReport.
Rule: order by fine descending, then by LoanId, and sum the total.
Example: 4 lines give a report with total 2050.

Parsing is a separate boundary step: LoanCsvParser.Parse(IEnumerable<string>?) returns ParseOutput, which holds either valid Loan records or error messages.


6. CONFIGURABLE RULE (HIGHER-ORDER FUNCTION)

Higher-order function: FineStages.ApplyFines(overdue, Func<int,int> fineForDaysLate). The delegate is called once per overdue loan.
Two policies: FinePolicies.Standard and FinePolicies.Strict.
The policy is selected in Program.cs (FinePolicies.TryGet) and passed as an argument. There is no mutable global setting.
Same dataset and same pipeline code: Standard total 2050, Strict total 5100.


7. BUILD, RUN, TEST

Prerequisite: .NET SDK 8.0. Run all commands from the folder that contains LibraryLoans.sln.

Build:
    dotnet build LibraryLoans.sln

Run tests:
    dotnet test LibraryLoans.sln

Run the program (normal case, both policies):
    dotnet run --project src/LibraryLoans.App -- data/loans.csv 2026-10-20

Failure and edge demonstrations:
    dotnet run --project src/LibraryLoans.App -- data/loans_invalid.csv 2026-10-20
    dotnet run --project src/LibraryLoans.App -- data/loans_empty.csv 2026-10-20
    dotnet run --project src/LibraryLoans.App -- data/does_not_exist.csv 2026-10-20

The first argument is the CSV path, the second is the date in yyyy-MM-dd format. If the date is omitted, today's date is used.


8. FUNCTIONAL CORE AND I/O

LibraryLoans.Core has no Console, file, clock or random access. All models are records and stage outputs are ImmutableArray, so later stages cannot change earlier results. Input collections are only read, never modified.
LibraryLoans.App/Program.cs does all I/O: arguments, DateTime.Today (only when no date is given), File.ReadAllLines and Console output.
There is no null-forgiving operator in application code. Invalid fields are rejected with messages and are never replaced by defaults.


9. REQUIREMENT MAP AND TESTS

Task 1, workflow and dataset: sections 1 to 4, data/loans.csv.
Task 2, models and validation: Models.cs, LoanCsvParser.cs.
Task 3, three stages: FineStages.cs, FinePipeline.cs.
Task 4, configurable rule: FineStages.ApplyFines, FinePolicies.cs, Program.cs.
Task 5, purity: the Core project, test Stages_DoNotMutateCallerOwnedCollections.

Tests are in tests/LibraryLoans.Tests/FinePipelineTests.cs (xUnit, 16 tests):
Normal workflow: NormalWorkflow_StandardPolicy_ProducesDocumentedReport, Stage1_SelectOverdue_ReturnsOnlyLateLoansWithDaysLate.
Empty dataset: EmptyDataset_ProducesEmptyReport_NotAnError.
Boundary value: Boundary_ReturnedOnDueDate_IsNotOverdue_OneDayLateIsFined, Boundary_FineCapIsReachedExactlyAtItsThreshold.
Invalid and missing input: Parser_RejectsNullInput_WithoutFabricatingLoans, Parser_RejectsMalformedRows_AndReturnsNoLoansAtAll, Parser_RejectsMissingHeader_AndEmptyInput, Parser_EmptyReturnDate_MeansStillOut_NotAnError, Stages_RejectNullArguments.
Two policies: TwoPolicies_SameInput_GiveDifferentDocumentedResults, HigherOrderStage_InvokesThePolicyDelegate_OncePerOverdueLoan, PolicyLookup_KnownAndUnknownNames.
Purity and stages: Stages_DoNotMutateCallerOwnedCollections, Stage3_BuildReport_BreaksTiesByLoanId_AndIsDeterministic, SamePolicyAndInput_AlwaysGiveSameOutput.

Latest test outcome: Passed 16, Failed 0.


10. CONTRIBUTIONS

10. CONTRIBUTIONS

Student 1: Zholdas Aliya
Implemented logic: the input models Loan and ParseOutput (Models.cs) and the CSV parser LoanCsvParser.Parse (LoanCsvParser.cs). The parser validates all fields at the application boundary, distinguishes missing input (null), a valid empty dataset (header only) and malformed rows, treats an empty ReturnDate as "still out", and returns no loans at all if any row is invalid.
Tests: Parser_RejectsNullInput_WithoutFabricatingLoans, Parser_RejectsMalformedRows_AndReturnsNoLoansAtAll, Parser_RejectsMissingHeader_AndEmptyInput, Parser_EmptyReturnDate_MeansStillOut_NotAnError.
Documentation: input fields (README section 2), invalid sample file data/loans_invalid.csv.

Student 2: Ismagul Nuray
Implemented logic: stage 1 of the pipeline, FineStages.DaysLate and FineStages.SelectOverdue (FineStages.cs). It computes whole days late using an explicit asOf date and keeps only loans that are strictly overdue, so returning exactly on the due date is not fined.
Tests: Stage1_SelectOverdue_ReturnsOnlyLateLoansWithDaysLate, Boundary_ReturnedOnDueDate_IsNotOverdue_OneDayLateIsFined, Stages_RejectNullArguments, SamePolicyAndInput_AlwaysGiveSameOutput.
Documentation: business rules (README section 3), dataset and expected results written before the first run (README section 4), data/loans.csv.

Student 3: Abdurakhim Aliya
Implemented logic: stage 3, FineStages.BuildReport (ordering by fine descending with a LoanId tiebreaker, and the total), the composed workflow FinePipeline.Run (FinePipeline.cs), and the console application Program.cs, which performs all input and output (arguments, file reading, error messages, printing the reports for both policies).
Tests: NormalWorkflow_StandardPolicy_ProducesDocumentedReport, EmptyDataset_ProducesEmptyReport_NotAnError, Stage3_BuildReport_BreaksTiesByLoanId_AndIsDeterministic.
Documentation: topic and users (README section 1), build, run and test instructions (README section 7), data/loans_empty.csv.

Student 4: Nurlanov Adil
Implemented logic: stage 2, FineStages.ApplyFines (the higher-order function that calls the policy delegate once per overdue loan), and the two fine policies Standard and Strict with the policy lookup FinePolicies.TryGet (FinePolicies.cs). The policy is chosen outside the pipeline and passed as an argument.
Tests: Boundary_FineCapIsReachedExactlyAtItsThreshold, TwoPolicies_SameInput_GiveDifferentDocumentedResults, HigherOrderStage_InvokesThePolicyDelegate_OncePerOverdueLoan, PolicyLookup_KnownAndUnknownNames, Stages_DoNotMutateCallerOwnedCollections.
Documentation: pipeline stages (README section 5), configurable rule (README section 6), functional core and I/O separation (README section 8), requirement map and test list (README section 9).
