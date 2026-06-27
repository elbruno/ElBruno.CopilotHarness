using System.Threading.Channels;

namespace ElBruno.CopilotHarness.Router.Api.BackgroundJobs;

public sealed class QueuedBackgroundJobProcessor(
    IBackgroundJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueuedBackgroundJobProcessor> logger)
    : BackgroundService
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var job = await queue.DequeueAsync(cancellationToken);

        using var scope = scopeFactory.CreateScope();
        await job(scope.ServiceProvider, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (ChannelClosedException)
            {
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A queued background job failed.");
            }
        }
    }
}
