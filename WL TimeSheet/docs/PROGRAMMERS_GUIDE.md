# Programmer and Windows deployment guide

## Code map

| Location | Responsibility |
|---|---|
| Program.cs | Services, Negotiate, authorization, antiforgery, throttling, explicit initialization |
| Models/Domain.cs; Data/AppDbContext.cs | Domain, input validation, constraints |
| Security/ | Windows profile resolution and administration unlock |
| Services/TimesheetService.cs | Ownership, transactions, rowversions, chained times |
| Services/TimesheetPolicy.cs | Time zone, rounding, edit window, midnight limit |
| Services/TemplateImporter.cs; ExcelExportService.cs | Catalogue import, template Excel, CSV |
| Controllers/, Views/, wwwroot/ | MVC/API and responsive browser UI |

## Administration paging

Admin timesheet exports use the separate, administration-protected `GET /api/timesheets/admin-export` endpoint with the existing date, employee and task filters and `format=xlsx|csv`. Excel uses `Templates/WL-AdminExport.xlsx`, supplied by the client on 23 September 2026. It combines all matching employees and dates into one table: Employee, Date, Time Started, Time Finished, Units, Duration, Task Type, Description. CSV has the same columns. Excel preserves the template headings and widths, removes example data, and expands for longer reports; dates and durations are numeric Excel values. Exports include all matching rows, subject to the existing export limit, regardless of pagination. The personal daily template export remains separate.

Task Types and Employees use `GET /api/task-types/page` and `GET /api/employees/page`. Both require unlocked administration and accept `search`, `page` (default 1), and `pageSize` (10, 20, 50 or 100; default 20). SQL filters and counts the full directory before applying ordered Skip/Take. Responses contain `rows`, `page`, `pageSize`, `pages` and `total`. Search and page-size changes reset the UI to page one; saves refresh the current page, which is clamped if necessary. Directory records scroll within the panel, with compact pagination below. Existing unpaged lookup endpoints remain available for task pickers and employee selectors.

## Timesheet UI and paging

`wwwroot/js/site.js` supplies shared HTTP/error helpers and administration screens. `wwwroot/js/timesheet.js` owns the list/calendar workspace. Both entry surfaces share the searchable task picker and duration preview. Dropdown options display Task Name (Explanatory Notes), with long notes wrapping and no separate note beneath the input; `TimesheetService` remains authoritative for validation, permissions and timeline calculations.

Display name and clock-in save on field blur (or Enter), with validation and visible saving/saved/error status. Requests are serialized, edits typed during a save are preserved, and failures retain values for retry. The daily save button is removed. Task rows still use Enter or their save action. List mode keeps one unsaved row below the scrolling records, outside pagination. Units → Tab → Task type → Tab → Description → Enter (or Tab from the final field) saves without navigation, resets the row and restores Units focus. File-closure tasks include their required count before final Tab-to-save. The four totals cards follow the timesheet workspace. Validation retains input, each draft has a submission UUID, and saving disables repeat submission. Existing rows edit inline. Calendar alone uses the existing task drawer; each editable current-month date has a separate plus button. Day-body selection updates the overview without hiding or rebuilding the calendar grid. The loading indicator is positioned outside document flow so requests do not collapse the calendar and change scroll position.

`GET /api/timesheets` accepts `page` (default 1) and `pageSize` (10, 20, 50 or 100; default 20). It returns `rows`, `page`, `pageSize`, `pages`, `total` and aggregates over the full filtered selection. SQL applies ordered Skip/Take; a correlated sum over all preceding daily entries preserves start times through pagination and task filters. Day detail uses a SQL aggregate. Monthly summaries contain all month dates, editability and at most two task-name previews per day. Selected-day overview is paginated separately. Export endpoints retain the full filtered template/CSV workflow and existing export size limit. No schema migration is required for this UI revision.

Lucide 0.468.0 SVG icons are served locally from `wwwroot/icons/lucide` through CSS masks. Preserve the bundled license when publishing. There is no icon CDN or client package runtime.

