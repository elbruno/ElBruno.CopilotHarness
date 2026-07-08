using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Judge.Web;

public sealed class ContinuousBenchmarkOptions
{
    public const string SectionName = "ContinuousBenchmark";

    /// <summary>When false the scheduler does nothing.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Minutes between automatic benchmark cycles.</summary>
    public int IntervalMinutes { get; init; } = 60;

    /// <summary>Maximum number of recent prompt records included per cycle.</summary>
    public int BatchSize { get; init; } = 5;

    /// <summary>Name prefix for auto-generated benchmark runs.</summary>
    public string SuiteName { get; init; } = "continuous-eval";
}

/// <summary>
/// Hosted service that periodically runs a benchmark batch and then invokes
/// the <see cref="IRecommendationAgent"/> to refresh routing suggestions.
/// </summary>
public sealed class ContinuousBenchmarkScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ContinuousBenchmarkOptions> options,
    ILogger<ContinuousBenchmarkScheduler> logger) : BackgroundService
{
    private DateTimeOffset? _lastRunAtUtc;
    private string? _lastRunStatus;

    public DateTimeOffset? LastRunAtUtc => _lastRunAtUtc;
    public string? LastRunStatus => _lastRunStatus;
    public ContinuousBenchmarkOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Continuous benchmark scheduler is disabled (ContinuousBenchmark:Enabled=false).");
            return;
        }

        logger.LogInformation(
            "Continuous benchmark scheduler started — interval={IntervalMinutes} min, batch={BatchSize}.",
            options.Value.IntervalMinutes, options.Value.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(options.Value.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastRunStatus = $"Failed: {ex.Message}";
                logger.LogError(ex, "Continuous benchmark cycle failed.");
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting continuous benchmark cycle.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<JudgeDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<IBenchmarkRunner>();
        var agent = scope.ServiceProvider.GetRequiredService<IRecommendationAgent>();

        var recentPrompts = (await dbContext.PromptRecords
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .OrderByDescending(p => p.ImportedAtUtc)
            .Take(options.Value.BatchSize)
            .ToList();

        if (recentPrompts.Count == 0)
        {
            logger.LogInformation("No prompt records available — skipping continuous benchmark cycle.");
            _lastRunStatus = "Skipped: no prompt records";
            _lastRunAtUtc = DateTimeOffset.UtcNow;
            return;
        }

        // Re-use the profile combinations seen in recent successful runs
        var profiles = await dbContext.BenchmarkResults
            .AsNoTracking()
            .Select(r => new { r.ProfileName, r.Deployment })
            .Distinct()
            .Take(3)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            logger.LogInformation("No profile history found — skipping continuous benchmark cycle.");
            _lastRunStatus = "Skipped: no profile history";
            _lastRunAtUtc = DateTimeOffset.UtcNow;
            return;
        }

        var models = profiles
            .Select(p => new BenchmarkModelRequest(p.ProfileName, p.Deployment, null))
            .ToList();

        var request = new ReplayPromptRecordsRequest(
            $"{options.Value.SuiteName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmm}",
            recentPrompts.Select(p => p.Id).ToList(),
            models);

        var run = await runner.ReplayAsync(request, cancellationToken);

        await agent.AnalyzeAsync(cancellationToken);

        _lastRunAtUtc = DateTimeOffset.UtcNow;
        _lastRunStatus = run.Status;

        logger.LogInformation(
            "Continuous benchmark cycle completed — runId={RunId}, status={Status}, prompts={Count}.",
            run.Id, run.Status, recentPrompts.Count);
    }
}
