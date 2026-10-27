using System.ComponentModel.DataAnnotations;
namespace WL_TimeSheet.Models;

public abstract class AuditedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public string UpdatedBy { get; set; } = "system";
    public byte[] Version { get; set; } = [];
}
public sealed class Employee : AuditedEntity
{
    public string? WindowsSid
    {
        get; set;
    }

    public string? WindowsAccount
    {
        get; set;
    }

    public string? Email
    {
        get; set;
    }

    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public enum DescriptionRule
{
    Optional, Required, FilesClosed
}
public sealed class TaskType : AuditedEntity
{
    public string Name { get; set; } = "";
    public string ExplanatoryNotes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsBreak
    {
        get; set;
    }

    public DescriptionRule DescriptionRule
    {
        get; set;
    }
}
public sealed class DailyTimesheet : AuditedEntity
{
    // Snapshot the administrator's default when the day is first created.
    public int MinutesPerUnit { get; set; } = 1;
    public Guid EmployeeId
    {
        get; set;
    }

    public Employee Employee { get; set; } = null!;
    public DateOnly WorkDate
    {
        get; set;
    }

    public string DisplayName { get; set; } = "";
    public TimeOnly ClockInTime
    {
        get; set;
    }

    public TimeOnly StartTime
    {
        get; set;
    }

    public DateTimeOffset? ReopenedUntil
    {
        get; set;
    }

    public string? ReopenedBy
    {
        get; set;
    }

    public string? ReopenReason
    {
        get; set;
    }

    public List<TimeEntry> Entries { get; set; } = [];
}
public sealed class ActivityEvent
{
    public const int RetentionMonths = 2;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
    public Guid ActorEmployeeId { get; set; }
    public Employee Actor { get; set; } = null!;
    public Guid SubjectEmployeeId { get; set; }
    public Employee Subject { get; set; } = null!;
    public string Action { get; set; } = "";
    public DateOnly WorkDate { get; set; }
    public string Summary { get; set; } = "";
}
public sealed class TimesheetReopen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
    public DateTimeOffset ReopenedUntil { get; set; }
    public string ReopenedBy { get; set; } = "";
    public string Reason { get; set; } = "";
}
public sealed class TimeEntry : AuditedEntity
{
    public Guid DailyTimesheetId
    {
        get; set;
    }

    public DailyTimesheet DailyTimesheet { get; set; } = null!;
    public int Position
    {
        get; set;
    }

    public int Units
    {
        get; set;
    }

    public Guid TaskTypeId
    {
        get; set;
    }

    public TaskType TaskType { get; set; } = null!;
    public bool IsBreak
    {
        get; set;
    } // Snapshot: historical productivity never changes when a type is edited.
    public string Description { get; set; } = "";
    public int? FilesClosed
    {
        get; set;
    }

    public Guid SubmissionId
    {
        get; set;
    }
}
public sealed class AdministrationSettings
{
    public int MinutesPerUnit { get; set; } = 1;
    public bool BackupEnabled { get; set; }
    public int BackupIntervalDays { get; set; } = 7;
    public int BackupRetentionDays { get; set; } = 30;
    public string BackupDirectory { get; set; } = "";
    public DateTimeOffset? LastBackupAt { get; set; }
    public DateTimeOffset? LastBackupAttemptAt { get; set; }
    public string? LastBackupError { get; set; }
    public int Id { get; set; } = 1;
    public string PasswordHash { get; set; } = "";
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ModifiedBy { get; set; } = "system";
    public byte[] Version { get; set; } = [];
}
public sealed class EntryInput
{
    [Required]
    public DateOnly? WorkDate
    {
        get; set;
    }
    [Required, Range(0, 1440)]
    public int? Units
    {
        get; set;
    }

    public Guid TaskTypeId
    {
        get; set;
    }
    [StringLength(4000)]
    public string? Description
    {
        get; set;
    }
    [Range(0, 1000000)]
    public int? FilesClosed
    {
        get; set;
    }

