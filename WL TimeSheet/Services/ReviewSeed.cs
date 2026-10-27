using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Services;

public static class ReviewSeed
{
    public const string ReviewerSid = "client-review-sample-user";
    public static async Task InitializeAsync(AppDbContext db, TimesheetPolicy policy)
    {
        if (await db.Employees.AnyAsync(x => x.WindowsSid == ReviewerSid))
        {
            return;
        }

        var employee = new Employee { WindowsSid = ReviewerSid, WindowsAccount = "Client Reviewer", DisplayName = "Client Reviewer" };
        db.Employees.Add(employee);
        var work = await db.TaskTypes.OrderBy(x => x.Name).FirstAsync(x => x.IsActive && !x.IsBreak && x.DescriptionRule == DescriptionRule.Optional);
        var pause = await db.TaskTypes.FirstAsync(x => x.IsActive && x.IsBreak);
        for (var offset = 0; offset < 3; offset++)
        {
            var day = new DailyTimesheet { EmployeeId = employee.Id, WorkDate = policy.Today.AddDays(-offset), DisplayName = "Client Reviewer", ClockInTime = new(8, 36), StartTime = new(8, 36) };
            for (var i = 0; i < 6; i++)
            {
                day.Entries.Add(new TimeEntry { Position = i + 1, TaskTypeId = i == 2 ? pause.Id : work.Id, IsBreak = i == 2, Units = i == 2 ? 12 : 30, Description = i == 2 ? "Sample break" : "Sample task for client review", SubmissionId = Guid.NewGuid() });
            }

            db.DailyTimesheets.Add(day);
        }
        await db.SaveChangesAsync();
    }
}
