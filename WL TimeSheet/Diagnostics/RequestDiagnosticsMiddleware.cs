using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Models;

namespace WL_TimeSheet.Diagnostics;

public sealed class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = context.TraceIdentifier,
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path.Value
        });
        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            failed = true;
            logger.LogWarning("Client disconnected after {ElapsedMs} ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception exception)
        {
            failed = true;
            var (status, message) = exception switch
            {
                BusinessException e => (e.Status, e.Message),
                DbUpdateConcurrencyException => (409, "This timesheet changed. Reload it before saving again."),
                DbUpdateException => (409, "This change conflicts with another record. Reload and try again."),
                _ => (500, "We could not complete this request. Contact your administrator with the reference below.")
            };
            logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning, exception,
                "Request failed with status {StatusCode} after {ElapsedMs} ms", status, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    message,
                    traceId = context.TraceIdentifier
                });
            }
            else
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(message + " Reference: " + context.TraceIdentifier);
            }
        }
        finally
        {
            if (!failed)
            {
                var status = context.Response.StatusCode;
                // Static assets remain quiet unless they fail. No bodies, query strings or credentials are captured.
                var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning :
                    context.GetEndpoint() is null ? LogLevel.Debug : LogLevel.Information;
                logger.Log(level, "Request completed with status {StatusCode} after {ElapsedMs} ms", status, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }
}
