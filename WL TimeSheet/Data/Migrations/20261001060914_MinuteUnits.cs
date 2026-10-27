using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WL_TimeSheet.Data.Migrations
{
    /// <inheritdoc />
    public partial class MinuteUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries",
                sql: "[Units] BETWEEN 0 AND 1440");

            // Preserve elapsed/productive time: an old unit represented six minutes.
            // EF records this migration so the conversion runs only once.
            migrationBuilder.Sql("UPDATE [dbo].[TimeEntries] SET [Units] = [Units] * 6;");
            // ALTER VIEW must start its own SQL batch, including in generated upgrade scripts.
            migrationBuilder.Sql("EXEC(N'" + ReportingView(1).Replace("'", "''") + "');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never silently round away new one-minute entries during rollback.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [dbo].[TimeEntries] WHERE [Units] % 6 <> 0) THROW 51000, 'Minute entries cannot be represented as six-minute units. Restore a reviewed backup to roll back.', 1;");
            migrationBuilder.Sql("UPDATE [dbo].[TimeEntries] SET [Units] = [Units] / 6;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries",
                sql: "[Units] BETWEEN 0 AND 240");
            migrationBuilder.Sql("EXEC(N'" + ReportingView(6).Replace("'", "''") + "');");
        }

        private static string ReportingView(int minutesPerUnit) => $@"
ALTER VIEW [dbo].[TimesheetReporting] AS
SELECT e.Id AS EntryId, d.Id AS TimesheetId, p.WindowsAccount AS [User], d.DisplayName, d.WorkDate,
e.Position, e.Units, e.Units*{minutesPerUnit} AS DurationMinutes, e.IsBreak,
CASE WHEN e.IsBreak=1 THEN 0 ELSE e.Units*{minutesPerUnit} END AS ProductiveMinutes,
t.Name AS TaskType, e.Description, e.FilesClosed, t.ExplanatoryNotes,
DATEADD(MINUTE, DATEDIFF(MINUTE,CAST('00:00' AS time),d.StartTime)+
COALESCE(SUM(e.Units*{minutesPerUnit}) OVER(PARTITION BY d.Id ORDER BY e.Position ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0), CAST(d.WorkDate AS datetime2)) AS StartAt,
DATEADD(MINUTE, DATEDIFF(MINUTE,CAST('00:00' AS time),d.StartTime)+
SUM(e.Units*{minutesPerUnit}) OVER(PARTITION BY d.Id ORDER BY e.Position ROWS UNBOUNDED PRECEDING), CAST(d.WorkDate AS datetime2)) AS FinishAt,
e.CreatedAt, e.UpdatedAt, e.CreatedBy, e.UpdatedBy
FROM TimeEntries e JOIN DailyTimesheets d ON e.DailyTimesheetId=d.Id
JOIN Employees p ON d.EmployeeId=p.Id JOIN TaskTypes t ON e.TaskTypeId=t.Id;";
    }
}
