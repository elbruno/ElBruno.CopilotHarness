using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class UsageTelemetryEndpointsIntegrationTests
{
    [Fact]
    public async Task Ingest_AcceptsValidPayload()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("valid-event"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task Ingest_DuplicateEvent_DoesNotDoubleCount()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var request = CreateRequest("duplicate-event");
        await client.PostAsJsonAsync("/admin/telemetry/usage/events", request);
        await client.PostAsJsonAsync("/admin/telemetry/usage/events", request);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var summaryResponse = await client.GetAsync($"/admin/telemetry/usage/summary?fromUtc={from}&toUtc={to}");
        summaryResponse.EnsureSuccessStatusCode();
        var summary = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, summary.GetProperty("eventCount").GetInt64());
        Assert.Equal(55, summary.GetProperty("totalTokens").GetInt64());
    }

    [Fact]
    public async Task Summary_ReturnsExpectedAggregateForWindow()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("agg-1", "FoundryProxy", "azure_openai", 20, 10, 30));
        await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("agg-2", "FoundryProxy", "azure_openai", 30, 20, 50));
        await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("agg-3", "OllamaProxy", "ollama", 10, 5, 15));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(2).ToString("O"));
        var response = await client.GetAsync($"/admin/telemetry/usage/summary?fromUtc={from}&toUtc={to}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3, payload.GetProperty("eventCount").GetInt64());
        Assert.Equal(95, payload.GetProperty("totalTokens").GetInt64());
        Assert.Equal(2, payload.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Summary_IncludesEstimatedUsd_ForCloudRowsWithRates()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        await UpsertManualRateAsync(client, "azure_openai", "gpt-5-mini", 2.0, 4.0);
        await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("cost-cloud-1", "FoundryProxy", "azure_openai", 1_000_000, 500_000, 1_500_000));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(2).ToString("O"));
        var response = await client.GetAsync($"/admin/telemetry/usage/summary?fromUtc={from}&toUtc={to}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = payload.GetProperty("rows").EnumerateArray().Single(item => item.GetProperty("provider").GetString() == "azure_openai");

        Assert.True(row.GetProperty("estimateAvailable").GetBoolean());
        Assert.Equal(4.0, row.GetProperty("estimatedUsd").GetDouble(), 6);
        Assert.Equal(4.0, payload.GetProperty("estimatedUsdTotal").GetDouble(), 6);
    }

    [Fact]
    public async Task Summary_LocalRows_NeverIncludeUsdEstimate()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("cost-local-1", "OllamaProxy", "ollama", 1000, 500, 1500));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(2).ToString("O"));
        var response = await client.GetAsync($"/admin/telemetry/usage/summary?fromUtc={from}&toUtc={to}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = payload.GetProperty("rows").EnumerateArray().Single(item => item.GetProperty("provider").GetString() == "ollama");

        Assert.False(row.GetProperty("estimateAvailable").GetBoolean());
        Assert.Equal("provider-not-supported", row.GetProperty("estimateUnavailableReason").GetString());
        Assert.True(payload.GetProperty("hasEstimateGaps").GetBoolean());
    }

    [Fact]
    public async Task Summary_UnknownCloudModel_ReturnsEstimateUnavailableMarker()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/admin/telemetry/usage/events", CreateRequest("cost-unknown-1", "FoundryProxy", "azure_openai", 1000, 500, 1500));
        var unknownModelRequest = CreateRequest("cost-unknown-2", "FoundryProxy", "azure_openai", 1000, 500, 1500) with
        {
            RequestModel = "unknown-model",
            ResponseModel = "unknown-model"
        };
        await client.PostAsJsonAsync("/admin/telemetry/usage/events", unknownModelRequest);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(2).ToString("O"));
        var response = await client.GetAsync($"/admin/telemetry/usage/summary?fromUtc={from}&toUtc={to}&model=unknown-model");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = payload.GetProperty("rows").EnumerateArray().Single();

        Assert.False(row.GetProperty("estimateAvailable").GetBoolean());
        Assert.Equal("rate-not-found", row.GetProperty("estimateUnavailableReason").GetString());
        Assert.Equal(0.0, payload.GetProperty("estimatedUsdTotal").GetDouble(), 6);
    }

    private static UsageTelemetryIngestRequest CreateRequest(
        string idempotencyKey,
        string proxy = "FoundryProxy",
        string provider = "azure_openai",
        long inputTokens = 33,
        long outputTokens = 22,
        long totalTokens = 55) =>
        new(
            IdempotencyKey: idempotencyKey,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Proxy: proxy,
            Provider: provider,
            RequestModel: "gpt-5-mini",
            ResponseModel: "gpt-5-mini",
            TraceId: "trace-abc",
            SpanId: "span-abc",
            Operation: "chat",
            StatusCode: 200,
            Succeeded: true,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            TotalTokens: totalTokens);

    private static async Task UpsertManualRateAsync(
        HttpClient client,
        string provider,
        string model,
        double inputUsdPer1MToken,
        double outputUsdPer1MToken)
    {
        var response = await client.PostAsJsonAsync("/admin/telemetry/usage/pricing/cards/override", new
        {
            provider,
            model,
            operation = "chat",
            inputUsdPer1MToken,
            outputUsdPer1MToken,
            effectiveFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
            effectiveToUtc = (DateTimeOffset?)null,
            updatedBy = "integration-tests",
            reason = "test-rate",
            sourceReference = "tests"
        });
        response.EnsureSuccessStatusCode();
    }
}
