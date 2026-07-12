using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class UsagePricingCatalogStore(HarnessDbContext dbContext) : IUsagePricingCatalogStore
{
    public async Task<IReadOnlyList<UsagePricingCard>> GetCardsAsync(
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        var query = dbContext.UsagePricingCards.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(card => card.Provider == provider);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            query = query.Where(card => card.Model == model);
        }

        var cards = await query
            .Select(card => new UsagePricingCard(
                card.Id,
                card.Provider,
                card.Model,
                card.Operation,
                card.InputUsdPer1MToken,
                card.OutputUsdPer1MToken,
                card.EffectiveFromUtc,
                card.EffectiveToUtc,
                card.SourceType,
                card.SourceReference,
                card.SourceMetadataJson,
                card.IsOverride,
                card.UpdatedBy,
                card.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return cards
            .OrderByDescending(card => card.EffectiveFromUtc)
            .ThenByDescending(card => card.IsOverride)
            .ThenByDescending(card => card.UpdatedAtUtc)
            .ToList();
    }

    public async Task<int> UpsertCardsAsync(IEnumerable<UsagePricingCardUpsert> cards, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var normalizedCards = cards.ToList();
        var changed = 0;
        foreach (var card in normalizedCards)
        {
            var existing = await dbContext.UsagePricingCards
                .FirstOrDefaultAsync(entity =>
                        entity.Provider == card.Provider &&
                        entity.Model == card.Model &&
                        entity.Operation == card.Operation &&
                        entity.EffectiveFromUtc == card.EffectiveFromUtc &&
                        entity.IsOverride == card.IsOverride,
                    cancellationToken);

            if (existing is null)
            {
                dbContext.UsagePricingCards.Add(new UsagePricingCardEntity
                {
                    Provider = card.Provider,
                    Model = card.Model,
                    Operation = card.Operation,
                    InputUsdPer1MToken = card.InputUsdPer1MToken,
                    OutputUsdPer1MToken = card.OutputUsdPer1MToken,
                    EffectiveFromUtc = card.EffectiveFromUtc,
                    EffectiveToUtc = card.EffectiveToUtc,
                    SourceType = card.SourceType,
                    SourceReference = card.SourceReference,
                    SourceMetadataJson = card.SourceMetadataJson,
                    IsOverride = card.IsOverride,
                    UpdatedBy = card.UpdatedBy,
                    UpdatedAtUtc = card.UpdatedAtUtc
                });
                changed++;
                continue;
            }

            existing.InputUsdPer1MToken = card.InputUsdPer1MToken;
            existing.OutputUsdPer1MToken = card.OutputUsdPer1MToken;
            existing.EffectiveToUtc = card.EffectiveToUtc;
            existing.SourceType = card.SourceType;
            existing.SourceReference = card.SourceReference;
            existing.SourceMetadataJson = card.SourceMetadataJson;
            existing.UpdatedBy = card.UpdatedBy;
            existing.UpdatedAtUtc = card.UpdatedAtUtc;
            changed++;
        }

        if (changed == 0)
        {
            return 0;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
