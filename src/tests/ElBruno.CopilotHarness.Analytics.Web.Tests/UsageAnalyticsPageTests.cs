using ElBruno.CopilotHarness.Analytics.Web.Services;
using Xunit;

namespace ElBruno.CopilotHarness.Analytics.Web.Tests;

public sealed class UsageAnalyticsPageTests
{
    [Fact]
    public async Task RootPage_RendersSummaryAndCostLabels()
    {
        using var factory = new AnalyticsWebApplicationFactory(new StubAnalyticsClient(
            summary: new UsageTelemetrySummaryResponse(
                FromUtc: new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                ToUtc: new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero),
                EventCount: 3,
                InputTokens: 1000,
                OutputTokens: 500,
                TotalTokens: 1500,
                EstimatedUsdTotal: 0.0025,
                HasEstimateGaps: false,
                EstimateDisclaimer: "Estimated USD is for planning only and is not billed cost. Unknown rates remain token-only.",
                Rows:
                [
                    new UsageTelemetrySummaryRowResponse("FoundryProxy", "azure_openai", "gpt-5-mini", 2, 700, 300, 1000, true, 0.0025, null),
                    new UsageTelemetrySummaryRowResponse("OllamaProxy", "ollama", "llama3.1", 1, 300, 200, 500, false, null, "provider-not-supported")
                ]),
            pricingCards:
            [
                new UsagePricingCardResponse(1, "azure_openai", "gpt-5-mini", "chat", 1, 2, DateTimeOffset.UtcNow.AddDays(-1), null, "azure-retail-prices-api", "prices", null, false, "tests", DateTimeOffset.UtcNow)
            ]));

        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        Assert.Contains("Usage Analytics", html);
        Assert.Contains("1,500", html);
        Assert.Contains("Estimated USD: $0.0025", html);
        Assert.Contains("Token-only: Ollama", html);
        Assert.Contains("Pricing catalog snapshot", html);
    }

    [Fact]
    public async Task RootPage_ShowsEmptyState_WhenNoRowsReturn()
    {
        using var factory = new AnalyticsWebApplicationFactory(new StubAnalyticsClient(
            summary: new UsageTelemetrySummaryResponse(
                FromUtc: DateTimeOffset.UtcNow.AddHours(-1),
                ToUtc: DateTimeOffset.UtcNow,
                EventCount: 0,
                InputTokens: 0,
                OutputTokens: 0,
                TotalTokens: 0,
                EstimatedUsdTotal: 0,
                HasEstimateGaps: false,
                EstimateDisclaimer: "Estimated USD is for planning only and is not billed cost. Unknown rates remain token-only.",
                Rows: []),
            pricingCards: []));

        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        Assert.Contains("No usage telemetry yet", html);
        Assert.Contains("No pricing cards are configured", html);
    }

    [Fact]
    public async Task RootPage_ShowsErrorState_WhenSummaryFails()
    {
        using var factory = new AnalyticsWebApplicationFactory(new StubAnalyticsClient(
            summaryFactory: (_, _, _) => throw new HttpRequestException("summary unavailable"),
            pricingCards: []));

        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        Assert.Contains("Usage summary unavailable", html);
        Assert.Contains("summary unavailable", html);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var factory = new AnalyticsWebApplicationFactory(new StubAnalyticsClient(
            summary: new UsageTelemetrySummaryResponse(
                FromUtc: DateTimeOffset.UtcNow.AddHours(-1),
                ToUtc: DateTimeOffset.UtcNow,
                EventCount: 0,
                InputTokens: 0,
                OutputTokens: 0,
                TotalTokens: 0,
                EstimatedUsdTotal: 0,
                HasEstimateGaps: false,
                EstimateDisclaimer: "Estimated USD is for planning only and is not billed cost. Unknown rates remain token-only.",
                Rows: []),
            pricingCards: []));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/analytics");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"service\":\"analytics-web\"", payload);
        Assert.Contains("\"status\":\"ok\"", payload);
    }

    private sealed class StubAnalyticsClient : IUsageTelemetryApiClient
    {
        private readonly Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<UsageTelemetrySummaryResponse>> _summaryFactory;
        private readonly Func<CancellationToken, Task<IReadOnlyList<UsagePricingCardResponse>>> _pricingFactory;

        public StubAnalyticsClient(
            UsageTelemetrySummaryResponse? summary = null,
            IReadOnlyList<UsagePricingCardResponse>? pricingCards = null,
            Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<UsageTelemetrySummaryResponse>>? summaryFactory = null,
            Func<CancellationToken, Task<IReadOnlyList<UsagePricingCardResponse>>>? pricingFactory = null)
        {
            _summaryFactory = summaryFactory ?? ((_, _, _) => Task.FromResult(summary ?? throw new InvalidOperationException("Summary response missing.")));
            _pricingFactory = pricingFactory ?? (_ => Task.FromResult(pricingCards ?? []));
        }

        public Task<UsageTelemetrySummaryResponse> GetSummaryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            _summaryFactory(fromUtc, toUtc, cancellationToken);

        public Task<IReadOnlyList<UsagePricingCardResponse>> GetPricingCardsAsync(CancellationToken cancellationToken = default) =>
            _pricingFactory(cancellationToken);
    }
}
