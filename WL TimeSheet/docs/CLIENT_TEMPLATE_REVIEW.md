# Client workbook and revised business-rule review

> Update, 1 October 2026: Les requested 1 unit = 1 minute and direct access at `/`. The current app preserves the entered clock-in minute and converts historical six-minute units through the MinuteUnits migration. The workbook observations below record the original template, not the current calculation rule.

Reviewed 21 September 2026. Source: `C:\Users\patrickb\source\repos\WL TimeSheet\Template\Updated Time Log Template Alphabetical Task Types.xlsx`. The original workbook was inspected read-only and has not been modified.

The client's newly supplied rules supersede conflicting assumptions in the initial brief. The revision is now implemented, including Windows identity, daily chained times, break totals, template import, administration unlock and exports. On 21 September 2026 the user authorized proceeding with configurable defaults and adjusting them after client feedback.

## Workbook evidence

The workbook has a visible `Sheet1` and a hidden `Task Type Lookups` sheet. The lookup sheet contains **38 task types** in rows 2–39. The named range `TaskTypesList` references column A of those rows.

| Location | Actual meaning / formula |
|---|---|
| Sheet1 B1:D1 (merged) | Display name; sample value Pat |
| Sheet1 B2:D2 (merged) | Work date; sample text 21/9/26 |
| Sheet1 F1 | Productive time, SUMIFS over durations excluding the two break labels |
| Sheet1 A3:G3 | Time Started, Time Finished, Units, Duration, Task Type, Description, Task Type Explanatory Notes |
| A4 | Manually entered first start; sample value 08:50, no rounding formula |
| B4 onward | Start + Units × TIME(0,6,0) |
| C4:C45 validation | Whole number greater than or equal to zero |
| D4 onward | Units × TIME(0,6,0) |
| A5 onward | Previous row's finish |
| G4 onward | VLOOKUP of explanatory notes from Task Type Lookups |

The two sample populated rows are Lunch Break entries of 10 and 2 units. Their total elapsed duration is 72 minutes, but productive time is zero. These are sample entries, not data to import into employee timesheets.

### Workbook defects to avoid reproducing

- F1 sums D4:D44 even though entry formulas extend through row 45. Productive work on row 45 would be omitted.
- Task-type validation covers E4:E44, leaving E45 without the dropdown.
- The example first start is 08:50, but the written six-minute round-down rule implies 08:48. The workbook itself does not implement that rounding.
- Break exclusion is based on exact task names. The app should use the explicit Break Flag, preserving historical break classification as necessary, rather than depend on editable names.

## Implemented changes from the original application

| Area | Original implementation | Implemented revision |
|---|---|---|
| Authentication | Keycloak OIDC and application employee mapping | Windows/RDS identity; automatic standard-user access without a separate login |
| Administration | Employee Admin role | Password-gated administration; secure salted hash in SQL; password change facility |
| Deployment | Linux production recommendation to avoid proprietary native SQL runtime | Windows 11 Pro host with IIS Windows Authentication; validate managed SQL driver deployment/support constraints |
| Daily record | Entries grouped by employee/date | Explicit single daily timesheet per Windows identity/date, enforced by unique index |
| Display name | Employee profile name | Defaults to Windows username, overridable for display without changing ownership |
| Units | Editable decimal independent of time | Nonnegative whole number, 1 unit = 6 minutes |
| Time entry | Independent start/finish inputs | First clock-in/start, then automatic duration, finish and chained next start |
| Editing earlier rows | Only that row's times change | Recalculate all subsequent row start/finish times atomically |
| Breaks | No break metadata or deduction | Configurable break flag; elapsed timeline includes breaks; productive totals exclude them |
| Description | Required for every task | Optional generally; configurable required detail for task types such as File Closures; phone-call guidance displayed |
| Task type catalogue | Empty until Admin creates types | Import the 38 supplied names/notes into SQL once; retain administrator control, not hard-coded UI options |
| Reporting | Excel list/date/calendar summaries | Add CSV with specified fields and productive totals; preserve Excel export using the supplied workbook layout |
| Editing cutoff | No automatic locking | Today and preceding three local dates editable; older days read-only; administrator can reopen for 24 hours |

