namespace CropQc.Web.Services;

/// <summary>Retries durable HarvestWatch mail work without affecting request success.</summary>
public sealed class HarvestWatchOutboundEmailHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<HarvestWatchOutboundEmailHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IHarvestWatchService>()
                    .ProcessPendingOutboundEmailsAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "HarvestWatch outbound email retry failed; pending delivery work is retained."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
