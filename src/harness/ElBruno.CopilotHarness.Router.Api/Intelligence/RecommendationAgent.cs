using System.Text.Json;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api.Intelligence;

/// <summary>
/// Analyses recent rule confidence scores and shadow results to produce
/// recommended rule changes. Recommendations are written to the
/// <see cref="IApprovalWorkflowStore"/> as pending approval requests.
/// </summary>
public interface IRecommendationAgent
{
    Task<int> GenerateRecommendationsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Deterministic recommendation agent that uses rule confidence scores
/// to suggest routing rule changes. Fires when a rule's confidence drops
/// below a configurable threshold.
/// </summary>
public sealed class DeterministicRecommendationAgent(
    IRuleConfidenceStore confidenceStore,
    IApprovalWorkflowStore approvalStore,
    ILogger<DeterministicRecommendationAgent> logger) : IRecommendationAgent
{
    private const double LowConfidenceThreshold = 0.55;
    private readonly IRuleConfidenceStore _confidenceStore = confidenceStore;
    private readonly IApprovalWorkflowStore _approvalStore = approvalStore;
    private readonly ILogger<DeterministicRecommendationAgent> _logger = logger;

    public async Task<int> GenerateRecommendationsAsync(CancellationToken cancellationToken)
    {
        var scores = await _confidenceStore.GetCurrentScoresAsync(cancellationToken);
        var generated = 0;

        foreach (var score in scores)
        {
            if (score.TotalInvocations < 10) continue;
            if (score.ConfidenceScore >= LowConfidenceThreshold) continue;

            // Check if there's already a pending recommendation for this rule
            var existing = await _approvalStore.ListAsync("pending", 0, 100, cancellationToken);
            var alreadyPending = existing.Any(a =>
                a.ChangeType == "rule-recommendation" &&
                a.Title.Contains(score.RuleKey, StringComparison.OrdinalIgnoreCase));

            if (alreadyPending) continue;

            var trend = score.ConfidenceScore < 0.3 ? "critically low" : "below threshold";
            var payload = JsonSerializer.Serialize(new
            {
                ruleKey = score.RuleKey,
                currentValue = score.RuleKey,
                recommendedValue = "review-needed",
                rationale = $"Rule '{score.RuleKey}' has {trend} confidence ({score.ConfidenceScore:P0}) " +
                            $"over {score.TotalInvocations} invocations in window '{score.WindowLabel}'. " +
                            "Consider adjusting routing thresholds or model assignments.",
                confidence = score.ConfidenceScore
            });

            await _approvalStore.CreateAsync(new CreateApprovalRequest(
                ChangeType: "rule-recommendation",
                Title: $"Low confidence on rule: {score.RuleKey}",
                Description: $"Rule '{score.RuleKey}' confidence is {score.ConfidenceScore:P0} — below the {LowConfidenceThreshold:P0} threshold.",
                PayloadJson: payload,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(14)),
                cancellationToken);

            generated++;

            _logger.LogInformation(
                "Recommendation generated for rule '{RuleKey}' with confidence {Confidence:P0}.",
                score.RuleKey,
                score.ConfidenceScore);
        }

        return generated;
    }
}
