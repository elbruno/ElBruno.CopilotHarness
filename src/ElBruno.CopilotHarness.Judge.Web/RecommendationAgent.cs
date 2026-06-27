using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Judge.Web;

public interface IRecommendationAgent
{
    /// <summary>
    /// Analyses completed benchmark runs and generates routing recommendations.
    /// Only creates new recommendations — existing pending ones are not duplicated.
    /// </summary>
    Task<IReadOnlyList<RecommendationEntity>> AnalyzeAsync(CancellationToken cancellationToken);
}

public sealed class RecommendationAgent(
    JudgeDbContext dbContext,
    ILogger<RecommendationAgent> logger) : IRecommendationAgent
{
    private const int MaxRecentRuns = 20;
    private const double MinScoreDifferenceToRecommend = 0.10;

    public async Task<IReadOnlyList<RecommendationEntity>> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var runs = (await dbContext.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Results)
            .Where(r => r.Status == "Completed")
            .ToListAsync(cancellationToken))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(MaxRecentRuns)
            .ToList();

        if (runs.Count == 0)
        {
            logger.LogInformation("No completed benchmark runs — nothing to analyse.");
            return [];
        }

        var allResults = runs.SelectMany(r => r.Results).ToList();

        var profileStats = allResults
            .GroupBy(r => r.ProfileName)
            .Select(g => new
            {
                ProfileName = g.Key,
                Attempts = g.Count(),
                Wins = g.Count(r => r.IsWinner),
                AverageScore = g.Average(r => r.OverallScore),
                AverageLatencyMs = g.Average(r => r.LatencyMs ?? 0)
            })
            .OrderByDescending(s => s.AverageScore)
            .ToList();

        if (profileStats.Count < 2)
        {
            logger.LogInformation("Only {Count} profile(s) in benchmark history — need at least 2 to generate a recommendation.", profileStats.Count);
            return [];
        }

        var best = profileStats[0];
        var second = profileStats[1];
        var scoreDiff = best.AverageScore - second.AverageScore;

        if (scoreDiff <= MinScoreDifferenceToRecommend)
        {
            logger.LogInformation(
                "Score diff {Diff:F4} between '{Best}' and '{Second}' is below threshold {Threshold} — no recommendation.",
                scoreDiff, best.ProfileName, second.ProfileName, MinScoreDifferenceToRecommend);
            return [];
        }

        // Avoid duplicating pending recommendations for the same suggested profile
        var alreadyPending = await dbContext.Recommendations
            .AnyAsync(r => r.SuggestedProfileName == best.ProfileName && r.Status == "Pending", cancellationToken);

        if (alreadyPending)
        {
            logger.LogInformation("A pending recommendation for profile '{Profile}' already exists — skipping.", best.ProfileName);
            return [];
        }

        var winRate = best.Attempts > 0 ? (double)best.Wins / best.Attempts : 0;

        // Confidence: blend of win rate and score margin
        var confidence = Math.Round(
            Math.Min(0.99, winRate * 0.6 + Math.Min(1.0, scoreDiff / 10.0) * 0.4),
            4);

        var recommendation = new RecommendationEntity
        {
            Type = "routing-rule-change",
            Summary = $"Switch default routing to '{best.ProfileName}'",
            Rationale =
                $"Profile '{best.ProfileName}' achieved a {winRate:P0} win rate with an average overall score of " +
                $"{best.AverageScore:F2} across {best.Attempts} benchmark attempt(s), outperforming " +
                $"'{second.ProfileName}' by {scoreDiff:F2} point(s). " +
                $"Average latency: {best.AverageLatencyMs:F0} ms vs {second.AverageLatencyMs:F0} ms.",
            Confidence = confidence,
            SuggestedAction = $"Update the default routing profile from '{second.ProfileName}' to '{best.ProfileName}'.",
            SuggestedProfileName = best.ProfileName,
            Status = "Pending"
        };

        dbContext.Recommendations.Add(recommendation);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Generated recommendation to switch default routing to '{Profile}' (confidence={Confidence:F2}).",
            best.ProfileName, confidence);

        return [recommendation];
    }
}
