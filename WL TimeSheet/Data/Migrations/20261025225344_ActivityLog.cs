using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WL_TimeSheet.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Employees_ActorEmployeeId",
                        column: x => x.ActorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Employees_SubjectEmployeeId",
                        column: x => x.SubjectEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_ActorEmployeeId",
                table: "ActivityEvents",
                column: "ActorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_OccurredAt",
                table: "ActivityEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_SubjectEmployeeId_WorkDate",
                table: "ActivityEvents",
                columns: new[] { "SubjectEmployeeId", "WorkDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");
        }
    }
}
