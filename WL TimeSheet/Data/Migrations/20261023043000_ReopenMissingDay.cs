using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WL_TimeSheet.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReopenMissingDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimesheetReopens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReopenedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ReopenedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimesheetReopens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimesheetReopens_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetReopens_EmployeeId_WorkDate",
                table: "TimesheetReopens",
                columns: new[] { "EmployeeId", "WorkDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimesheetReopens");
        }
    }
}
