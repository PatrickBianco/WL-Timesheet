using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;
namespace WL_TimeSheet.Services;

public sealed class TimesheetService(AppDbContext db, CurrentEmployee current, TimesheetPolicy policy, ActivityLogService activity, ILogger<TimesheetService> logger)
{
    public const int MaxRows = 10000;
    private Task<int> DefaultMinutesPerUnitAsync() => db.AdministrationSettings
        .Where(x => x.Id == 1).Select(x => x.MinutesPerUnit).SingleAsync();
    public async Task<object> PageAsync(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId, int page, int pageSize)
    {
        if (page < 1 || pageSize is not (10 or 20 or 50 or 100))
        {
            throw new BusinessException("Choose a valid page and a page size of 10, 20, 50 or 100.");
        }

        var query = (await QueryAsync(from, to, employeeId)).SelectMany(x => x.Entries);
        if (taskTypeId.HasValue)
        {
            query = query.Where(x => x.TaskTypeId == taskTypeId);
        }

        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var daily = await query.GroupBy(x => x.DailyTimesheet.WorkDate).Select(g => new
        {
            Date = g.Key,
            Minutes = g.Sum(x => x.IsBreak ? 0 : x.Units * x.DailyTimesheet.MinutesPerUnit),
            BreakMinutes = g.Sum(x => x.IsBreak ? x.Units * x.DailyTimesheet.MinutesPerUnit : 0),
            Units = g.Sum(x => x.Units),
            Count = g.Count()
        }).ToListAsync();
        // Calculate offsets in SQL over ALL preceding rows, before paging/type filtering.
        var data = await query.OrderBy(x => x.DailyTimesheet.WorkDate).ThenBy(x => x.DailyTimesheet.EmployeeId).ThenBy(x => x.Position)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
            {
                x.Id,
                x.DailyTimesheetId,
                x.DailyTimesheet.EmployeeId,
                EmployeeName = x.DailyTimesheet.DisplayName,
                WindowsAccount = x.DailyTimesheet.Employee.WindowsAccount ?? x.DailyTimesheet.Employee.DisplayName,
                x.DailyTimesheet.WorkDate,
                x.Position,
                StartMinute = x.DailyTimesheet.StartTime.Hour * 60 + x.DailyTimesheet.StartTime.Minute +
                    (db.TimeEntries.Where(previous => previous.DailyTimesheetId == x.DailyTimesheetId && previous.Position < x.Position).Sum(previous => (int?)previous.Units) ?? 0) * x.DailyTimesheet.MinutesPerUnit,
                x.Units,
                x.DailyTimesheet.MinutesPerUnit,
                x.TaskTypeId,
                TaskTypeName = x.TaskType.Name,
                x.TaskType.ExplanatoryNotes,
                x.Description,
                x.FilesClosed,
                x.IsBreak,
                x.Version,
                DayVersion = x.DailyTimesheet.Version,
                x.DailyTimesheet.ReopenedUntil
            }).ToListAsync();
        var rows = data.Select(x => new EntryRow(x.Id, x.DailyTimesheetId, x.EmployeeId, x.EmployeeName, x.WindowsAccount, x.WorkDate, x.Position,
            x.StartMinute, x.StartMinute + x.Units * x.MinutesPerUnit, x.Units, x.TaskTypeId, x.TaskTypeName, x.ExplanatoryNotes, x.Description, x.FilesClosed, x.IsBreak,
            x.Version, x.DayVersion, policy.CanEdit(x.WorkDate, x.ReopenedUntil), x.MinutesPerUnit)).ToList();
        return new
        {
            rows,
            page,
            pageSize,
            total,
            pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
            minutes = daily.Sum(x => x.Minutes),
            breakMinutes = daily.Sum(x => x.BreakMinutes),
            elapsedMinutes = daily.Sum(x => x.Minutes + x.BreakMinutes),
            units = daily.Sum(x => x.Units),
            days = daily.Count,
            employees = await query.Select(x => x.DailyTimesheet.EmployeeId).Distinct().CountAsync(),
            daily,
            weekly = daily.GroupBy(x => x.Date.AddDays(-((int)x.Date.DayOfWeek + 6) % 7)).Select(g => new { week = g.Key, minutes = g.Sum(x => x.Minutes), count = g.Sum(x => x.Count) })
        };
    }

    public async Task<IQueryable<DailyTimesheet>> QueryAsync(DateOnly from, DateOnly to, Guid? employeeId)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            throw new BusinessException("Select a date range of up to 367 days.");
        }

        var actor = await current.GetAsync();
        if (!await current.IsAdminAsync())
        {
            if (employeeId.HasValue && employeeId != actor.Id)
            {
                throw new BusinessException("You can only access your own timesheet.", 403);
            }

            employeeId = actor.Id;
        }
        var q = db.DailyTimesheets.AsNoTracking().Where(x => x.WorkDate >= from && x.WorkDate <= to);
        if (employeeId.HasValue)
        {
            q = q.Where(x => x.EmployeeId == employeeId);
        }

        return q;
    }

    public async Task<List<EntryRow>> ListAsync(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId)
    {
        var q = await QueryAsync(from, to, employeeId);
        if (await q.SelectMany(x => x.Entries).CountAsync() > MaxRows)
        {
            throw new BusinessException("Too many entries. Narrow the date range or employee filter.");
        }

        var days = await q.Include(x => x.Employee).Include(x => x.Entries).ThenInclude(x => x.TaskType).OrderBy(x => x.WorkDate).ThenBy(x => x.EmployeeId).ToListAsync();
        var result = new List<EntryRow>();
        foreach (var day in days)
        {
            var minutes = day.StartTime.Hour * 60 + day.StartTime.Minute;
            foreach (var e in day.Entries.OrderBy(x => x.Position))
            {
                var end = minutes + e.Units * day.MinutesPerUnit;
                if (!taskTypeId.HasValue || taskTypeId == e.TaskTypeId)
                {
                    result.Add(new(e.Id, day.Id, day.EmployeeId, day.DisplayName, day.Employee.WindowsAccount ?? day.Employee.DisplayName, day.WorkDate, e.Position, minutes, end, e.Units, e.TaskTypeId, e.TaskType.Name, e.TaskType.ExplanatoryNotes, e.Description, e.FilesClosed, e.IsBreak, e.Version, day.Version, policy.CanEdit(day.WorkDate, day.ReopenedUntil), day.MinutesPerUnit));
                }

                minutes = end; // Advance through ALL entries, including filtered-out tasks and breaks.
            }
        }
        return result;
    }

    public async Task<object> CalendarAsync(DateOnly month, Guid? employeeId, Guid? taskTypeId)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var q = (await QueryAsync(start, start.AddDays(DateTime.DaysInMonth(start.Year, start.Month) - 1), employeeId)).SelectMany(x => x.Entries);
        if (taskTypeId.HasValue)
        {
            q = q.Where(x => x.TaskTypeId == taskTypeId);
        }

        var summaries = await q.GroupBy(x => x.DailyTimesheet.WorkDate).Select(g => new
        {
            Date = g.Key,
            Count = g.Count(),
            Minutes = g.Sum(x => x.IsBreak ? 0 : x.Units * x.DailyTimesheet.MinutesPerUnit),
            BreakMinutes = g.Sum(x => x.IsBreak ? x.Units * x.DailyTimesheet.MinutesPerUnit : 0),
            ElapsedMinutes = g.Sum(x => x.Units * x.DailyTimesheet.MinutesPerUnit),
            Units = g.Sum(x => x.Units)
        }).ToListAsync();
        // Bounded to at most 31 dates and two labels per date, never full monthly entry records.
        var result = new List<object>();
        var reopened = await (await QueryAsync(start, start.AddMonths(1).AddDays(-1), employeeId))
            .Where(x => x.ReopenedUntil > policy.Now).Select(x => x.WorkDate).Distinct().ToListAsync();
        reopened.AddRange(await GrantDatesAsync(start, start.AddMonths(1).AddDays(-1), employeeId));
        for (var date = start; date < start.AddMonths(1); date = date.AddDays(1))
        {
            var summary = summaries.SingleOrDefault(x => x.Date == date);
            var previews = summary is null ? [] : await q.Where(x => x.DailyTimesheet.WorkDate == date)
                .OrderBy(x => x.DailyTimesheet.EmployeeId).ThenBy(x => x.Position).Select(x => x.TaskType.Name).Take(2).ToArrayAsync();
            result.Add(new
            {
                Date = date,
                Count = summary?.Count ?? 0,
                Minutes = summary?.Minutes ?? 0,
                BreakMinutes = summary?.BreakMinutes ?? 0,
                ElapsedMinutes = summary?.ElapsedMinutes ?? 0,
                Units = summary?.Units ?? 0,
                Previews = previews,
                CanEdit = policy.CanEdit(date) || reopened.Contains(date)
            });
        }
        return result;
    }

    public async Task<DayView> DayAsync(DateOnly date, Guid? employeeId = null)
    {
        var actor = await current.GetAsync();
        var day = await (await QueryAsync(date, date, employeeId ?? actor.Id)).Select(x => new
        {
            x.Id,
            x.WorkDate,
            x.DisplayName,
            x.ClockInTime,
            x.StartTime,
            x.MinutesPerUnit,
            x.Version,
            x.ReopenedUntil,
            Units = x.Entries.Sum(e => (int?)e.Units) ?? 0
        }).SingleOrDefaultAsync();
        if (day is null)
        {
            var grant = await ActiveGrantAsync(employeeId ?? actor.Id, date);
            return new(null, date, actor.WindowsAccount ?? actor.DisplayName, null, null, null, policy.CanEdit(date, grant?.ReopenedUntil), grant?.ReopenedUntil, 0, 0, policy.Options.EditablePreviousDays, await DefaultMinutesPerUnitAsync());
        }
        return new(day.Id, day.WorkDate, day.DisplayName, day.ClockInTime, day.StartTime, day.Version, policy.CanEdit(day.WorkDate, day.ReopenedUntil),
                day.ReopenedUntil, day.StartTime.Hour * 60 + day.StartTime.Minute + day.Units * day.MinutesPerUnit, day.Units, policy.Options.EditablePreviousDays, day.MinutesPerUnit);
    }

    private DayView View(DailyTimesheet day)
    {
        var units = day.Entries.Sum(x => x.Units);
        return new(day.Id, day.WorkDate, day.DisplayName, day.ClockInTime, day.StartTime, day.Version,
            policy.CanEdit(day.WorkDate, day.ReopenedUntil), day.ReopenedUntil, day.StartTime.Hour * 60 + day.StartTime.Minute + units * day.MinutesPerUnit, units, policy.Options.EditablePreviousDays, day.MinutesPerUnit);
    }

    public void SetVersion(AuditedEntity entity, byte[]? version)
    {
        if (version is null || version.Length != 8)
        {
            throw new BusinessException("Reload the timesheet before saving.", 409);
        }

        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
    }

    private static void Touch(AuditedEntity entity, Employee actor)
    {
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = actor.WindowsSid ?? actor.Id.ToString();
    }

    public async Task<DayView> SaveDayAsync(DayInput input)
    {
        if (input.WorkDate is null || input.ClockInTime is null || string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Length > 160)
        {
            throw new BusinessException("Enter a date, display name and clock-in time.");
        }

        var actor = await current.GetAsync();
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null;
        var existing = await db.DailyTimesheets.Include(x => x.Entries).SingleOrDefaultAsync(x => x.EmployeeId == actor.Id && x.WorkDate == input.WorkDate);
        var createdDay = existing is null;
        var previousClock = existing?.ClockInTime;
        var previousName = existing?.DisplayName;
        DailyTimesheet day;
        if (existing is null)
        {
            var minutesPerUnit = await DefaultMinutesPerUnitAsync();
            if (input.MinutesPerUnit != minutesPerUnit)
                throw new BusinessException("The unit setting changed. Reload this page before creating the timesheet.", 409);
            day = new DailyTimesheet { EmployeeId = actor.Id, WorkDate = input.WorkDate.Value, CreatedBy = actor.WindowsSid!, MinutesPerUnit = minutesPerUnit };
            var grant = await ActiveGrantAsync(actor.Id, input.WorkDate.Value);
            if (grant is not null)
            {
                day.ReopenedUntil = grant.ReopenedUntil;
                day.ReopenedBy = grant.ReopenedBy;
                day.ReopenReason = grant.Reason;
            }
            db.DailyTimesheets.Add(day);
        }
        else
        {
            day = existing;
            SetVersion(day, input.Version);
        }

        policy.RequireEditable(day);
        day.ClockInTime = input.ClockInTime.Value;
        day.StartTime = policy.Start(day.ClockInTime);
        TimesheetPolicy.CheckLength(day.StartTime, day.Entries.Sum(x => x.Units), day.MinutesPerUnit);
        day.DisplayName = input.DisplayName.Trim();
        Touch(day, actor);
        var daySummary = ActivityLogService.DaySummary(createdDay, day.WorkDate, previousClock, day.ClockInTime, previousName, day.DisplayName);
        if (daySummary is not null)
        {
            activity.Add(actor, actor.Id, createdDay ? "DayStarted" : "DayUpdated", day.WorkDate, daySummary);
        }
        await db.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        logger.LogInformation("Daily timesheet {TimesheetId} saved for {WorkDate} by employee {EmployeeId}", day.Id, day.WorkDate, actor.Id);
        return View(day);
    }

    public async Task<TimeEntry> SaveAsync(Guid? id, EntryInput input)
    {
        if (input.WorkDate is null || input.Units is null || input.Units < 0 || input.Units > 1440 || input.TaskTypeId == Guid.Empty)
        {
            throw new BusinessException("Select a date, task type and whole units between 0 and 1440.");
        }

        var actor = await current.GetAsync();
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null;
        var day = await db.DailyTimesheets.Include(x => x.Entries).SingleOrDefaultAsync(x => x.EmployeeId == actor.Id && x.WorkDate == input.WorkDate)
            ?? throw new BusinessException("Save the daily clock-in details before adding tasks.");
        TimeEntry? entry;
        if (id.HasValue)
        {
            entry = day.Entries.SingleOrDefault(x => x.Id == id) ?? throw new BusinessException("Entry not found in your timesheet.", 404);
        }
        else
        {
            if (input.SubmissionId == Guid.Empty)
            {
                throw new BusinessException("A submission identifier is required.");
            }

            entry = day.Entries.SingleOrDefault(x => x.SubmissionId == input.SubmissionId);
            if (entry is not null)
            {
                return entry;
            }
        }
        policy.RequireEditable(day);
        SetVersion(day, input.DayVersion);
        var type = await db.TaskTypes.SingleOrDefaultAsync(x => x.Id == input.TaskTypeId) ?? throw new BusinessException("Task type does not exist.");
        if (!type.IsActive && (entry is null || entry.TaskTypeId != type.Id))
        {
            throw new BusinessException("Choose an active task type.");
        }

        var description = input.Description?.Trim() ?? "";
        if (description.Length > 4000)
        {
            throw new BusinessException("Description must be 4000 characters or fewer.");
        }

        if (type.DescriptionRule == DescriptionRule.Required && description.Length == 0)
        {
            throw new BusinessException("This task type requires a description.");
        }

        if (type.DescriptionRule == DescriptionRule.FilesClosed)
        {
            if (input.FilesClosed is null || input.FilesClosed < 0 || input.FilesClosed > 1000000)
            {
                throw new BusinessException("Enter the number of files closed.");
            }

            var detail = $"Closed {input.FilesClosed} files";
            if (description.Length == 0)
            {
                description = detail;
            }
            else if (!description.Contains(detail, StringComparison.OrdinalIgnoreCase))
            {
                description = detail + ". " + description;
            }

            if (description.Length > 4000)
            {
                throw new BusinessException("Shorten the description to leave room for the file count.");
            }
        }
        var createdEntry = entry is null;
        var previousUnits = entry?.Units;
        var previousFiles = entry?.FilesClosed;
        string? previousTypeName = null;
        if (entry is not null)
        {
            previousTypeName = entry.TaskTypeId == type.Id
                ? type.Name
                : await db.TaskTypes.Where(x => x.Id == entry.TaskTypeId).Select(x => x.Name).SingleAsync();
        }

        if (entry is null)
        {
            entry = new TimeEntry
            {
                DailyTimesheetId = day.Id,
                Position = day.Entries.Select(x => x.Position).DefaultIfEmpty().Max() + 1,
                SubmissionId = input.SubmissionId,
                CreatedBy = actor.WindowsSid!,
                IsBreak = type.IsBreak,
                TaskTypeId = type.Id
            };
            day.Entries.Add(entry);
            db.TimeEntries.Add(entry);
        }
        else
        {
            SetVersion(entry, input.Version);
            if (entry.TaskTypeId != type.Id)
            {
                entry.IsBreak = type.IsBreak;
            }
        }
        entry.TaskTypeId = type.Id;
        entry.Units = input.Units.Value;
        entry.Description = description;
        entry.FilesClosed = type.DescriptionRule == DescriptionRule.FilesClosed ? input.FilesClosed : null;
        var taskSummary = ActivityLogService.TaskSummary(day.WorkDate, createdEntry, type.Name, entry.Units, previousTypeName, previousUnits, previousFiles, entry.FilesClosed);
        if (taskSummary is not null)
        {
            activity.Add(actor, actor.Id, createdEntry ? "TaskAdded" : "TaskUpdated", day.WorkDate, taskSummary);
        }
        TimesheetPolicy.CheckLength(day.StartTime, day.Entries.Sum(x => x.Units), day.MinutesPerUnit);
        Touch(entry, actor);
        Touch(day, actor);
        await db.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        logger.LogInformation("Task {EntryId} {Operation} in timesheet {TimesheetId} with {Units} units", entry.Id, id.HasValue ? "updated" : "created", day.Id, entry.Units);
        return entry;
    }

    public async Task DeleteAsync(Guid id, byte[]? version, byte[]? dayVersion)
    {
        var actor = await current.GetAsync();
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null;
        var entry = await db.TimeEntries.Include(x => x.DailyTimesheet).Include(x => x.TaskType).SingleOrDefaultAsync(x => x.Id == id && x.DailyTimesheet.EmployeeId == actor.Id)
            ?? throw new BusinessException("Entry not found in your timesheet.", 404);
        policy.RequireEditable(entry.DailyTimesheet);
        SetVersion(entry, version);
        SetVersion(entry.DailyTimesheet, dayVersion);
        Touch(entry.DailyTimesheet, actor);
        activity.Add(actor, actor.Id, "TaskRemoved", entry.DailyTimesheet.WorkDate, ActivityLogService.RemovedSummary(entry.DailyTimesheet.WorkDate, entry.TaskType.Name, entry.Units));
        db.TimeEntries.Remove(entry);
        await db.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        logger.LogInformation("Task {EntryId} deleted from timesheet {TimesheetId} by employee {EmployeeId}", id, entry.DailyTimesheetId, actor.Id);
    }

    public async Task ReopenAsync(ReopenInput input)
    {
        var actor = await current.RequireAdminAsync();
        if (input.WorkDate > policy.Today)
        {
            throw new BusinessException("Future timesheets cannot be reopened.");
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500)
        {
            throw new BusinessException("Enter a reason of up to 500 characters.");
        }

        var subjectName = await db.Employees.Where(x => x.Id == input.EmployeeId).Select(x => x.DisplayName).SingleOrDefaultAsync();
        if (subjectName is null)
        {
            throw new BusinessException("Employee not found.", 404);
        }

        var until = policy.Now.AddHours(policy.Options.ReopenHours);
        var reason = input.Reason.Trim();
        activity.Add(actor, input.EmployeeId, "DayReopened", input.WorkDate, ActivityLogService.ReopenSummary(input.WorkDate, subjectName));
        var day = await db.DailyTimesheets.SingleOrDefaultAsync(x => x.EmployeeId == input.EmployeeId && x.WorkDate == input.WorkDate);
        if (day is not null)
        {
            SetVersion(day, input.Version);
            day.ReopenedUntil = until;
            day.ReopenedBy = actor.WindowsSid;
            day.ReopenReason = reason;
            Touch(day, actor);
            await db.SaveChangesAsync();
            logger.LogInformation("Timesheet {TimesheetId} reopened until {ReopenedUntil} by employee {EmployeeId}", day.Id, day.ReopenedUntil, actor.Id);
            return;
        }

        var grant = await db.TimesheetReopens.SingleOrDefaultAsync(x => x.EmployeeId == input.EmployeeId && x.WorkDate == input.WorkDate);
        if (grant is null)
        {
            grant = new TimesheetReopen { EmployeeId = input.EmployeeId, WorkDate = input.WorkDate };
            db.TimesheetReopens.Add(grant);
        }
        grant.ReopenedUntil = until;
        grant.ReopenedBy = actor.WindowsSid ?? actor.Id.ToString();
        grant.Reason = reason;
        await db.SaveChangesAsync();
        logger.LogInformation("Date {WorkDate} reopened until {ReopenedUntil} for employee {EmployeeId} before a timesheet exists", input.WorkDate, until, input.EmployeeId);
    }

    private Task<TimesheetReopen?> ActiveGrantAsync(Guid employeeId, DateOnly date) =>
        db.TimesheetReopens.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.WorkDate == date && x.ReopenedUntil > policy.Now);

    private async Task<List<DateOnly>> GrantDatesAsync(DateOnly from, DateOnly to, Guid? employeeId)
    {
        var actor = await current.GetAsync();
        if (!await current.IsAdminAsync())
        {
            employeeId = actor.Id;
        }

        var grants = db.TimesheetReopens.AsNoTracking().Where(x => x.WorkDate >= from && x.WorkDate <= to && x.ReopenedUntil > policy.Now);
        if (employeeId.HasValue)
        {
            grants = grants.Where(x => x.EmployeeId == employeeId);
        }

        return await grants.Select(x => x.WorkDate).Distinct().ToListAsync();
    }
}
