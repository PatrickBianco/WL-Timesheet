# Implementation plan

The timesheet application in this repository is already built. This plan records what is done, what must not be rebuilt, and the remaining work before a production handover. Feature IDs are defined in `PRD.md`.

Do not start a new solution, a new identity provider, or an approval workflow as part of this plan. Those are open questions, not scheduled tasks.

## 1. Status

| Phase | Features | State |
|---|---|---|
| 1. Foundation | Host, EF, SQL Server, migrations | Done |
| 2. Identity | TS-001, TS-002 | Done |
| 3. Catalogue and people | TS-011, TS-012, TS-015 | Done |
| 4. Timesheet entry | TS-003, TS-004, TS-008 | Done |
| 5. Calculation and lock | TS-005, TS-006, TS-007, TS-013 | Done |
| 6. Approval workflow | None | Not scheduled. No product requirement |
| 7. Reporting and export | TS-009, TS-010, reporting view | Done. Power BI login is TBD |
| 8. Operations | TS-014, TS-016 | Backup and logs are in code. Restore drill and IIS proof are not done |

## 2. Dependency order

Implementation already followed this order. Later changes must respect it.

```text
TS-001 profile
  -> TS-003 day header
      -> TS-004 rows
          -> TS-006 timeline
          -> TS-005 totals
  -> TS-002 unlock
      -> TS-007 reopen
      -> TS-010 team export
      -> TS-011 employees
      -> TS-012 task types
      -> TS-013 unit default
          -> TS-003 snapshot on the next new day
      -> TS-014 backup
TS-015 import -> TS-012
TS-009 depends on TS-004 and TS-006
```

TS-013 must not recalculate existing days. TS-007 must not let an administrator edit someone else's rows.

## 3. Completed phases

### Phase 1 — Foundation

**Delivered.** `net10.0` web project, `AppDbContext`, SQL Server migrations, configuration, Windows service host, content security headers.

**Done when.** The app starts only if migrations are current, and `--initialize` applies them.

### Phase 2 — Authentication and administration (TS-001, TS-002)

**Delivered.** Negotiate sign-in, SID profile, inactive block, shared password hash, 30-minute unlock cookie, lock, password change, rate limits.

**Done when.** Two Windows users cannot read each other's rows, and a wrong administration password does not unlock.

### Phase 3 — Employees and task types (TS-011, TS-012, TS-015)

**Delivered.** Paged directories, deactivate (except self), task rules, idempotent workbook import, no sample time rows.

**Done when.** A duplicate task name is rejected and a second import does not clone names.

### Phase 4 — Timesheet entry (TS-003, TS-004, TS-008)

**Delivered.** Header save, keyboard row, list and calendar, paging, submission id, rowversions.

**Done when.** A day cannot gain tasks before a header, and a repeated submission id does not duplicate the row.

### Phase 5 — Rules and units (TS-005, TS-006, TS-007, TS-013)

**Delivered.** Break snapshot, derived timeline, midnight rejection, three-day window, reopen with reason, per-day unit snapshot, settings page.

**Done when.** Changing the default or a task type's break flag leaves historical durations and productive totals unchanged, and an old day stays read-only until reopen.

### Phase 7 — Exports (TS-009, TS-010)

**Delivered.** Personal workbook, admin workbook, CSV, on-screen totals, `dbo.TimesheetReporting`.

**Done when.** An export uses the filter rather than the current page, and mixed unit rates stay distinct.

### Phase 8a — Backup and logs (TS-014, TS-016)

**Delivered.** Administration backup page, weekly and 30-day defaults, hosted schedule, copy-only backup, own-file retention, diagnostic file log.

**Done when.** The settings reject retention shorter than the interval, and a failed backup records an error without removing the previous file.

## 4. Remaining work

These tasks are the implementation backlog. They do not add product features that `PRD.md` marks TBD.

### Phase 8b — Production proof

Depends on phases 1–8a.

