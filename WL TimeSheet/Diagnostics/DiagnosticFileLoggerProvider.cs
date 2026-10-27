using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WL_TimeSheet.Diagnostics;

public sealed class DiagnosticFileOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxFileSizeMB { get; set; } = 10;
    public int RetentionDays { get; set; } = 30;
}

/// <summary>Bridges ASP.NET ILogger events to a private System.Diagnostics trace listener.</summary>
[ProviderAlias("DiagnosticFile")]
public sealed class DiagnosticFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object gate = new();
    private readonly TraceSource source = new("WL.TimeSheet", SourceLevels.All);
    private readonly string directory;
    private readonly DiagnosticFileOptions options;
    private readonly TimeProvider clock;
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
    private FileStream? stream;
    private DateOnly fileDate;
    private int sequence;
    private bool disposed;
    private DateTimeOffset lastFailure = DateTimeOffset.MinValue;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DiagnosticFileLoggerProvider(string directory, DiagnosticFileOptions options, TimeProvider? clock = null)
    {
        if (options.MaxFileSizeMB is < 1 or > 100 || options.RetentionDays is < 1 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Diagnostic file size must be 1–100 MB and retention 1–3650 days.");
        }

        this.directory = Path.GetFullPath(directory);
        this.options = options;
        this.clock = clock ?? TimeProvider.System;
        source.Listeners.Clear();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    private void Write<TState>(string category, LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!options.Enabled || level == LogLevel.None)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            try
            {
                var now = clock.GetUtcNow();
                var scopeValues = new List<object?>();
                scopes.ForEachScope((scope, list) => list.Add(Fields(scope)), scopeValues);
                var activity = Activity.Current;
                var record = new
                {
                    TimestampUtc = now,
                    Level = level.ToString(),
                    Category = category,
                    EventId = eventId.Id,
                    EventName = eventId.Name,
                    ProcessId = Environment.ProcessId,
                    ThreadId = Environment.CurrentManagedThreadId,
                    TraceId = activity?.TraceId.ToString(),
                    SpanId = activity?.SpanId.ToString(),
                    Message = formatter(state, exception),
                    Properties = Fields(state),
                    Scopes = scopeValues,
                    Exception = ExceptionDetails(exception)
                };
                var text = JsonSerializer.Serialize(record, JsonOptions);
                OpenFile(now, Encoding.UTF8.GetByteCount(text) + 2);
                var type = level switch
                {
                    LogLevel.Critical => TraceEventType.Critical,
                    LogLevel.Error => TraceEventType.Error,
                    LogLevel.Warning => TraceEventType.Warning,
                    LogLevel.Information => TraceEventType.Information,
                    _ => TraceEventType.Verbose
                };
                source.TraceEvent(type, eventId.Id, text);
                source.Flush(); // Complete entries are immediately available, including warnings and failures.
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
            {
                CloseFile();
                if (clock.GetUtcNow() - lastFailure > TimeSpan.FromMinutes(1))
                {
                    lastFailure = clock.GetUtcNow();
                    Console.Error.WriteLine($"Diagnostic file logging failed in '{directory}': {failure.Message}");
                }
            }
        }
    }

    private void OpenFile(DateTimeOffset now, int bytes)
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (stream is not null && date == fileDate && stream.Length + bytes <= (long)options.MaxFileSizeMB * 1024 * 1024)
        {
            return;
        }

        CloseFile();
        Directory.CreateDirectory(directory);
        if (fileDate != date)
        {
            sequence = 0;
            fileDate = date;
            foreach (var path in Directory.EnumerateFiles(directory, "WL-TimeSheet-*.log", SearchOption.TopDirectoryOnly))
            {
                var match = Regex.Match(Path.GetFileName(path), @"^WL-TimeSheet-(\d{8})-p\d+-\d+\.log$");
                if (match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var logDate)
                    && logDate < date.AddDays(-options.RetentionDays))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Another worker may still own this file. */ }
                }
            }
        }
        string file;
        do
        {
            file = Path.Combine(directory, $"WL-TimeSheet-{date:yyyyMMdd}-p{Environment.ProcessId}-{sequence++:000}.log");
        } while (File.Exists(file));
        stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        source.Listeners.Add(new IndentedListener(new StreamWriter(stream, new UTF8Encoding(false))));
    }

    private static object? Fields(object? state)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> values)
        {
            return state?.ToString();
        }

        var result = new Dictionary<string, object?>();
        foreach (var item in values)
        {
            var key = item.Key == "{OriginalFormat}" ? "MessageTemplate" : item.Key;
            result[key] = Regex.IsMatch(key, "password|secret|token|authorization|cookie|connectionstring", RegexOptions.IgnoreCase)
                ? "[REDACTED]" : item.Value switch
                {
                    null => null,
                    string or bool or byte or short or int or long or float or double or decimal or Guid or DateOnly or DateTime or DateTimeOffset => item.Value,
                    _ => item.Value.ToString()
                };
        }
        return result;
    }

    private static object? ExceptionDetails(Exception? error, int depth = 0) => error is null ? null : new
    {
        Type = error.GetType().FullName,
        error.Message,
        error.HResult,
        StackTrace = error.StackTrace?.Split('\n').Select(x => x.Trim()).ToArray(),
        InnerExceptions = depth >= 10 ? [] : (error is AggregateException aggregate ? aggregate.InnerExceptions.AsEnumerable()
            : error.InnerException is null ? [] : new[] { error.InnerException }).Select(x => ExceptionDetails(x, depth + 1)).ToArray()
    };

    private void CloseFile()
    {
        foreach (TraceListener listener in source.Listeners)
        {
            try
            {
                listener.Dispose();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        source.Listeners.Clear();
        try
        {
            stream?.Dispose();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        stream = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CloseFile();
            source.Close();
        }
    }

    private sealed class FileLogger(DiagnosticFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => owner.options.Enabled && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Write(category, logLevel, eventId, state, exception, formatter);
            }
        }
    }

    private sealed class IndentedListener(TextWriter writer) : TextWriterTraceListener(writer)
    {
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            WriteLine(message);
            WriteLine("");
        }
    }
}
