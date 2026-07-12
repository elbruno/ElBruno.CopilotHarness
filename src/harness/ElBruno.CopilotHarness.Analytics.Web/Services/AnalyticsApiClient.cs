using System.Net.Http.Json;

namespace ElBruno.CopilotHarness.Analytics.Web.Services;

public sealed class UsageTelemetryApiClient(HttpClient httpClient) : IUsageTelemetryApiClient
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<UsageTelemetrySummaryResponse> GetSummaryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<UsageTelemetrySummaryResponse>(
            $"/admin/telemetry/usage/summary?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}",
            cancellationToken);

        return response ?? throw new InvalidOperationException("Usage summary response was empty.");
    }

    public async Task<IReadOnlyList<UsagePricingCardResponse>> GetPricingCardsAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<UsagePricingCardResponse>>("/admin/telemetry/usage/pricing/cards", cancellationToken)
        ?? [];
}
