using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WL_TimeSheet.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinutesPerUnit",
                table: "DailyTimesheets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MinutesPerUnit",
                table: "AdministrationSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyTimesheets_MinutesPerUnit",
                table: "DailyTimesheets",
                sql: "[MinutesPerUnit] BETWEEN 1 AND 60");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdministrationSettings_MinutesPerUnit",
                table: "AdministrationSettings",
                sql: "[MinutesPerUnit] BETWEEN 1 AND 60");
            migrationBuilder.Sql("EXEC(N'" + ReportingView("d.MinutesPerUnit").Replace("'", "''") + "');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [dbo].[DailyTimesheets] WHERE [MinutesPerUnit] <> 1) THROW 51000, 'Timesheets use custom unit settings. Restore a reviewed backup to roll back.', 1;");
            migrationBuilder.Sql("EXEC(N'" + ReportingView("1").Replace("'", "''") + "');");
            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyTimesheets_MinutesPerUnit",
                table: "DailyTimesheets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AdministrationSettings_MinutesPerUnit",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "MinutesPerUnit",
                table: "DailyTimesheets");

            migrationBuilder.DropColumn(
                name: "MinutesPerUnit",
                table: "AdministrationSettings");
        }
        private static string ReportingView(string minutesPerUnit) => $@"
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
