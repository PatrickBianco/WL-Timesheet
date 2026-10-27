using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;

namespace WL_TimeSheet.Services;

public sealed class ActivityLogService(AppDbContext db, TimesheetPolicy policy)
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-AU");

    public void Add(Employee actor, Guid subjectEmployeeId, string action, DateOnly workDate, string summary)
    {
        db.ActivityEvents.Add(new ActivityEvent
        {
            OccurredAt = policy.Now,
            ActorEmployeeId = actor.Id,
            SubjectEmployeeId = subjectEmployeeId,
            Action = action,
            WorkDate = workDate,
            Summary = summary.Length <= 500 ? summary : summary[..500]
        });
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = policy.Now.AddMonths(-ActivityEvent.RetentionMonths);
        return db.ActivityEvents.Where(x => x.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ActivityLogPage> PageAsync(Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize)
    {
        if (page < 1 || pageSize is not (10 or 20 or 50 or 100))
        {
            throw new BusinessException("Choose a valid page and a page size of 10, 20, 50 or 100.");
        }

        if (from > to)
        {
            (from, to) = (to, from);
        }

        await PurgeExpiredAsync();
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Select(x => new ActivityEmployeeOption(x.Id, x.DisplayName))
            .ToListAsync();
        var query = db.ActivityEvents.AsNoTracking();
        if (employeeId.HasValue)
        {
            query = query.Where(x => x.SubjectEmployeeId == employeeId || x.ActorEmployeeId == employeeId);
        }

        if (from.HasValue)
        {
            query = query.Where(x => x.WorkDate >= from);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.WorkDate <= to);
        }

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, pages);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(policy.Options.TimeZone);
        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.OccurredAt,
                ActorName = x.Actor.DisplayName,
                SubjectName = x.Subject.DisplayName,
                x.WorkDate,
                x.Summary,
                OwnTimesheet = x.ActorEmployeeId == x.SubjectEmployeeId
            })
            .ToListAsync();

        return new ActivityLogPage(
            rows.Select(x => new ActivityLogRow(
                TimeZoneInfo.ConvertTime(x.OccurredAt, zone).ToString("d MMM yyyy, h:mm tt", DisplayCulture),
                x.ActorName,
                x.SubjectName,
                x.WorkDate.ToString("d MMM yyyy", DisplayCulture),
                x.Summary,
                x.OwnTimesheet)).ToList(),
            employees,
            page,
            pageSize,
            pages,
            total,
            employeeId,
            from,
            to);
    }

    public static string? DaySummary(bool created, DateOnly workDate, TimeOnly? previousClock, TimeOnly clock, string? previousName, string name)
    {
        var day = FormatDay(workDate);
        var clockText = clock.ToString("HH:mm", DisplayCulture);
        if (created)
        {
            return $"Started {day}. Clock-in {clockText}.";
        }

        var changes = new List<string>();
        if (previousClock != clock)
        {
            changes.Add($"clock-in from {previousClock:HH:mm} to {clockText}");
        }

        if (!string.Equals(previousName, name, StringComparison.Ordinal))
        {
            changes.Add($"display name from {previousName} to {name}");
        }

        return changes.Count == 0 ? null : $"Updated {day}: {string.Join("; ", changes)}.";
    }

    public static string? TaskSummary(DateOnly workDate, bool created, string typeName, int units, string? previousTypeName, int? previousUnits, int? previousFiles, int? files)
    {
        var day = FormatDay(workDate);
        if (created)
        {
            return $"Added {typeName}, {units} units, on {day}.";
        }

        var changes = new List<string>();
        if (!string.Equals(previousTypeName, typeName, StringComparison.Ordinal))
        {
            changes.Add($"task type from {previousTypeName} to {typeName}");
        }

        if (previousUnits != units)
        {
            changes.Add($"units from {previousUnits} to {units}");
        }

        if (previousFiles != files)
        {
            changes.Add($"files closed from {FormatFiles(previousFiles)} to {FormatFiles(files)}");
        }

        return changes.Count == 0 ? null : $"Updated {typeName} on {day}: {string.Join("; ", changes)}.";
    }

    public static string RemovedSummary(DateOnly workDate, string typeName, int units) =>
        $"Removed {typeName}, {units} units, on {FormatDay(workDate)}.";

    public static string ReopenSummary(DateOnly workDate, string subjectName) =>
        $"Reopened {FormatDay(workDate)} for {subjectName}.";

    private static string FormatDay(DateOnly workDate) => workDate.ToString("d MMM yyyy", DisplayCulture);

    private static string FormatFiles(int? files) => files?.ToString(DisplayCulture) ?? "none";
}

public sealed record ActivityEmployeeOption(Guid Id, string DisplayName);

public sealed record ActivityLogRow(string When, string ActorName, string SubjectName, string WorkDate, string Summary, bool OwnTimesheet);

public sealed record ActivityLogPage(
    IReadOnlyList<ActivityLogRow> Rows,
    IReadOnlyList<ActivityEmployeeOption> Employees,
    int Page,
    int PageSize,
    int Pages,
    int Total,
    Guid? EmployeeId,
    DateOnly? From,
    DateOnly? To);