## Local setup

Install .NET 10 SDK and SQL Server. Configure ConnectionStrings:Timesheet in user secrets. Local SQL uses integrated Windows credentials and loopback TCP. TrustServerCertificate is local-only; production needs a trusted SQL certificate.

Restore packages. Supply Bootstrap:AdminPassword through protected configuration and run `dotnet run -- --initialize --import-template`. Remove that value after the hash is initialized. Run `dotnet run --launch-profile https`. Normal startup never changes schema.

Do not add header-trusted identity or a development-login bypass to production.

Kestrel explicitly uses HTTP/1.1 so browsers can complete the Windows authentication handshake. IIS handles its own server authentication. The published web.config enables Windows Authentication and disables anonymous access; install the IIS feature and allow these site settings before deployment. See Microsoft's [Windows Authentication hosting guide](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0).

## Adjusting defaults

Clock-in uses the entered minute; the old RoundStartDown setting is ignored. Timesheet:EditablePreviousDays=3; ReopenHours=24; TimeZone=Australia/Adelaide. EditablePreviousDays=null permits unrestricted past dates. ReopenHours supports 1–168. Restart after changing configuration. Stored starts stay stable until daily details are saved again.

The October 2026 root/minute release requires `20261001060914_MinuteUnits`, unlike the earlier UI-only revision. Existing units are converted once to preserve elapsed time. `/` renders the workspace; `/timesheet` redirects to `/`. See the deployment README for the required database backup and upgrade procedure.

The editing window is today plus three preceding local dates, not a rolling 72 hours. An exact hourly rule needs an agreed reference timestamp. Finish at midnight is allowed; overnight work belongs on the next date. Administrators configure task description requirements and break status.

## Windows 11 Pro / IIS

1. Install IIS including Windows Authentication and the .NET 10 Hosting Bundle; restart IIS. Use an appropriately licensed production SQL Server edition (Developer edition is development-only).
2. Publish into a clean directory: `dotnet publish -c Release -o artifacts/windows-publish`. Deploy all output, including Templates/WL-TimeLog.xlsx.
3. Create a dedicated application pool (No Managed Code) and HTTPS site with the real intranet hostname and an RDS-trusted certificate. Restrict access to the intended intranet.
4. Enable Windows Authentication; disable Anonymous Authentication on the site. Configure browser intranet policies and any required SPN for the chosen hostname/service identity. Test two distinct RDS users.
5. Configure AllowedHosts, ConnectionStrings__Timesheet and policy settings in protected server configuration. Grant the service identity necessary database DML access; use a separate deployment identity for migration DDL. SQL uses service identity, not employee impersonation.
6. Apply migrations/import before starting the site. Supply Bootstrap__AdminPassword only for first initialization, then remove it. Persist/protect Data Protection keys for the pool identity. Configure logging and SQL backup/restore.
7. Verify own-only access, admin unlock/change, breaks, locking/reopening and exports from RDS. Browser policy may otherwise cause a Windows credential prompt.

### SQL networking production decision

The original strict open-source condition excludes Microsoft.Data.SqlClient.SNI.runtime assets. The project therefore enables managed networking on Windows, a switch Microsoft documents for testing/debugging. Local successful SQL tests do not establish supported Windows production operation.

Before production, agree either to approve Microsoft's native networking package/license and remove the exclusion/switch with deployment testing, or use a supported managed-driver hosting architecture and revisit Windows hosting/authentication. Do not label the current local managed-switch configuration production-certified. See THIRD_PARTY_LIBRARIES.md.

## Diagnostic logging

`Diagnostics/DiagnosticFileLoggerProvider.cs` connects ASP.NET `ILogger` to a dedicated `System.Diagnostics.TraceSource` and `TextWriterTraceListener`. No additional logging package is required. Existing console/debug logging remains available. See Microsoft's [custom logger provider documentation](https://github.com/dotnet/docs/blob/main/docs/core/extensions/logging/custom-provider.md).

