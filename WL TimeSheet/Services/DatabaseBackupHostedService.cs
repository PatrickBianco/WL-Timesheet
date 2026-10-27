namespace WL_TimeSheet.Services;

public sealed class DatabaseBackupHostedService(IServiceScopeFactory scopes, ILogger<DatabaseBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                try
                {
                    await scope.ServiceProvider.GetRequiredService<ActivityLogService>().PurgeExpiredAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Activity log retention cleanup failed");
                }

                await scope.ServiceProvider.GetRequiredService<DatabaseBackupService>().RunScheduledAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled database backup check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
