# WL TimeSheet deployment handover

Last updated: 1 October 2026.

This records the work completed and the deployment evidence shared during setup. Remote server results came from Patrick's PowerShell output, logs and screenshots; the remote server was not directly accessed from the development workspace.

## Current position

Subsequent request: let administrators configure the unit without coding. The configurable-unit release adds **Administration > Timesheet settings**, a 1–60-minute default for newly created days, per-day rate snapshots, and migration `20261001062615_ConfigurableUnits`. Historical durations remain stable. This supersedes the fixed-one-minute behavior described below; 1 minute remains the initial default. Remote deployment and SQL runtime verification are still pending.

Latest requested release: root URL `/` and 1 unit = 1 minute. Source calculations, previews, reports and exports have been updated. The MinuteUnits migration is required to preserve historical durations; see the deployment README's upgrade procedure. Remote deployment of this release has not been performed from this workspace. The service/certificate troubleshooting notes below record earlier setup evidence.

The published application has been installed as a Windows Service. SQL Server connectivity and database initialization have succeeded. HTTPS has returned a Windows authentication challenge from the service.

The latest blocker is database access for the service account, `NT AUTHORITY\LOCAL SERVICE`. We have now identified a working SQL administrator connection: `WSLTimesheet\TimesheetAdmin`, with `IsSqlAdmin = 1` and `CanAccessTimesheet = 1`.

**Next action:** run the SQL permission script below in that working SSMS connection, restart the existing service, and test the browser. Successful execution of that permission script and a working timesheet under the service have not yet been confirmed.

## Source cleanup and publishing

- Reformatted the frontend source for readability and removed the test suite as requested.
- Removed unused starter views, unused frontend libraries and obsolete temporary artifacts.
- Kept the active timesheet views, application code, templates and deployment files.
- Added a Windows x64 self-contained, single-file publish profile and Windows Service support.
- Kept configuration outside the EXE so server settings can be changed without rebuilding.
- Added service installation, lifecycle commands, startup recovery and folder permissions in `Service.ps1`.

Frontend entry points are `Views/Home/Workspace.cshtml`, `Views/Home/Management.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/site.js` and `wwwroot/js/timesheet.js`. The home route redirects to `/timesheet`.

The deployment package is under `artifacts/publish-service` and contains:

```text
WL TimeSheet.exe
appsettings.json
appsettings.Production.json
README.md
Service.ps1
```

The .NET runtime and application assets are bundled. SQL Server is installed separately. The EXE extracts bundled content to the running account's temporary directory; this is expected. The later source cleanup was build-checked, but this record does not establish that the deployed EXE was republished after that cleanup.

## Recorded remote setup

Les subsequently requested standard HTTPS port **443**. The source deployment template and local `artifacts/publish-service` configuration now use 443. Applying this change to the remote server has not yet been confirmed. The table below preserves the values observed before that request. After the change, use `https://WSLTimesheet/timesheet` (subject to final DNS confirmation). See the deployment README for the change procedure. The existing ZIP has not been regenerated.

| Item | Recorded value |
| --- | --- |
| Deployment directory | `C:\WL-TImesheet` |
| Windows Service | `WLTimeSheet` |
| Service identity | `NT AUTHORITY\LocalService` |
| Browser address used | `https://WSLTimesheet:7294/timesheet` |
| Address resolved during testing | `192.168.100.30` |
| HTTPS listener | `0.0.0.0:7294` |
| SQL instance reported by SSMS | `WSLTimesheet\SQLEXPRESS` |
| SQL TCP port tested successfully | `14330` |
| Database | `WLTimeSheet` |
| Diagnostic logs | `C:\ProgramData\WLTimeSheet\Logs` |
| Service encryption keys | `%ProgramData%\WLTimeSheet\Keys\LocalService` |

`WSLTimesheet` is the name used in the recorded URL and SQL output. The final domain membership, DNS name and staff-facing address still need confirmation; an SQL instance name alone does not establish current Windows domain membership.

The same-machine SQL connection proposed during troubleshooting was:

```text
Server=tcp:127.0.0.1,14330;Database=WLTimeSheet;Integrated Security=True;Encrypt=True;TrustServerCertificate=True
```

Check the deployed `appsettings.Production.json` for the actual current value. `TrustServerCertificate=True` bypasses SQL certificate validation; the intended final setup uses a trusted SQL certificate and matching server name with validation enabled. This setting is separate from the website's HTTPS certificate.

## Fixes and results so far

