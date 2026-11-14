Library Loans - Final Project
Introduction to Functional Programming, academic year 2026-2027
Group IT-2505: Zholdas Aliya (leader), Ismagul Nuray, Abdurakhim Aliya, Nurlanov Adil
Repository: https://github.com/Alflotta/Library_loans.git
Branch: main
Final commit: fb0b3aa831f01ffa50928d02aa28aa1ec3720729

1. PROBLEM, USERS, GOALS

The program is a console application for a small library. It keeps members, books, loans, reservations and unpaid fines, applies the lending rules, charges fines for late returns, and produces two reports.
Users: the librarian (issues, returns, renews, reserves, takes payments, reads reports) and the front desk (looks up members, books and loans).
Goal: show the functional techniques of the course in one coherent application: pure core, pipeline, higher-order function, safe operation sequence, Result and Maybe, LINQ reports, lazy processing and safe file handling.

2. REQUIREMENTS FOR RUNNING

.NET SDK 10. No other package is needed except the test packages that dotnet restores (xunit).
Working directory: the folder that contains LibraryLoans.sln.
Build: dotnet build
Test: dotnet test
Run: dotnet run --project src/LibraryLoans.App
Optional arguments: dotnet run --project src/LibraryLoans.App -- [statePath] [yyyy-MM-dd] [standard, strict or native] [reportPath]
Defaults: statePath data/library_state.json, date today, policy standard, reportPath out/fine_report.txt. All paths are relative to the working directory.
For the demonstration below use: dotnet run --project src/LibraryLoans.App -- data/library_state.json 2026-11-14 standard out/fine_report.txt
The program changes data/library_state.json only when you choose 0 (Save and exit). Restore the file from the repository to repeat the demonstration.

3. HOW A USER WORKS WITH THE PROGRAM

The program loads the state file, then shows a numbered menu and reads one line for each answer.
1 Register member: member id
2 Add book: book id,copies
3 Check out a book: loan id,member id,book id,days
4 Return a book: loan id
5 Renew a loan: loan id
6 Reserve a book: member id,book id
7 Pay a fine: member id,amount
8 Show reports
9 Export reports to the report file
10 Look up: member,id or book,id or loan,id
11 Switch scoring configuration (reminder or escalation)
12 Run an operations script file (for example data/script.csv)
13 Fines report from a loans CSV file (for example data/loans_report.csv)
0 Save the state and exit
The business date for checkout, return and renewal is the date given on the command line. A wrong input is rejected with a message and the state does not change. If the input ends, the program exits without saving.

4. DOMAIN MODEL AND RULES

Models (immutable): Loan, OverdueLoan, FineLine, FineReport, MemberSummary, ReportBundle, BalanceLine, ValidatedCheckout, LoanState (Active or Returned), LibraryState (members, stock, loans, reservations, balances), Operation (seven cases), StateSnapshot with its parts.
Rules (LibraryRules.cs): loan period 1 to 30 days; at most 3 active loans per member; at most 2 renewals, each adds 7 days; a member whose unpaid balance is above 500 KZT cannot borrow.
Fine policies (FinePolicies.cs): standard 50 KZT per day capped at 1000, strict 100 KZT per day capped at 3000. The policy is a function from days late to fine and is passed into the workflow.
Reservation: possible only when no copy is available. The first member in the queue has priority: another member cannot take a free copy, and a loan cannot be renewed while somebody waits.
Fine: a late return adds the fine to the member balance. A payment reduces the balance and cannot be zero, negative or above the balance.

5. THE WORKFLOWS (9, the minimum for four students is 6)

1 Check out a book: validated request, member and book lookup, availability, loan limit, balance block, reservation priority (Aliya Zh).
2 Return with fine: late days, fine from the policy, balance update (Aliya Zh, rule written with Adil).
3 Renew a loan: not overdue, renewal limit, nobody waiting (Adil).
4 Reserve a book: only without a free copy, no duplicate, queue position (Adil).
5 Pay a fine and unblock the member (Adil).
6 Fines report by loan with a minimum fine filter (Nuray).
7 Debt report by member with a configurable score (Nuray).
8 Run an operations script lazily, step by step (Aliya A).
9 Save and load the whole state with validation (Aliya A).

6. DEMONSTRATION WITH THE SAMPLE DATA (date 2026-11-14, standard policy)