Files are written **directly beside the running application assembly**, using `AppContext.BaseDirectory`, not the working directory or wwwroot:

- Development: `bin/Debug/net10.0/WL-TimeSheet-yyyyMMdd-p<processId>-000.log`
- Published application: `<publish folder>/WL-TimeSheet-yyyyMMdd-p<processId>-000.log`

Each event is an indented JSON object separated by a blank line (a sequence of JSON objects, not one JSON array). Events include UTC timestamp, severity, category, event ID, process/thread IDs, activity trace/span IDs, message template/properties and request scopes. Exceptions include type, message, stack frames and nested exceptions. Concurrent writes are serialized; request scopes remain isolated. Entries flush immediately, rotate by UTC day or configured size, and use process-specific names for multiple workers. A single unusually large event is retained intact and may exceed the size target.

`Logging:DiagnosticFile` settings in appsettings.json:

```json
"DiagnosticFile": {
  "Enabled": true,
  "MaxFileSizeMB": 10,
  "RetentionDays": 30
}
```

Restart after changing file options. Standard `Logging:LogLevel` settings control severity/category filtering; EF SQL command chatter defaults to Warning. Files older than the retention period are removed only when their names match this application's dated diagnostic pattern.

`RequestDiagnosticsMiddleware` records request method/path, request ID, status and duration. Business/concurrency failures are warnings; unexpected failures are errors with exception details. The client receives the same request reference, without stack traces. Startup/shutdown, initialization, task/day saves, deletion, reopening and administrator unlock/password-change outcomes also generate events. Application events omit passwords, request bodies, query strings, timesheet descriptions and connection strings. Sensitive structured property names are redacted; do not place secrets in formatted messages.

Grant the application/service identity write access to its assembly directory and restrict log read access to administrators. Logs are outside wwwroot and are not served by the application. If the directory is unwritable or the disk fails, logging reports the failure to stderr (at most once per minute) while the application continues; it does not silently choose another directory. Preserve logs during deployment. Read active files using an editor/tail tool that shares access with the writer, for example `Get-Content <file> -Tail 50 -Wait`.

## Maintenance tasks

Preserve lock files, review package licenses/advisories and regenerate the dependency inventory after upgrades. Use migrations and backup/restore drills. Existing independent-time entries require a reviewed conversion; the revision migration stops rather than discarding them. Keep calculations and ownership authoritative on the server.


## C# coding style

The repository .editorconfig uses four-space indentation, braces on separate lines, braces around conditional bodies, and one statement per line. Prefer descriptive local names and break compound conditions across lines when helpful. Keep validation, entity updates, persistence and return statements visually separated.

Format application code with:

```powershell
dotnet format style 'WL TimeSheet.csproj' --no-restore --diagnostics IDE0011 --exclude Data/Migrations
dotnet format whitespace 'WL TimeSheet.csproj' --no-restore --exclude Data/Migrations
```

Generated migrations are excluded from this cleanup.

## Configurable units

Administration > Timesheet settings (/settings) sets 1–60 minutes per unit for newly created days. The ConfigurableUnits migration adds the persisted default and daily snapshot. TimesheetService calculates timelines and productive/break/calendar totals using each daily rate; exports include that rate. Existing days cannot be reinterpreted by changing the default. Settings POST uses existing administrator authorization and antiforgery protection, and rowversion validation prevents stale updates.

## Database backup

Administration > Database backup (`/settings/backup`) stores `BackupEnabled`, `BackupIntervalDays` (default 7), `BackupRetentionDays` (default 30) and `BackupDirectory` on `AdministrationSettings`. `DatabaseBackupHostedService` checks every 15 minutes. When the schedule is due it runs `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM` and then deletes matching `.bak` files older than the retention period. Retention must be at least as long as the interval. A failed attempt waits an hour before retrying. Back up now uses the saved folder and retention. Apply `20261022010000_DatabaseBackup` with `--initialize` before starting the new build.
