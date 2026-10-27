# System architecture

Razor pages, application CSS and vanilla JavaScript provide the browser UI. ASP.NET Core MVC supplies authenticated JSON APIs and exports. EF Core persists normalized daily timesheets in SQL Server.

## Identity and security

Negotiate authenticates the HTTP Windows principal. CurrentEmployee provisions a profile from its stable Windows SID on first access; it never uses the server process username or a browser-supplied identity. Inactive profiles cannot use the application. No employee password form or external identity server is needed.

Administration uses a separate 30-minute encrypted Secure/HTTP-only cookie bound to the Windows SID and database security stamp. Framework PasswordHasher stores a salted PBKDF2 hash with 210,000 iterations. Changing the password invalidates unlock sessions. Attempts are throttled to five per minute per identity in the application process.

Normal users can only read, write and export their own timesheets. Unlocked administrators can view team reports and reopen older days; editing still requires the owning Windows identity. Modifying requests require antiforgery tokens. Output escaping, content security policy and CSV formula neutralization protect browser/spreadsheet output.

## Calculation and consistency

A unique employee/date record stores entered clock-in and the first start. Ordered entries store integer units and a break classification snapshot. Row times derive from first start plus all preceding units multiplied by the daily MinutesPerUnit snapshot. Task filtering happens after timeline calculation. Productive totals exclude break snapshots, preserving historical totals when a task type changes.

Transactions and daily rowversions serialize aggregate changes. Entry rowversions detect stale edits/deletes. Unique submission IDs prevent duplicate successful submissions. Foreign keys preserve historical references.

## Hosting

Local HTTPS Kestrel uses Negotiate. Windows 11 Pro hosting uses IIS Windows Authentication, anonymous authentication disabled, a trusted certificate and RDS browser intranet configuration. SQL access uses the application/service identity, not employee impersonation. Persist/protect Data Protection keys. Administrators set SQL backup frequency and retention on Database backup; the service writes copy-only backups and deletes its own expired files.

The client IIS/RDS environment has not been validated. Managed SQL networking on Windows is currently enabled to retain the original open-source constraint; its production support/licensing conflict is documented in PROGRAMMERS_GUIDE.md.


AdministrationSettings.MinutesPerUnit controls new daily timesheets. Each day snapshots the default, and the client supplies its expected rate when creating a day so a stale preview is rejected. Settings updates require unlocked administration, antiforgery validation and the current settings rowversion. Existing daily rates are immutable through the application.