Choose 12 and type data/script.csv. Expected output:
 1. Loan L005 renewed, new due date 2026-11-22.
 2. REJECTED (LoanOverdue): Loan 'L001' is overdue since 2026-11-03 and cannot be renewed.
 3. Member Dana reserved 'Dune', queue position 1.
 4. Loan L001 returned on 2026-11-14. Fine charged: 550 KZT.
 5. Loan L002 returned on 2026-11-14.
 6. Loan L006 issued to Dana for 'Dune', due 2026-11-21.
 7. REJECTED (MemberBlocked): Member 'Aigerim' owes 550 KZT, above the limit of 500 KZT.
 8. Member Aigerim paid 100 KZT, remaining balance 450 KZT.
 9. Loan L007 issued to Aigerim for 'SICP', due 2026-11-21.
10. REJECTED (BookReserved): Book 'Clean Code' is reserved for 'Beck'.
11. Loan L008 issued to Beck for 'Clean Code', due 2026-11-21.
Script finished: 11 operations, accepted 8, rejected 3.
Then choose 8. Expected reports:
Report 1 (fines of at least 250 KZT): L001 Aigerim Clean Code 11 days 550 KZT, L003 Dana SICP 5 days 250 KZT, total 550 plus 250 = 800 KZT (2 loans).
Report 2: Aigerim 1 loan, 550 KZT, max 11 days, score 16; Dana 1 loan, 250 KZT, max 5 days, score 7 (reminder scoring). After choice 11 the scores are 66 and 30 (escalation) and the fines stay the same.
Unpaid balances: Aigerim 450 KZT, Dana 250 KZT.
Policy demonstration: start with strict as the third argument and return L001: the fine is 1100 KZT instead of 550.
Failure demonstrations: a missing state file (dotnet run --project src/LibraryLoans.App -- data/missing.json) prints "Operational problem [FileNotFound]" and exits with code 1; data/library_state_malformed.json gives [MalformedContent] (invalid JSON); data/library_state_inconsistent.json gives [MalformedContent] with the list of inconsistencies. In the menu, choosing 12 with data/operations_invalid.csv shows the malformed line and applies nothing.

7. ARCHITECTURE

Three projects and a test project.
LibraryLoans.Core: pure logic, no Console, no file access, no global state. Models, rules, states, operations, parsers of text lines, workflow, fine pipeline, reports, report formatting, Result and Maybe.
LibraryLoans.Boundary: side effects. FileBoundary (reading, exceptions to Result), ScriptStream (lazy file reader), StateStore (JSON), SafeFileWriter (safe write), MenuSession (menu with injected TextReader and TextWriter).
LibraryLoans.App: Program.cs, only wires Console, arguments and MenuSession.
LibraryLoans.Tests: xunit tests.
Data flow: file text to Boundary to Core parsers to immutable state to workflow (Result) to new state to reports to Boundary to file or console.

8. REQUIREMENT MAP

