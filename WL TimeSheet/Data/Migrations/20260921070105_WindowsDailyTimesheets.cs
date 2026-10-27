using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WL_TimeSheet.Data.Migrations
{
    /// <inheritdoc />
    public partial class WindowsDailyTimesheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy independent time ranges cannot be silently converted into a six-minute chained day.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [TimeEntries]) THROW 51000, 'Existing legacy time entries require a reviewed conversion before this migration. No records have been changed.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Employees_EmployeeId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_EmployeeId_WorkDate_StartTime",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_WorkDate",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_MinutePrecision",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_TimeRange",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Employees_Email",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_ExternalIdentityId",
                table: "Employees");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Employees_Role",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "WorkDate",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "TimeEntries",
                newName: "DailyTimesheetId");

            migrationBuilder.RenameIndex(
                name: "IX_TimeEntries_EmployeeId_SubmissionId",
                table: "TimeEntries",
                newName: "IX_TimeEntries_DailyTimesheetId_SubmissionId");

            migrationBuilder.RenameColumn(
                name: "ExternalIdentityId",
                table: "Employees",
                newName: "WindowsAccount");

            migrationBuilder.AlterColumn<int>(
                name: "Units",
                table: "TimeEntries",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(7,2)",
                oldPrecision: 7,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "FilesClosed",
                table: "TimeEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBreak",
                table: "TimeEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "TimeEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DescriptionRule",
                table: "TaskTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsBreak",
                table: "TaskTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Employees",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254);

            migrationBuilder.AddColumn<string>(
                name: "WindowsSid",
                table: "Employees",
                type: "nvarchar(184)",
                maxLength: 184,
                nullable: true);

            migrationBuilder.Sql("UPDATE [Employees] SET [WindowsAccount]=NULL, [IsActive]=0 WHERE [WindowsSid] IS NULL;");

            migrationBuilder.CreateTable(
                name: "AdministrationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SecurityStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrationSettings", x => x.Id);
                    table.CheckConstraint("CK_AdministrationSettings_Singleton", "[Id]=1");
                });

            migrationBuilder.CreateTable(
                name: "DailyTimesheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ClockInTime = table.Column<TimeOnly>(type: "time(0)", precision: 0, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", precision: 0, nullable: false),
                    ReopenedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyTimesheets", x => x.Id);
                    table.CheckConstraint("CK_DailyTimesheets_Minutes", "DATEPART(SECOND,[ClockInTime])=0 AND DATEPART(SECOND,[StartTime])=0");
                    table.ForeignKey(
                        name: "FK_DailyTimesheets_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_DailyTimesheetId_Position",
                table: "TimeEntries",
                columns: new[] { "DailyTimesheetId", "Position" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_FilesClosed",
                table: "TimeEntries",
                sql: "[FilesClosed] IS NULL OR [FilesClosed] BETWEEN 0 AND 1000000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_Position",
                table: "TimeEntries",
                sql: "[Position] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries",
                sql: "[Units] BETWEEN 0 AND 240");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskTypes_DescriptionRule",
                table: "TaskTypes",
                sql: "[DescriptionRule] BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_WindowsSid",
                table: "Employees",
                column: "WindowsSid",
                unique: true,
                filter: "[WindowsSid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTimesheets_EmployeeId_WorkDate",
                table: "DailyTimesheets",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyTimesheets_WorkDate",
                table: "DailyTimesheets",
                column: "WorkDate");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_DailyTimesheets_DailyTimesheetId",
                table: "TimeEntries",
                column: "DailyTimesheetId",
                principalTable: "DailyTimesheets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
CREATE VIEW [dbo].[TimesheetReporting] AS
SELECT e.Id AS EntryId, d.Id AS TimesheetId, p.WindowsAccount AS [User], d.DisplayName, d.WorkDate,
e.Position, e.Units, e.Units*6 AS DurationMinutes, e.IsBreak,
CASE WHEN e.IsBreak=1 THEN 0 ELSE e.Units*6 END AS ProductiveMinutes,
t.Name AS TaskType, e.Description, e.FilesClosed, t.ExplanatoryNotes,
DATEADD(MINUTE, DATEDIFF(MINUTE,CAST('00:00' AS time),d.StartTime)+
COALESCE(SUM(e.Units*6) OVER(PARTITION BY d.Id ORDER BY e.Position ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0), CAST(d.WorkDate AS datetime2)) AS StartAt,
DATEADD(MINUTE, DATEDIFF(MINUTE,CAST('00:00' AS time),d.StartTime)+
SUM(e.Units*6) OVER(PARTITION BY d.Id ORDER BY e.Position ROWS UNBOUNDED PRECEDING), CAST(d.WorkDate AS datetime2)) AS FinishAt,
e.CreatedAt, e.UpdatedAt, e.CreatedBy, e.UpdatedBy
FROM TimeEntries e JOIN DailyTimesheets d ON e.DailyTimesheetId=d.Id
JOIN Employees p ON d.EmployeeId=p.Id JOIN TaskTypes t ON e.TaskTypeId=t.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Employees]) THROW 51000, 'Use a reviewed backup restore to revert a populated Windows-authentication deployment.', 1;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[TimesheetReporting];");
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_DailyTimesheets_DailyTimesheetId",
                table: "TimeEntries");

            migrationBuilder.DropTable(
                name: "AdministrationSettings");

            migrationBuilder.DropTable(
                name: "DailyTimesheets");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_DailyTimesheetId_Position",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_FilesClosed",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_Position",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskTypes_DescriptionRule",
                table: "TaskTypes");

            migrationBuilder.DropIndex(
                name: "IX_Employees_WindowsSid",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FilesClosed",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "IsBreak",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "DescriptionRule",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "IsBreak",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "WindowsSid",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "DailyTimesheetId",
                table: "TimeEntries",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_TimeEntries_DailyTimesheetId_SubmissionId",
                table: "TimeEntries",
                newName: "IX_TimeEntries_EmployeeId_SubmissionId");

            migrationBuilder.RenameColumn(
                name: "WindowsAccount",
                table: "Employees",
                newName: "ExternalIdentityId");

            migrationBuilder.AlterColumn<decimal>(
                name: "Units",
                table: "TimeEntries",
                type: "decimal(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "TimeEntries",
                type: "time(0)",
                precision: 0,
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "TimeEntries",
                type: "time(0)",
                precision: 0,
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<DateOnly>(
                name: "WorkDate",
                table: "TimeEntries",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Employees",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Employees",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_EmployeeId_WorkDate_StartTime",
                table: "TimeEntries",
                columns: new[] { "EmployeeId", "WorkDate", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_WorkDate",
                table: "TimeEntries",
                column: "WorkDate");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_MinutePrecision",
                table: "TimeEntries",
                sql: "DATEPART(SECOND,[StartTime]) = 0 AND DATEPART(SECOND,[EndTime]) = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_TimeRange",
                table: "TimeEntries",
                sql: "[EndTime] > [StartTime]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeEntries_Units",
                table: "TimeEntries",
                sql: "[Units] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                table: "Employees",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ExternalIdentityId",
                table: "Employees",
                column: "ExternalIdentityId",
                unique: true,
                filter: "[ExternalIdentityId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Employees_Role",
                table: "Employees",
                sql: "[Role] IN ('Admin','Employee')");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Employees_EmployeeId",
                table: "TimeEntries",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