The new password requirement is explicitly from the client and conflicts with the initial instruction not to build separate passwords. It applies to an **administration unlock**, not a new username/password system for employees. Use framework password hashing, antiforgery, attempt throttling and an expiring server-validated admin unlock. Supply the initial password via local secure configuration and store only its hash in SQL. Do not place its plaintext value in source, migrations or documentation. Ownership must remain tied to the authenticated Windows identity even when the display name is changed.

## Implemented data model

- Employees: stable Windows SID plus Windows account name, display profile and activation. Resolve the current HTTP Windows principal, never `Environment.UserName` or a client-supplied username.
- DailyTimesheets: EmployeeId + WorkDate unique, display name, entered clock-in and rounded start, edit/reopen metadata, audit fields and rowversion.
- TimeEntries: DailyTimesheetId, ordered position, integer units, TaskTypeId, description and any structured mandatory details. Recalculate chained times transactionally; do not accept client-calculated totals as authoritative.
- TaskTypes: add IsBreak and a configurable description/detail requirement, retaining notes, activity and audit metadata. Import workbook categories through a controlled idempotent importer.
- AdministrationSettings: hashed password plus change/version metadata; no plaintext password column. Reopening actions remain attributable to the Windows identity that unlocked administration.

Windows hosting requires IIS Windows Authentication with anonymous authentication disabled, a suitable HTTPS binding, and intranet/browser configuration for integrated authentication from RDS. A browser outside the trusted Windows environment can still show a Windows credentials prompt; no custom employee login page is needed. Validate the actual deployment identity and browser configuration before promising silent SSO for every client.

## Defaults approved for implementation, adjustable after feedback

1. First start: floor 08:50 to 08:48, following the written six-minute rule. Configure Timesheet:RoundStartDown to change this.
2. Edit window: today plus the preceding three local calendar dates. This is not a rolling 72-hour cutoff. Configure Timesheet:EditablePreviousDays; null allows all past dates. Older days can be reopened for Timesheet:ReopenHours (default 24).
3. Day boundary: allow a finish of 24:00, reject any duration beyond midnight; record next-day work on the next date.
4. Descriptions: optional generally, explicit file count required for File Closures, phone-call notes shown as guidance. Administrators can change each task type's detail rule.

Zero units are accepted, with equal start and finish. Editing or deleting a row shifts later times. Break classification is stored per entry, preserving historical productive totals when the task type changes. Export totals include every exported row, fixing the template's omitted-last-row defect. SQL contains the 38 source task types; sample timesheets were not imported.

## Local Keycloak setup status

The preceding local-development request downloaded isolated Keycloak 26.7.4 / Temurin 25 under ignored `artifacts/local-identity`, created a local realm and generated private credentials. One development administrator record was bootstrapped in SQL. This setup was paused when the client supplied the Windows/RDS requirement; it is not the target authentication design. Runtime downloads and generated credentials are excluded from application publication. No real WL account or timesheet data was imported.

The obsolete sign-in page, Keycloak setup scripts and OIDC browser test have been removed. The migrated legacy profile is inactive. The running application now resolves the current Windows account automatically; the initial administration password exists as a SQL hash and its bootstrap configuration has been removed.

## September 2026 UI workflow revision

List mode now uses a persistent blank inline row with keyboard entry and inline editing. Calendar keeps the existing drawer, reached through editable-day plus buttons or the single Day Overview header action. Selecting a date updates the overview without navigating or collapsing the month. Date navigation and filters share a compact toolbar; Excel/CSV appear beside Daily Timesheet. SQL pagination supports 10/20/50/100 rows (default 20), with totals across all matching records. The supplied logo and white/navy palette are retained, with local Lucide icons. Existing rules, permissions, exports and SQL schema remain in use.