Functional core: Core project; immutable records and sealed classes; pattern matching in LibraryState.ReturnChecked and RenewChecked (switch with when clauses), LoanState.ToLoan, LibraryWorkflow.Execute.
Pipeline with three named stages: FineStages.SelectOverdue, FineStages.ApplyFines, FineStages.BuildReport, composed in FinePipeline.Run; FineWorkflow.Build uses the first two and then the reports.
Higher-order function that changes a business rule: the fine policy Func of int to int (FinePolicies.Standard, Strict, NoFine) passed to LibraryWorkflow.Execute, Apply, Steps, Run and FineWorkflow.Build; the scoring function Scoring.Reminder and Scoring.Escalation (curried, Scoring.Curried) passed to LoanReports.MemberSummaries.
Safe operation sequence: ValidatedCheckout can only be created by ValidatedCheckout.Create against a library state; LibraryState has a private constructor, so a state can only come from Empty, a checked transition or Restore; LoanState is Active or Returned, so a returned loan cannot be returned or renewed again; a checkout of a missing member is stopped before the book is checked; a copy goes to the head of the reservation queue.
Result and Maybe: Result.cs, Maybe.cs; Map and Bind in LibraryWorkflow.Execute (checkout chain: RequireMember, RequireAvailableCopies, Create then ToResult, IssueChecked then Map) and in the Checked methods; failure skips later stages. Domain failures as Result: MemberNotFound, BookNotFound, NoCopyAvailable, LoanLimitReached, MemberBlocked, BookReserved, LoanOverdue, RenewalLimitReached, InvalidPayment and others (ErrorCode.cs). Missing lookups as Maybe: FindMember, FindAvailableCopies, FindLoan. Boundary exceptions: FileBoundary.ReadLines, ScriptStream.OpenFile, SafeFileWriter.WriteLines, StateStore.Parse.
Extension methods used in the application: LoanQueryExtensions.WhereOverdue (used by FineStages.SelectOverdue), ResultCollections.CollectResults and MapError (used by FileBoundary.SnapshotOperations and StateStore), and the Result and Maybe extensions Map, Bind, Match, ToResult.
Two LINQ reports: LoanReports.LargeFines (Where, OrderByDescending, ThenBy, Sum) and LoanReports.MemberSummaries (GroupBy, Count, Sum, Aggregate, OrderByDescending, ThenBy); also LoanReports.UnpaidBalances.
Lazy processing: ScriptStream (yield return with using, file opened only when iterated, closed on success, failure, early stop and consumer exception) and LibraryWorkflow.Steps (yield return, each operation is applied only when the consumer asks for the next step). Deliberate materialisation: CollectResults in FileBoundary.SnapshotOperations and ToImmutableArray in LibraryWorkflow.Run.
Persistence: load and validate data/library_state.json (StateStore.Load, LibraryState.Restore), save it (StateStore.Save), export reports (SafeFileWriter.WriteLines), read CSV scripts and loan files. Sample files are in the data folder. Paths are relative or given as arguments. Missing files and malformed content are Result failures.
Curried function with two configurations: Scoring.Curried, CreateScore, Reminder, Escalation.

9. TESTS (128, written before the first run on the final code; run them with dotnet test)

Earlier stages: FinePipelineTests (16), LibraryWorkflowTests (17), ResultHandlingTests (16).
ReportsTests (21, pure): LargeFines and MemberSummaries normal, empty, no match, ties; scoring with two partial applications; WhereOverdue; CollectResults; MapError; report formatting.
FinalRulesTests (30, pure and domain): renewal, reservation and queue priority, fines from the policy, strict against standard, blocking at the boundary of 500, payments, snapshot round trip, restore validation, lazy Steps, parser commands, ParseLine, Build with two policies, balances.
ScriptStreamTests (15, lazy processing and files): nothing opens before iteration, materialised once, repeated iteration reads twice, disposal after success, early stop, malformed line and consumer exception, open failure, blank lines, missing file, malformed content, file handle released, safe writer.
MenuWorkflowTests (13, complete workflows and file handling): renew and late return saved and reloaded; the whole script with all rules and the saved state; a blocked member rejected and a consistent saved state; policy difference; reports; scoring switch; export and a failed export; lookups; invalid input; fines from a CSV file; missing state file; malformed JSON and inconsistent state; save and load round trip.
The requirement of at least 12 tests with 8 pure, 2 workflow and 2 file tests (missing file and malformed content) is met several times over.
Latest test outcome:  total 135, passed 135, failed 0

10. ERROR HANDLING

Domain failures are values: Result of T with Ok or Failure carrying an AppError (code, message, kind Domain or Operational). Operational errors are FileNotFound, FileUnreadable, MalformedContent and ExportFailed. Only the Boundary projects catch exceptions and only the listed file exceptions are caught; other exceptions are programming errors and are not hidden.
A failed operation returns the same state object, so nothing changes. A failed save or export never leaves a half written file (data is written to a temporary file and moved; on failure the old file stays).

11. RESOURCES AND MEMORY NOTE

ScriptStream owns the StreamReader and releases it with using. SafeFileWriter owns a StreamWriter in a using block. File.ReadAllLines in FileBoundary.ReadLines opens and closes the file inside the call. No caches, no static mutable collections and no events are used, so nothing grows over time. Data is materialised in the loaded state, the operation list of a script and the report results.

12. LIMITATIONS AND NOT IMPLEMENTED

Console only. One state file, no user accounts and no concurrent use. Dates for operations come from the command line, not from the system clock during a session. Fine amounts are whole KZT. The native library is built per platform; only the linux-x64 binary is included (see section 14).

13. CONTRIBUTIONS

