# Websters Lawyers Timesheet

ASP.NET Core 10 MVC, SQL Server and automatic Windows identity. The client's workbook and business rules are implemented; Keycloak is no longer required.

Choose a date and enter daily clock-in/name details. They save automatically when you leave either field; a status message confirms saving or reports a failure. In List mode, use the blank row below saved records: enter whole units using the rate shown for that day, Tab to task type, then Tab to description. Enter or Tab from the last field saves and focuses Units in the next blank row. The four summary cards appear below the timesheet. Existing tasks edit inline. Server pagination offers 10/20/50/100 records; summary totals cover all matching records. Excel and CSV exports sit beside Daily Timesheet.

Calendar mode keeps the month and scroll position stable when selecting a day. Its day overview shows the selected tasks; the day’s plus button or overview Add task opens the same right-side drawer, including clock-in setup for a new day. Times chain automatically, breaks are excluded from productive totals, and file closures require a file count.

**Administration** includes **Timesheet settings**: choose 1–60 minutes per unit for newly created daily timesheets. Existing days retain their rate and durations; later changes need no code or restart. Administration also unlocks task-type management, employee activation, team reporting and reopening older days. The supplied initial administrator password has been initialized locally; use **Change password** to change it. Windows identity always determines timesheet ownership.

## Run

Prerequisites: .NET 10 SDK, Windows, SQL Server and HTTPS development certificate. The local database connection is configured in user secrets.

```powershell
dotnet restore
dotnet run --launch-profile https
```

Open https://localhost:7294/ from your Windows session. On a new installation configure ConnectionStrings:Timesheet and Bootstrap:AdminPassword through protected configuration, run `dotnet run -- --initialize --import-template`, then remove Bootstrap:AdminPassword. Normal startup does not migrate the database.

Upgrading the six-minute release requires the `MinuteUnits` migration before normal startup. It preserves historical durations by converting stored units to minutes. Follow the backup, stop, initialize and restart steps in the [deployment upgrade guide](deployment/README.md#required-upgrade-root-address-and-one-minute-units-1-october-2026).

## Frontend source

The timesheet and team overview use `Views/Home/Workspace.cshtml`; employee and task-type administration use `Views/Home/Management.cshtml`. Shared navigation is in `Views/Shared/_Layout.cshtml`, styles are in `wwwroot/css/site.css`, and browser behavior is in `wwwroot/js/site.js` and `wwwroot/js/timesheet.js`. The home route directly renders the timesheet at `/`, so it does not need an `Index.cshtml` view.

## Defaults awaiting client feedback

Adelaide time zone; keep clock-in at the entered minute; allow today plus the previous three calendar dates; administrator reopening lasts 24 hours. These settings are configurable in appsettings.json. This is a date-based window, not a rolling 72 hours. Days may finish at midnight but cannot cross it.

- [Client review and decisions](docs/CLIENT_TEMPLATE_REVIEW.md)
- [Architecture](docs/SYSTEM_ARCHITECTURE.md)
- [Database](docs/DATABASE_STRUCTURE.md)
- [Programmer / Windows deployment guide](docs/PROGRAMMERS_GUIDE.md)
- [Self-contained EXE and Windows Service setup](deployment/README.md)
- [Deployment handover and current status (1 October 2026)](docs/DEPLOYMENT_HANDOVER.md)
- [Libraries](docs/THIRD_PARTY_LIBRARIES.md)

Client IIS/RDS deployment still needs environment validation and the SQL driver support/licensing decision documented in the deployment guide.