    public Guid SubmissionId
    {
        get; set;
    }

    public byte[]? Version
    {
        get; set;
    }

    public byte[]? DayVersion
    {
        get; set;
    }
}
public sealed class DayInput
{
    [Required, Range(1, 60)]
    public int? MinutesPerUnit { get; set; }
    [Required]
    public DateOnly? WorkDate
    {
        get; set;
    }
    [Required]
    public TimeOnly? ClockInTime
    {
        get; set;
    }
    [Required, StringLength(160)] public string DisplayName { get; set; } = "";
    public byte[]? Version
    {
        get; set;
    }
}
public sealed class EmployeeInput
{
    [Required, StringLength(160)] public string DisplayName { get; set; } = "";
    [EmailAddress, StringLength(254)]
    public string? Email
    {
        get; set;
    }

    public bool IsActive { get; set; } = true;
    public byte[]? Version
    {
        get; set;
    }
}
public sealed class TaskTypeInput
{
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(2000)] public string ExplanatoryNotes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsBreak
    {
        get; set;
    }
    [EnumDataType(typeof(DescriptionRule))]
    public DescriptionRule DescriptionRule
    {
        get; set;
    }

    public byte[]? Version
    {
        get; set;
    }
}
public sealed record EntryRow(Guid Id, Guid DailyTimesheetId, Guid EmployeeId, string EmployeeName,
    string WindowsAccount, DateOnly WorkDate, int Position, int StartMinute, int EndMinute, int Units,
    Guid TaskTypeId, string TaskTypeName, string ExplanatoryNotes, string Description, int? FilesClosed,
    bool IsBreak, byte[] Version, byte[] DayVersion, bool CanEdit, int MinutesPerUnit = 1)
{
    public string StartTime => $"{StartMinute / 60:00}:{StartMinute % 60:00}";
    public string EndTime => $"{EndMinute / 60:00}:{EndMinute % 60:00}";
    public int DurationMinutes => Units * MinutesPerUnit;
    public int ProductiveMinutes => IsBreak ? 0 : DurationMinutes;
}
public sealed record DayView(Guid? Id, DateOnly WorkDate, string DisplayName, TimeOnly? ClockInTime,
    TimeOnly? StartTime, byte[]? Version, bool CanEdit, DateTimeOffset? ReopenedUntil, int NextStartMinute,
    int TotalUnits, int? EditablePreviousDays, int MinutesPerUnit = 1);

public sealed class UnitSettingsInput
{
    [Required, Range(1, 60, ErrorMessage = "Enter a whole number from 1 to 60 minutes per unit.")]
    public int? MinutesPerUnit { get; set; }
    [Required]
    public string Version { get; set; } = "";
}
public sealed class BackupSettingsInput
{
    public bool BackupEnabled { get; set; }
    [Required, Range(1, 365, ErrorMessage = "Enter how many days between backups, from 1 to 365. 7 is one week.")]
    public int? BackupIntervalDays { get; set; } = 7;
    [Required, Range(1, 3650, ErrorMessage = "Enter how many days to keep backups, from 1 to 3650. 30 is about one month.")]
    public int? BackupRetentionDays { get; set; } = 30;
    [StringLength(400, ErrorMessage = "The backup folder path is too long.")]
    public string BackupDirectory { get; set; } = "";
    [Required]
    public string Version { get; set; } = "";
    public string LastBackupText { get; set; } = "";
    public string NextBackupText { get; set; } = "";
    public string FilesText { get; set; } = "";
    public string? LastBackupError { get; set; }
}
public sealed class ReopenInput
{
    public Guid EmployeeId
    {
        get; set;
    }

    public DateOnly WorkDate
    {
        get; set;
    }
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    public byte[]? Version
    {
        get; set;
    }
}
public sealed class UnlockInput
{
    [Required] public string Password { get; set; } = "";
}
public sealed class ChangePasswordInput
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; set; } = "";
    [Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
}
public sealed class BusinessException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
