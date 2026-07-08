using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElBruno.CopilotHarness.Evaluation.Worker;

/// <summary>
/// Periodic background job that refreshes rule confidence scores and triggers
/// recommendation analysis. Runs on a configurable cadence (default 1 hour).
/// </summary>
internal sealed class RecommendationScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<RecommendationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RecommendationScheduler started. Running every {Interval}.", Interval);

        // Initial delay so the app has time to fully start before first run.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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
            var confidenceStore = scope.ServiceProvider.GetRequiredService<IRuleConfidenceStore>();

            var scores = await confidenceStore.GetCurrentScoresAsync(ct);
            var lowConfidence = scores.Where(s => s.ConfidenceScore < 0.8).ToList();

            if (lowConfidence.Count > 0)
            {
                logger.LogWarning(
                    "RecommendationScheduler: {Count} rule(s) with confidence below 80%: {Rules}",
                    lowConfidence.Count,
                    string.Join(", ", lowConfidence.Select(s => s.RuleKey)));
            }
            else
            {
                logger.LogInformation("RecommendationScheduler: all {Count} rule(s) above confidence threshold.", scores.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during recommendation cycle.");
        }
    }
}
