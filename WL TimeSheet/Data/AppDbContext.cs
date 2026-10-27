using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<DailyTimesheet> DailyTimesheets => Set<DailyTimesheet>();
    public DbSet<TimesheetReopen> TimesheetReopens => Set<TimesheetReopen>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<AdministrationSettings> AdministrationSettings => Set<AdministrationSettings>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var type in new[] { typeof(Employee), typeof(TaskType), typeof(DailyTimesheet), typeof(TimeEntry) })
        {
            b.Entity(type).HasKey(nameof(AuditedEntity.Id));
            b.Entity(type).Property(nameof(AuditedEntity.CreatedBy)).HasMaxLength(255);
            b.Entity(type).Property(nameof(AuditedEntity.UpdatedBy)).HasMaxLength(255);
            b.Entity(type).Property(nameof(AuditedEntity.Version)).IsRowVersion();
        }
        var e = b.Entity<Employee>();
        e.Property(x => x.Email).HasMaxLength(254);
        e.Property(x => x.WindowsSid).HasMaxLength(184);
        e.HasIndex(x => x.WindowsSid).IsUnique().HasFilter("[WindowsSid] IS NOT NULL");
        e.Property(x => x.WindowsAccount).HasMaxLength(255);
        e.Property(x => x.DisplayName).HasMaxLength(160);
        var t = b.Entity<TaskType>();
        t.Property(x => x.Name).HasMaxLength(120);
        t.HasIndex(x => x.Name).IsUnique();
        t.Property(x => x.ExplanatoryNotes).HasMaxLength(2000);
        t.ToTable(x => x.HasCheckConstraint("CK_TaskTypes_DescriptionRule", "[DescriptionRule] BETWEEN 0 AND 2"));
        var d = b.Entity<DailyTimesheet>();
        d.ToTable(x => x.HasCheckConstraint("CK_DailyTimesheets_MinutesPerUnit", "[MinutesPerUnit] BETWEEN 1 AND 60"));
        d.Property(x => x.DisplayName).HasMaxLength(160);
        d.Property(x => x.ClockInTime).HasPrecision(0);
        d.Property(x => x.StartTime).HasPrecision(0);
        d.Property(x => x.ReopenedBy).HasMaxLength(255);
        d.Property(x => x.ReopenReason).HasMaxLength(500);
        d.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        d.HasIndex(x => new { x.EmployeeId, x.WorkDate }).IsUnique();
        d.HasIndex(x => x.WorkDate);
        var activity = b.Entity<ActivityEvent>();
        activity.HasKey(x => x.Id);
        activity.Property(x => x.Action).HasMaxLength(40);
        activity.Property(x => x.Summary).HasMaxLength(500);
        activity.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        activity.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectEmployeeId).OnDelete(DeleteBehavior.Restrict);
        activity.HasIndex(x => x.OccurredAt);
        activity.HasIndex(x => new { x.SubjectEmployeeId, x.WorkDate });
        var reopen = b.Entity<TimesheetReopen>();
        reopen.HasKey(x => x.Id);
        reopen.Property(x => x.ReopenedBy).HasMaxLength(255);
        reopen.Property(x => x.Reason).HasMaxLength(500);
        reopen.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        reopen.HasIndex(x => new { x.EmployeeId, x.WorkDate }).IsUnique();
        d.ToTable(x => x.HasCheckConstraint("CK_DailyTimesheets_Minutes", "DATEPART(SECOND,[ClockInTime])=0 AND DATEPART(SECOND,[StartTime])=0"));
        var entry = b.Entity<TimeEntry>();
        entry.Property(x => x.Description).HasMaxLength(4000);
        entry.HasOne(x => x.DailyTimesheet).WithMany(x => x.Entries).HasForeignKey(x => x.DailyTimesheetId).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne(x => x.TaskType).WithMany().HasForeignKey(x => x.TaskTypeId).OnDelete(DeleteBehavior.Restrict);
        entry.HasIndex(x => new { x.DailyTimesheetId, x.Position }).IsUnique();
        entry.HasIndex(x => new { x.DailyTimesheetId, x.SubmissionId }).IsUnique();
        entry.ToTable(x =>
        {
            x.HasCheckConstraint("CK_TimeEntries_Units", "[Units] BETWEEN 0 AND 1440");
            x.HasCheckConstraint("CK_TimeEntries_Position", "[Position] > 0");
            x.HasCheckConstraint("CK_TimeEntries_FilesClosed", "[FilesClosed] IS NULL OR [FilesClosed] BETWEEN 0 AND 1000000");
        });
        var admin = b.Entity<AdministrationSettings>();
        admin.ToTable(x => x.HasCheckConstraint("CK_AdministrationSettings_MinutesPerUnit", "[MinutesPerUnit] BETWEEN 1 AND 60"));
        admin.HasKey(x => x.Id);
        admin.Property(x => x.Id).ValueGeneratedNever();
        admin.Property(x => x.PasswordHash).HasMaxLength(1000);
        admin.Property(x => x.ModifiedBy).HasMaxLength(255);
        admin.Property(x => x.BackupDirectory).HasMaxLength(400);
        admin.Property(x => x.LastBackupError).HasMaxLength(1000);
        admin.Property(x => x.Version).IsRowVersion();
        admin.ToTable(x => x.HasCheckConstraint("CK_AdministrationSettings_Singleton", "[Id]=1"));
        admin.ToTable(x => x.HasCheckConstraint("CK_AdministrationSettings_BackupIntervalDays", "[BackupIntervalDays] BETWEEN 1 AND 365"));
        admin.ToTable(x => x.HasCheckConstraint("CK_AdministrationSettings_BackupRetentionDays", "[BackupRetentionDays] BETWEEN 1 AND 3650 AND [BackupRetentionDays] >= [BackupIntervalDays]"));
    }
}
