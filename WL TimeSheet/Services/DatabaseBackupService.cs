using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;

namespace WL_TimeSheet.Services;

public sealed class DatabaseBackupService(
    AppDbContext db,
    CurrentEmployee current,
    TimesheetPolicy policy,
    IWebHostEnvironment environment,
    ILogger<DatabaseBackupService> logger)
{
    public const string DefaultDirectory = @"C:\ProgramData\WLTimeSheet\Backups";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    public async Task<BackupSettingsInput> SettingsAsync()
    {
        await current.RequireAdminAsync();
        var settings = await db.AdministrationSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        return Describe(settings);
    }

    public async Task SaveAsync(BackupSettingsInput input)
    {
        var actor = await current.RequireAdminAsync();
        var interval = input.BackupIntervalDays ?? 0;
        var retention = input.BackupRetentionDays ?? 0;
        if (interval is < 1 or > 365)
            throw new BusinessException("Enter how many days between backups, from 1 to 365. 7 is one week.");
        if (retention is < 1 or > 3650)
            throw new BusinessException("Enter how many days to keep backups, from 1 to 3650. 30 is about one month.");
        if (retention < interval)
            throw new BusinessException("Keep backups at least as long as the interval, so the newest backup is not deleted before the next one.");

        var directory = ResolveDirectory(input.BackupDirectory, input.BackupEnabled);
        var version = new byte[8];
        if (!Convert.TryFromBase64String(input.Version ?? "", version, out var length) || length != 8)
            throw new BusinessException("Reload the backup settings before saving.", 409);

        var settings = await db.AdministrationSettings.SingleAsync(x => x.Id == 1);
        db.Entry(settings).Property(x => x.Version).OriginalValue = version;
        settings.BackupEnabled = input.BackupEnabled;
        settings.BackupIntervalDays = interval;
        settings.BackupRetentionDays = retention;
        settings.BackupDirectory = directory;
        settings.ModifiedAt = DateTimeOffset.UtcNow;
        settings.ModifiedBy = actor.WindowsSid!;
        if (settings.BackupEnabled)
        {
            try { Directory.CreateDirectory(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new BusinessException("The backup folder could not be created. " + exception.Message);
            }
        }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("Backup settings changed in another session. Reload this page before saving.", 409);
        }
        logger.LogInformation(
            "Database backup settings saved by employee {EmployeeId}: enabled {Enabled}, every {IntervalDays} days, keep {RetentionDays} days, folder {Directory}",
            actor.Id, settings.BackupEnabled, settings.BackupIntervalDays, settings.BackupRetentionDays, settings.BackupDirectory);
    }

    public Task RunNowAsync(CancellationToken cancellationToken = default) => RunAsync(scheduled: false, cancellationToken);

    public Task RunScheduledAsync(CancellationToken cancellationToken) => RunAsync(scheduled: true, cancellationToken);

    private async Task RunAsync(bool scheduled, CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            if (scheduled) return;
            throw new BusinessException("A database backup is already running.");
        }

        try
        {
            if (!scheduled) await current.RequireAdminAsync();
            await RunCoreAsync(scheduled, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task RunCoreAsync(bool scheduled, CancellationToken cancellationToken)
    {
        var settings = await db.AdministrationSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (settings is null) return;
        if (scheduled && !settings.BackupEnabled) return;

        var directory = string.IsNullOrWhiteSpace(settings.BackupDirectory) ? "" : settings.BackupDirectory;
        if (scheduled && string.IsNullOrWhiteSpace(directory)) return;
        if (!scheduled && string.IsNullOrWhiteSpace(directory))
            throw new BusinessException("Save a backup folder before running a backup.");

        var now = policy.Now;
        var due = IsDue(settings, now);
        if (scheduled && !due)
        {
            TryDeleteExpired(settings, directory, now);
            return;
        }

        var started = now;
        await SaveStatusAsync(started, succeeded: false, error: settings.LastBackupError, cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var file = await WriteBackupAsync(directory, cancellationToken);
            var removed = DeleteExpired(directory, DatabaseName(), settings.BackupRetentionDays, started);
            await SaveStatusAsync(started, succeeded: true, error: null, cancellationToken);
            logger.LogInformation("Database backup written to {BackupFile}; removed {ExpiredCount} expired backups", file, removed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var message = CleanError(exception);
            try { await SaveStatusAsync(started, succeeded: false, error: message, cancellationToken); }
            catch (Exception saveException) when (saveException is not OperationCanceledException)
            {
                logger.LogError(saveException, "Database backup failed and the error could not be saved");
            }
            logger.LogError(exception, "Database backup failed");
            if (!scheduled)
                throw new BusinessException("The database backup did not finish. " + message);
        }
    }

    private static bool IsDue(AdministrationSettings settings, DateTimeOffset now)
    {
        if (settings.LastBackupAt is null)
            return settings.LastBackupAttemptAt is null || now - settings.LastBackupAttemptAt.Value >= RetryAfterFailure;
        if (now - settings.LastBackupAt.Value < TimeSpan.FromDays(settings.BackupIntervalDays))
            return false;
        return settings.LastBackupError is null
            || settings.LastBackupAttemptAt is null
            || now - settings.LastBackupAttemptAt.Value >= RetryAfterFailure;
    }

    private async Task<string> WriteBackupAsync(string directory, CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new BusinessException("The database connection is not configured.");
        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(database) || database.Length > 128 || database.Any(c => c < 32))
            throw new BusinessException("The database name in the connection string cannot be backed up.");

        var file = Path.Combine(directory, BackupFileName(database, DateTime.UtcNow));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 900;
        command.CommandText = $"BACKUP DATABASE {Bracket(database)} TO DISK = @path WITH COPY_ONLY, CHECKSUM";
        command.Parameters.Add(new SqlParameter("@path", System.Data.SqlDbType.NVarChar, 500) { Value = file });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return file;
    }

    private void TryDeleteExpired(AdministrationSettings settings, string directory, DateTimeOffset now)
    {
        try { DeleteExpired(directory, DatabaseName(), settings.BackupRetentionDays, now); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Expired database backups could not be deleted from {Directory}", directory);
        }
    }

    private string DatabaseName()
    {
        var connectionString = db.Database.GetConnectionString() ?? "";
        return new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    private async Task SaveStatusAsync(DateTimeOffset attempt, bool succeeded, string? error, CancellationToken cancellationToken)
    {
        for (var tryNumber = 0; tryNumber < 2; tryNumber++)
        {
            var settings = await db.AdministrationSettings.SingleAsync(x => x.Id == 1, cancellationToken);
            settings.LastBackupAttemptAt = attempt;
            settings.LastBackupError = error;
            if (succeeded) settings.LastBackupAt = attempt;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (tryNumber == 0)
            {
                db.Entry(settings).State = EntityState.Detached;
            }
        }
        throw new BusinessException("Backup status could not be saved. Reload and try again.", 409);
    }

    private BackupSettingsInput Describe(AdministrationSettings settings)
    {
        var directory = string.IsNullOrWhiteSpace(settings.BackupDirectory) ? DefaultDirectory : settings.BackupDirectory;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(policy.Options.TimeZone);
        return new BackupSettingsInput
        {
            BackupEnabled = settings.BackupEnabled,
            BackupIntervalDays = settings.BackupIntervalDays,
            BackupRetentionDays = settings.BackupRetentionDays,
            BackupDirectory = directory,
            Version = Convert.ToBase64String(settings.Version),
            LastBackupError = settings.LastBackupError,
            LastBackupText = settings.LastBackupAt is null ? "No backup has been written yet." : "Last backup: " + Format(settings.LastBackupAt.Value, zone) + ".",
            NextBackupText = NextText(settings, zone),
            FilesText = FilesText(directory, DatabaseName())
        };
    }

    private static string NextText(AdministrationSettings settings, TimeZoneInfo zone)
    {
        if (!settings.BackupEnabled)
            return "Automatic backups are off. Use Back up now to write one immediately.";
        if (settings.LastBackupAt is null)
            return "The first automatic backup runs within about 15 minutes of saving, then every " + settings.BackupIntervalDays + " days.";
        var next = settings.LastBackupAt.Value.AddDays(settings.BackupIntervalDays);
        return "Next automatic backup: " + Format(next, zone) + ".";
    }

    private string FilesText(string directory, string database)
    {
        try
        {
            if (!Directory.Exists(directory))
                return "This folder does not exist yet. It is created when the first backup runs.";
            var files = Directory.EnumerateFiles(directory, "*.bak").Where(file => IsOwnBackup(Path.GetFileName(file), FileStem(database))).ToList();
            if (files.Count == 0)
                return "No backup files are in this folder yet.";
            return files.Count == 1 ? "1 backup file is in this folder." : files.Count + " backup files are in this folder.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Backup folder {Directory} could not be read", directory);
            return "The backup folder could not be read. Check that this application can open it.";
        }
    }

    private string ResolveDirectory(string? configured, bool required)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? DefaultDirectory : configured.Trim();
        if (path.Length > 400)
            throw new BusinessException("The backup folder path is too long.");
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new BusinessException("Enter a full folder path, for example " + DefaultDirectory + ".");
        }
        if (!Path.IsPathRooted(path) || full.Length > 400)
            throw new BusinessException("Enter a full folder path, for example " + DefaultDirectory + ".");
        if (IsInsideApplication(full))
            throw new BusinessException("Choose a folder outside the application directory.");
        if (!required && string.IsNullOrWhiteSpace(configured))
            return "";
        return full;
    }

    private bool IsInsideApplication(string full)
    {
        var roots = new[] { environment.ContentRootPath, environment.WebRootPath }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath);
        return roots.Any(root => full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private int DeleteExpired(string directory, string database, int retentionDays, DateTimeOffset now)
    {
        if (!Directory.Exists(directory) || string.IsNullOrWhiteSpace(database)) return 0;
        var cutoff = now.UtcDateTime.AddDays(-retentionDays);
        var stem = FileStem(database);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.bak"))
        {
            if (!IsOwnBackup(Path.GetFileName(file), stem)) continue;
            if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
            File.Delete(file);
            removed++;
        }
        if (removed > 0)
            logger.LogInformation("Removed {ExpiredCount} database backups older than {RetentionDays} days from {Directory}", removed, retentionDays, directory);
        return removed;
    }

    internal static string BackupFileName(string database, DateTime utc) =>
        FileStem(database) + "-" + utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";

    internal static string FileStem(string database)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(database.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "database";
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    internal static bool IsOwnBackup(string fileName, string stem)
    {
        var prefix = stem + "-";
        if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return false;
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var stamp = fileName[prefix.Length..^4];
        return stamp.Length == 15 && stamp[8] == '-' && stamp.All(c => c == '-' || char.IsAsciiDigit(c));
    }

    private static string Bracket(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    private static string CleanError(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 1000 ? message : message[..1000];
    }

    private static string Format(DateTimeOffset value, TimeZoneInfo zone)
    {
        var label = zone.Id == "Australia/Adelaide" ? "Adelaide" : zone.Id;
        return TimeZoneInfo.ConvertTime(value, zone).ToString("d MMM yyyy HH:mm", CultureInfo.GetCultureInfo("en-AU")) + " " + label;
    }
}
