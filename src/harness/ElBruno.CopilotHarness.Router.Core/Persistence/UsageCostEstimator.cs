namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class UsageCostEstimator : IUsageCostEstimator
{
    public UsageEstimateSummary Estimate(
        IReadOnlyList<UsageTelemetryEventWindowItem> events,
        IReadOnlyList<UsagePricingCard> cards)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(cards);

        var groupedCosts = new Dictionary<(string Proxy, string Provider, string Model), double>();
        var groupedAvailability = new Dictionary<(string Proxy, string Provider, string Model), (bool Available, string? Reason)>();

        foreach (var usageEvent in events)
        {
            var key = (usageEvent.Proxy, usageEvent.Provider, usageEvent.Model);

            if (!IsEstimateAllowedForProvider(usageEvent.Provider))
            {
                groupedAvailability[key] = (false, "provider-not-supported");
                continue;
            }

            var matchedCard = SelectBestCard(cards, usageEvent.Provider, usageEvent.Model, usageEvent.OccurredAtUtc);
            if (matchedCard is null)
            {
                groupedAvailability[key] = (false, "rate-not-found");
                continue;
            }

            var estimatedUsd =
                (usageEvent.InputTokens / 1_000_000d) * matchedCard.InputUsdPer1MToken +
                (usageEvent.OutputTokens / 1_000_000d) * matchedCard.OutputUsdPer1MToken;

            groupedCosts.TryGetValue(key, out var current);
            groupedCosts[key] = current + estimatedUsd;
            groupedAvailability[key] = (true, null);
        }

        var rows = new Dictionary<(string Proxy, string Provider, string Model), UsageEstimateRow>();
        foreach (var grouped in events.GroupBy(e => (e.Proxy, e.Provider, e.Model)))
        {
            groupedAvailability.TryGetValue(grouped.Key, out var availability);
            var available = availability.Available;
            groupedCosts.TryGetValue(grouped.Key, out var estimatedUsd);
            rows[grouped.Key] = new UsageEstimateRow(
                grouped.Key.Proxy,
                grouped.Key.Provider,
                grouped.Key.Model,
                available,
                available ? Math.Round(estimatedUsd, 8) : null,
                available ? null : availability.Reason ?? "rate-not-found");
        }

        var total = rows.Values
            .Where(r => r.EstimateAvailable && r.EstimatedUsd.HasValue)
            .Sum(r => r.EstimatedUsd!.Value);
        var hasGaps = rows.Values.Any(r => !r.EstimateAvailable);
        return new UsageEstimateSummary(Math.Round(total, 8), hasGaps, rows);
    }

    public static UsagePricingCard? SelectBestCard(
        IReadOnlyList<UsagePricingCard> cards,
        string provider,
        string model,
        DateTimeOffset occurredAtUtc)
    {
        return cards
            .Where(card =>
                Normalize(card.Provider) == Normalize(provider) &&
                Normalize(card.Model) == Normalize(model) &&
                string.Equals(card.Operation, "chat", StringComparison.OrdinalIgnoreCase) &&
                card.EffectiveFromUtc <= occurredAtUtc &&
                (card.EffectiveToUtc is null || occurredAtUtc < card.EffectiveToUtc.Value))
            .OrderByDescending(card => card.IsOverride)
            .ThenByDescending(card => card.EffectiveFromUtc)
            .ThenByDescending(card => card.UpdatedAtUtc)
            .FirstOrDefault();
    }

    public static bool IsEstimateAllowedForProvider(string provider)
    {
        var normalized = Normalize(provider);
        return normalized is "azure_openai" or "azure-openai" or "foundry" or "foundry-cloud";
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
