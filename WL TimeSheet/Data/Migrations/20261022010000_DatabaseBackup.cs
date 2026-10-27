using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WL_TimeSheet.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupDirectory",
                table: "AdministrationSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "BackupEnabled",
                table: "AdministrationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BackupIntervalDays",
                table: "AdministrationSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "BackupRetentionDays",
                table: "AdministrationSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastBackupAt",
                table: "AdministrationSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastBackupAttemptAt",
                table: "AdministrationSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastBackupError",
                table: "AdministrationSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdministrationSettings_BackupIntervalDays",
                table: "AdministrationSettings",
                sql: "[BackupIntervalDays] BETWEEN 1 AND 365");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdministrationSettings_BackupRetentionDays",
                table: "AdministrationSettings",
                sql: "[BackupRetentionDays] BETWEEN 1 AND 3650 AND [BackupRetentionDays] >= [BackupIntervalDays]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AdministrationSettings_BackupIntervalDays",
                table: "AdministrationSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AdministrationSettings_BackupRetentionDays",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "BackupDirectory",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "BackupEnabled",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "BackupIntervalDays",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "BackupRetentionDays",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "LastBackupAt",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "LastBackupAttemptAt",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "LastBackupError",
                table: "AdministrationSettings");
        }
    }
}