| Task | Trace | Done when |
|---|---|---|
| Decide SQL native package versus managed networking and record the decision | Open question in PRD | `PROGRAMMERS_GUIDE.md` no longer calls the choice open, and a Windows host connects with the chosen stack |
| Publish to a clean IIS site: Windows auth on, anonymous off, trusted certificate | TS-001 | Two RDS users reach `/` as themselves |
| Run `--initialize` on a copy of production, including `DatabaseBackup`, with the site stopped | TS-014 | Startup then reports no pending migrations |
| Grant the runtime login DML plus `BACKUP DATABASE`. Grant DDL only to the migration identity | TS-014 | Backup now writes a `.bak`. A normal user session cannot alter schema |
| Confirm the application identity can delete expired `.bak` files | TS-014 | A file older than retention disappears on the next check or on Back up now, and unrelated files remain |
| Restore that `.bak` onto a scratch database and read one known day | TS-014 | Row counts and one day's units match the source. Restore steps are written in the handover |
| Persist Data Protection keys for the pool identity | TS-002 | An unlock cookie still validates after an app-pool recycle |
| Restrict log and backup directories to administrators and the service | TS-016 | The site does not serve log or `.bak` files |

### Phase 9 — Regression before release

Depends on 8b for the environment checks. Local checks can run earlier.

| Check | Features |
|---|---|
| Own-only list, calendar, and personal export | TS-001, TS-008, TS-009 |
| Header required before a task; stale unit default rejected | TS-003, TS-013 |
| Break versus productive totals; midnight rejection | TS-005, TS-006 |
| Day older than the window is read-only; reopen expires | TS-007 |
| Admin export hidden until unlock; password change kills the cookie | TS-002, TS-010 |
| Inactive employee blocked; own account cannot be deactivated | TS-001, TS-011 |
| File-closure count required; inactive task type blocked for new rows | TS-004, TS-012 |
| Offline minute/export check in `artifacts/minute-verification` | TS-006, TS-009, TS-010 |

No automated UI suite exists. Add one only if a later change keeps breaking a keyboard or calendar path. Do not block release on a new framework.

### Explicitly not in this backlog

| Item | Reason |
|---|---|
| Submit, approve, reject | No requirement. Would be a new phase after a written decision |
| Overtime, rosters, payroll | TBD |
| Departments, projects, GPS, biometrics | Out of scope |
| Email reminders | TBD |
| Moving `EditablePreviousDays` and `ReopenHours` onto the settings screen | TBD |
| Replacing the shared administration password with named roles | TBD |
| Building Power BI reports | The view is enough until a reader is named |

## 5. Release completion

Release is complete when:

- Phase 8b evidence is recorded in `DEPLOYMENT_HANDOVER.md`.
- Phase 9 checks have been run on the IIS host by two Windows accounts.
- `Bootstrap:AdminPassword` is absent from the server configuration.
- An administrator has saved the backup folder and enabled the schedule, or has written down why it stays off.
- A restore from a backup of that environment has been read back once.
- Open questions in `PRD.md` are either still marked TBD or have a dated decision. Unresolved questions must not be implemented quietly.

## 6. How to change the product later

1. Update the feature in `PRD.md` or add a new ID. Do not hide the change in a pull request only.
2. Adjust `UI-UX-DESIGN.md` if a screen or state changes.
3. Adjust `TECHNICAL-SPECIFICATION.md` if a table, endpoint, or permission changes.
4. Add a migration. Do not edit shipped migrations.
5. Add a row to section 4 or a new phase here with the feature ID and a testable done-when line.

Existing operator documents (`PROGRAMMERS_GUIDE.md`, `DATABASE_STRUCTURE.md`, `DEPLOYMENT_HANDOVER.md`, `SYSTEM_ARCHITECTURE.md`) stay the runbooks. Update them when a change alters setup, schema, or hosting. Do not copy them into a fifth specification.