1. **SQL TCP connectivity:** enabled TCP/IP for SQLEXPRESS, configured port `14330`, and restarted `MSSQL$SQLEXPRESS` from elevated PowerShell. The later loopback port test returned `TcpTestSucceeded: True`.
2. **Database schema:** explicitly ran the published EXE with `--initialize --import-template`. Logs confirmed migrations `20260921020701_InitialCreate` and `20260921070105_WindowsDailyTimesheets`, import of 38 task types, and successful completion. Normal startup does not initialize or migrate the database.
3. **HTTPS:** created a certificate for `WSLTimesheet` in Local Computer > Personal and imported its public certificate into the server's trusted roots. Service certificate private-key read permission was identified as a required check. A later service request completed TLS and returned HTTP `401` with `WWW-Authenticate: Negotiate`. That verifies an authentication challenge, not a complete authenticated request or client certificate trust; the curl test used `-k`.
4. **Service installation:** used the process-scoped execution-policy command below after PowerShell blocked direct script execution. Output confirmed the service running as LocalService with automatic recovery configured.
5. **Encryption keys:** separated service keys from keys created by the interactive account. Confirmed the configuration points at `Keys\LocalService` and granted LocalService Modify access. Windows-protected keys are tied to the account that created them; preserve existing key directories.
6. **Database permissions:** application logs reported that LocalService could not open `WLTimeSheet`. A previous SSMS connection as `GLUCHE\databaseadmin` could neither administer SQL nor access that database. The latest connection as `WSLTimesheet\TimesheetAdmin` can do both. These are distinct SQL login identities in the supplied results; the reason for the Windows display-name/domain differences has not been established.

## Complete the service database permission fix

Run this in **SSMS**, using the connection where both administrator and database-access checks returned `1`. It is SQL, not PowerShell and not a change to `Service.ps1`.

```sql
USE [master];
GO
IF NOT EXISTS (
    SELECT 1 FROM sys.server_principals
    WHERE name = N'NT AUTHORITY\LOCAL SERVICE'
)
    CREATE LOGIN [NT AUTHORITY\LOCAL SERVICE] FROM WINDOWS;
GO

USE [WLTimeSheet];
GO
IF USER_ID(N'NT AUTHORITY\LOCAL SERVICE') IS NULL
    CREATE USER [NT AUTHORITY\LOCAL SERVICE]
    FOR LOGIN [NT AUTHORITY\LOCAL SERVICE];
GO

GRANT CONNECT TO [NT AUTHORITY\LOCAL SERVICE];
GRANT SELECT, INSERT, UPDATE, DELETE
ON SCHEMA::dbo TO [NT AUTHORITY\LOCAL SERVICE];
GO
```

This is for the current setup with SQL on the app server. It grants ordinary application data access, not schema-migration privileges. Staff do not each need SQL logins: their Windows identity identifies them to the app, while the service identity connects to SQL.

After the script succeeds, run in **Administrator PowerShell on the app server**:

```powershell
Restart-Service WLTimeSheet
Get-Service WLTimeSheet
```

Refresh `https://WSLTimesheet:7294/timesheet`. If a reference-number error remains, capture the corresponding exception from the diagnostic logs. Do not recreate the database or reinstall the service to retry this permission change.

## Service operation

The service is already installed. The command used for its initial installation was:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\WL-TImesheet\Service.ps1" -Action Install
```

Do not repeat `Install` for an existing service. Use elevated PowerShell for normal control:

```powershell
Get-Service WLTimeSheet
Restart-Service WLTimeSheet
# Use these separately when needed:
Stop-Service WLTimeSheet
Start-Service WLTimeSheet
```

Running the EXE manually uses the interactive user's permissions. It does not prove that the service account has the same SQL, certificate or encryption-key access. Stop the service before an interactive diagnostic run to avoid two processes competing for port 7294.

## Verification completed and still needed

Local development-machine verification previously included a copied published EXE, a separate fresh database, explicit initialization, Windows-authenticated HTTPS access, saving/reloading a timesheet entry, and an Excel export. This was not a Windows Service test and does not establish remote deployment readiness. The source cleanup build also passed.

Remote evidence confirms installation, a listening HTTPS endpoint, a successful authentication challenge, reachable SQL TCP port and successful schema initialization. Remaining checks are:

- Confirm the LocalService SQL grants succeed and the timesheet loads under the service.
- Save and reload an entry and export a timesheet from the remote deployment.
- Confirm service startup and application access after a planned server reboot.
- Confirm the final hostname, domain/service authentication configuration and trusted HTTPS certificate on the RDS clients.
- Verify access from two individual staff accounts, with separate timesheet ownership.
- Apply the requested change to HTTPS port 443 and confirm the firewall permits TCP 443 from the intended RDS hosts. Staff do not need access to SQL port 14330.
- Open Administration > Database backup after upgrade, set the folder, and turn on automatic backups (weekly and 30 days are the defaults). Grant the SQL login `BACKUP DATABASE`. The SQL Server service account must be able to create files in that folder, and the application account must be able to delete expired ones. Retain the production configuration and encryption keys during upgrades.

Les confirmed that staff use browsers inside Remote Desktop sessions, each employee has an Active Directory account, and access is internal only. No public internet exposure was requested. Domain joining of the application host was offered if needed; completion has not been confirmed.

For the reusable installation and update procedure, see [the deployment README](../deployment/README.md). No passwords are recorded in this handover.
