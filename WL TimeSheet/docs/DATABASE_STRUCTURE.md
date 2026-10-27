# Database structure

SQL Server database WLTimesheet. EF migrations and AppDbContext are authoritative. GUID primary keys are application-generated. Audit timestamps are UTC; work dates use the configured local time zone. Audited entities have CreatedAt/By, UpdatedAt/By and SQL rowversion.

| Table | Principal fields |
|---|---|
| Employees | WindowsSid (unique when populated), WindowsAccount, DisplayName, Email, IsActive |
| DailyTimesheets | EmployeeId, WorkDate, DisplayName, ClockInTime, StartTime, ReopenedUntil, ReopenedBy, ReopenReason |
| TimeEntries | DailyTimesheetId, Position, integer Units, TaskTypeId, IsBreak snapshot, Description, FilesClosed, SubmissionId |
| TaskTypes | unique Name, ExplanatoryNotes, IsActive, IsBreak, DescriptionRule |
| AdministrationSettings | singleton Id=1, PasswordHash, SecurityStamp, MinutesPerUnit, backup schedule and folder, LastBackupAt/Attempt/Error, ModifiedAt/By, rowversion |
| __EFMigrationsHistory | Applied schema versions |

`AdministrationSettings.MinutesPerUnit` stores the default for new daily timesheets (1–60). `DailyTimesheets.MinutesPerUnit` snapshots it at creation. Subsequent default changes never reinterpret that day's units. Reporting duration is entry Units multiplied by the saved daily rate. Clock-in is not rounded to that rate.

Constraints enforce one day per employee/date, unique positions and submission IDs per day, units 0–1440, file count 0–1,000,000, and restricted deletion of referenced records. The service additionally limits the whole timeline to midnight. DescriptionRule values: Optional=0, Required=1, FilesClosed=2. Start/finish times and durations are derived, not independently editable entry fields.

dbo.TimesheetReporting joins employee/day/task data with window sums for StartAt/FinishAt and productive/break minutes. Give future Power BI consumers read-only access: web employee authorization does not apply to direct SQL access.

## Migration and initialization

`20261001062615_ConfigurableUnits` adds the default and per-day rate columns, initializes existing records to 1, enforces a range of 1–60 and changes the reporting view to use each day's rate. Apply through `--initialize` with the app stopped. The preceding MinuteUnits conversion remains required for old six-minute databases. Down migration refuses saved custom daily rates.

`20261001060914_MinuteUnits` changes units to minutes, expands the constraint to 0–1440, and updates `dbo.TimesheetReporting`. It multiplies existing Units by six once, preserving historical durations and stored daily starts. Apply it with the app stopped using `--initialize`; normal startup refuses pending migrations. Back up first. Rollback refuses entries not divisible by six.

WindowsDailyTimesheets follows InitialCreate and deliberately aborts if old independent-time entries exist: those require reviewed conversion. Back up first. Unlinked legacy employee profiles are deactivated, never mapped implicitly to Windows users. Down migration is not a substitute for backup restoration.

Initialization creates the administrator hash from protected Bootstrap:AdminPassword only when settings are absent. Remove the bootstrap value afterwards. The idempotent workbook import inserts missing names/notes and initial break/detail flags, preserving existing administrator edits. Sample timesheets are not imported.

`20261022010000_DatabaseBackup` adds the backup schedule to the singleton settings row. Administration > Database backup (`/settings/backup`) sets the interval (default 7 days) and retention (default 30 days). While automatic backups are on, the service writes a copy-only SQL Server `.bak` and deletes only its own files older than the retention period. The SQL login needs `BACKUP DATABASE`, and both the SQL Server service account and the application account need access to the backup folder. Test a restore separately; this job does not restore the database.