Zholdas Aliya (IT-2505): Result, AppError, ErrorCode, Map, Bind, Match; LibraryWorkflow (Execute, Apply, Steps, Run, checkout chain); ValidatedCheckout; MenuSession and Program; native C library (native/libraryloans_native.c and .h, build.sh, build.ps1, NativeFineCalculator.cs, the "native" policy in Program.cs, ErrorCode.NativeLibraryUnavailable); workflows 1 and 2. Tests: MenuWorkflowTests (complete workflows and failure paths), the safe sequence tests and NativeIntegrationTests (7).
Ismagul Nuray (IT-2505): Maybe and lookups; LoanQueryExtensions.WhereOverdue, LoanReports, ReportFormat, Scoring (curried function and two configurations), FineWorkflow.Build; workflows 6 and 7. Tests: ReportsTests and the Build and balance tests.
Abdurakhim Aliya (IT-2505): FileBoundary, ScriptStream, SafeFileWriter, StateStore, StateSnapshot, LibraryState.ToSnapshot and Restore, OperationParser (RENEW, RESERVE, PAY, ParseLine), data files; workflows 8 and 9. Tests: ScriptStreamTests, snapshot, restore and parser tests, file tests in MenuWorkflowTests.
Nurlanov Adil (IT-2505): LibraryRules, renewals (RenewChecked), reservations (ReserveChecked and the queue priority in Violations), fine charging and balances (CompleteReturn, PayChecked), blocking; workflows 3, 4 and 5. Tests: renewal, reservation, fine, policy, blocking and payment tests in FinalRulesTests.
Earlier parts (parser of loan CSV, fine stages, validated checkout, Result model) keep the contributions listed in the earlier README files.

14. NATIVE C LIBRARY (P/INVOKE)

Owner: Zholdas Aliya (C source, header, build scripts, C# binding and adapter, native policy in Program.cs, NativeIntegrationTests).

Source: native/libraryloans_native.c and native/libraryloans_native.h. Exported functions: int ll_abi_version(void) and int ll_fine(int days_late, int per_day, int cap, int grace_days). ll_fine returns min((days_late - grace_days) * per_day, cap), 0 inside the grace period, -1 for a negative argument; the arithmetic uses 64 bits.
C# binding and adapter: LibraryLoans.Boundary/NativeFineCalculator.cs ([DllImport] class Native, TryCreatePolicy, Policy). It is in the Boundary project because loading a binary is a side effect; the Core only receives a Func of int to int.
Use in the application: the third fine policy "native" (60 KZT per day after 3 free days, cap 1500). It is a real policy for returns and for the fines reports: dotnet run --project src/LibraryLoans.App -- data/library_state.json 2026-11-14 native out/fine_report.txt
Documented difference on the same input (loan L001, 11 days late): standard 550 KZT, strict 1100 KZT, native 480 KZT.
Platform and compiler: Linux x64 (gcc, C99), Windows x64 (MinGW-w64 gcc or MSVC cl), macOS (clang or gcc; folder runtimes/osx-x64). The included binary is runtimes/linux-x64/native/libraryloans_native.so. On other platforms build it first.
Build: Linux or macOS: bash native/build.sh. Windows (PowerShell): powershell -File native/build.ps1. The result goes to runtimes/RID/native/ and the project files copy it to the output folder (runtimes/RID/native next to the program).
Loading: NativeFineCalculator.DefaultPath() uses the environment variable LIBRARYLOANS_NATIVE if it is set, otherwise runtimes/RID/native/ next to the program. TryCreatePolicy checks the file, loads it with NativeLibrary.TryLoad, installs a DllImport resolver and compares ll_abi_version with the expected value 1.
Controlled failures (Result, error code NativeLibraryUnavailable, Operational): file missing, file is not a native library, function missing, wrong ABI version. The program prints "Operational problem [NativeLibraryUnavailable]" and exits with code 1. Example: LIBRARYLOANS_NATIVE=/nonexistent dotnet run --project src/LibraryLoans.App -- data/library_state.json 2026-11-14 native
Resource ownership: the library handle belongs to the process and is released when the process ends, because the runtime caches the P/Invoke binding and unloading it earlier is unsafe. The native function keeps no state and allocates no memory, so nothing has to be freed.
Tests: NativeIntegrationTests (7): real native call, difference between three policies, native policy inside FineWorkflow.Build, missing file, invalid file, empty path, negative input.
