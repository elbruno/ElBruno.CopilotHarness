using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElBruno.CopilotHarness.Evaluation.Worker;

/// <summary>
/// Periodic background job that polls for benchmark runs in "pending" state and
/// marks them as "running" → "completed" (or "failed"). In a real deployment,
/// this job would dispatch the actual LLM evaluation work.
/// </summary>
internal sealed class ContinuousBenchmarkJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ContinuousBenchmarkJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ContinuousBenchmarkJob started. Polling every {Interval}.", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IBenchmarkStore>();

            var runs = await store.ListRunsAsync(1, 50, ct);
            var pending = runs.Where(r => r.Status == "pending").ToList();

            logger.LogInformation("Benchmark cycle: {PendingCount} pending run(s).", pending.Count);

            foreach (var run in pending)
            {
                try
                {
                    await store.UpdateRunStatusAsync(run.RunId, "running", ct);
                    // TODO: dispatch real evaluation work here
                    await store.UpdateRunStatusAsync(run.RunId, "completed", ct);
                    logger.LogInformation("Benchmark run {RunId} completed.", run.RunId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Benchmark run {RunId} failed.", run.RunId);
                    await store.UpdateRunStatusAsync(run.RunId, "failed", ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during benchmark cycle.");
        }
    }
}
