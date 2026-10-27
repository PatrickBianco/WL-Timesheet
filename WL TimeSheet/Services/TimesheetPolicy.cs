using Microsoft.Extensions.Options;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Services;

public sealed class TimesheetOptions
{
    public string TimeZone { get; set; } = "Australia/Adelaide";
    public int? EditablePreviousDays { get; set; } = 3;
    public int ReopenHours { get; set; } = 24;
}
public sealed class TimesheetPolicy(IOptions<TimesheetOptions> options, TimeProvider clock)
{
    public TimesheetOptions Options => options.Value;
    public DateTimeOffset Now => clock.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.FindSystemTimeZoneById(Options.TimeZone)).DateTime);
    public bool CanEdit(DateOnly day, DateTimeOffset? reopenedUntil = null) =>
        day <= Today &&
        (
            reopenedUntil > Now ||
            Options.EditablePreviousDays is null ||
            day >= Today.AddDays(-Options.EditablePreviousDays.Value)
        );
    public void RequireEditable(DailyTimesheet day)
    {
        if (!CanEdit(day.WorkDate, day.ReopenedUntil))
        {
            throw new BusinessException("This timesheet is read-only. An administrator can reopen a past timesheet.", 403);
        }
    }

    public TimeOnly Start(TimeOnly clockIn)
    {
        if (clockIn.Ticks % TimeSpan.TicksPerMinute != 0) 
        {
            throw new BusinessException("Enter clock-in time to the nearest minute.");
        }

        return clockIn;
    }

    public static void CheckLength(TimeOnly start, int units, int minutesPerUnit = 1)
    {
        if (minutesPerUnit is < 1 or > 60 || units < 0 || start.Hour * 60 + start.Minute + (long)units * minutesPerUnit > 1440)
        {
            throw new BusinessException("These units extend beyond midnight. Reduce the units or use a separate day.");
        }
    }
}
