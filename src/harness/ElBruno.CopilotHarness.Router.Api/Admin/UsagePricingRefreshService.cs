using System.Text.Json;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public interface IUsagePricingRefreshService
{
    Task<UsagePricingRefreshResult> RefreshFromAzureRetailAsync(
        string updatedBy,
        string sourceReference,
        CancellationToken cancellationToken);
}

public sealed record UsagePricingRefreshResult(
    int ChangedRows,
    int ParsedRows,
    int SkippedRows);

public sealed class UsagePricingRefreshService(
    IHttpClientFactory httpClientFactory,
    IUsagePricingCatalogStore pricingCatalogStore) : IUsagePricingRefreshService
{
    private const string AzureRetailFilter =
        "serviceName eq 'Azure OpenAI Service' and priceType eq 'Consumption'";

    public async Task<UsagePricingRefreshResult> RefreshFromAzureRetailAsync(
        string updatedBy,
        string sourceReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(updatedBy))
        {
            throw new ArgumentException("updatedBy is required.", nameof(updatedBy));
        }

        var client = httpClientFactory.CreateClient("azure-retail-prices");
        var requestUri = $"/api/retail/prices?$filter={Uri.EscapeDataString(AzureRetailFilter)}";
        var parsedRows = 0;
        var skippedRows = 0;
        var aggregated = new Dictionary<(string Model, DateTimeOffset EffectiveFromUtc), (double? Input, double? Output, string RawJson)>();

        for (var i = 0; i < 5 && !string.IsNullOrWhiteSpace(requestUri); i++)
        {
            using var response = await client.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (payload.RootElement.TryGetProperty("Items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!TryMapAzureRetailItem(item, out var mapped))
                    {
                        skippedRows++;
                        continue;
                    }

                    var key = (mapped.Model, mapped.EffectiveFromUtc);
                    aggregated.TryGetValue(key, out var existing);
                    aggregated[key] = mapped.Direction == "input"
                        ? (mapped.PricePer1MToken, existing.Output, mapped.RawJson)
                        : (existing.Input, mapped.PricePer1MToken, mapped.RawJson);
                    parsedRows++;
                }
            }

            requestUri = payload.RootElement.TryGetProperty("NextPageLink", out var nextPageLink)
                ? nextPageLink.GetString()
                : null;
        }

        var upserts = aggregated
            .Where(item => item.Value.Input.HasValue || item.Value.Output.HasValue)
            .Select(item => new UsagePricingCardUpsert(
                Provider: "azure_openai",
                Model: item.Key.Model,
                Operation: "chat",
                InputUsdPer1MToken: item.Value.Input ?? 0,
                OutputUsdPer1MToken: item.Value.Output ?? 0,
                EffectiveFromUtc: item.Key.EffectiveFromUtc,
                EffectiveToUtc: null,
                SourceType: "azure-retail-prices-api",
                SourceReference: sourceReference,
                SourceMetadataJson: item.Value.RawJson,
                IsOverride: false,
                UpdatedBy: updatedBy,
                UpdatedAtUtc: DateTimeOffset.UtcNow))
            .ToList();

        var changedRows = await pricingCatalogStore.UpsertCardsAsync(upserts, cancellationToken);
        return new UsagePricingRefreshResult(changedRows, parsedRows, skippedRows);
    }

    private static bool TryMapAzureRetailItem(JsonElement item, out AzureRetailPriceMappedItem mapped)
    {
        mapped = default!;

        if (!item.TryGetProperty("meterName", out var meterNameElement) ||
            !item.TryGetProperty("unitPrice", out var unitPriceElement) ||
            !item.TryGetProperty("effectiveStartDate", out var effectiveStartElement))
        {
            return false;
        }

        var meterName = meterNameElement.GetString() ?? string.Empty;
        var direction = GetDirection(meterName);
        if (direction is null)
        {
            return false;
        }

        var model = item.TryGetProperty("armSkuName", out var skuElement)
            ? skuElement.GetString()
            : null;
        model ??= NormalizeModel(meterName);
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        if (!unitPriceElement.TryGetDouble(out var unitPrice))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(effectiveStartElement.GetString(), out var effectiveFrom))
        {
            return false;
        }

        mapped = new AzureRetailPriceMappedItem(
            model.Trim().ToLowerInvariant(),
            direction,
            unitPrice,
            effectiveFrom,
            item.GetRawText());
        return true;
    }

    private static string? GetDirection(string meterName)
    {
        if (meterName.Contains("input", StringComparison.OrdinalIgnoreCase))
        {
            return "input";
        }

        if (meterName.Contains("output", StringComparison.OrdinalIgnoreCase))
        {
            return "output";
        }

        return null;
    }

    private static string NormalizeModel(string meterName)
    {
        var normalized = meterName.Replace("Input", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Output", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Tokens", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Token", "", StringComparison.OrdinalIgnoreCase)
            .Replace("1M", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return normalized.ToLowerInvariant();
    }

    private sealed record AzureRetailPriceMappedItem(
        string Model,
        string Direction,
        double PricePer1MToken,
        DateTimeOffset EffectiveFromUtc,
        string RawJson);
}
