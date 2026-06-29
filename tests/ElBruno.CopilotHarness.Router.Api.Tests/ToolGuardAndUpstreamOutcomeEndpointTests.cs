using System.Net;
using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Endpoint-level tests for the tool-calling capability guard (C), the <c>SupportsToolCalling</c>
/// persistence round-trip (D), upstream outcome capture (E) and the Live feed projection (F).
/// </summary>
public sealed class ToolGuardAndUpstreamOutcomeEndpointTests
{
    private const string OllamaModel = "ollama llama3.2";        // seeded: SupportsToolCalling = false
    private const string FoundryModel = "foundry gpt-5-mini";    // seeded: SupportsToolCalling = true
    private const string LocalToolModel = "ollama llama3.1 (tools)"; // seeded: local tool-caller, preferred for overrides

    private static object ToolsPayload(string content) => new
    {
        messages = new[] { new { role = "user", content } },
        tools = new[]
        {
            new
            {
                type = "function",
                function = new { name = "get_weather", description = "Get the weather", parameters = new { } }
            }
        }
    };

    // ── D. SupportsToolCalling persistence ────────────────────────────────────

    [Fact]
    public async Task Models_Seed_FlagsToolCallingCapability()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var models = await client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");

        var ollama = models!.First(m => m.Name == OllamaModel);
        var gpt5 = models.First(m => m.Name == FoundryModel);

        Assert.False(ollama.SupportsToolCalling);
        Assert.True(gpt5.SupportsToolCalling);
    }

    [Fact]
    public async Task Models_Upsert_SupportsToolCalling_RoundTrips()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var create = new ModelConnectionUpsertRequest(
            Name: $"tools-off-{Guid.NewGuid():N}",
            Type: "ollama",
            Endpoint: "http://localhost:11434",
            ModelName: "phi3",
            ApiVersion: "2024-10-21",
            ApiKey: null,
            Enabled: true,
            IsProcessor: false,
            SupportsCustomTemperature: true,
            SupportsToolCalling: false);

        var created = await (await client.PostAsJsonAsync("/admin/models", create))
            .Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(created);
        Assert.False(created!.SupportsToolCalling);

        // Round-trips when read back from the store.
        var fetched = await client.GetFromJsonAsync<ModelConnectionDto>($"/admin/models/{created.Id}");
        Assert.NotNull(fetched);
        Assert.False(fetched!.SupportsToolCalling);

        // Flipping it on persists as well.
        var updated = await (await client.PutAsJsonAsync($"/admin/models/{created.Id}", create with { SupportsToolCalling = true }))
            .Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(updated);
        Assert.True(updated!.SupportsToolCalling);
    }

    // ── C. Tool-calling guard (end-to-end) ────────────────────────────────────

    [Fact]
    public async Task ToolRequest_OnNonCapableModel_OverridesToToolCapableModel()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        // Force routing to the non-tool-capable Ollama model via a keyword rule.
        await CreateKeywordRuleAsync(client, "weather", OllamaModel);

        var response = await client.PostAsJsonAsync("/v1/chat/completions", ToolsPayload("what is the weather today"));
        response.EnsureSuccessStatusCode();

        var entry = await GetSingleFeedEntryAsync(client);
        Assert.True(entry.RequestHadTools);
        Assert.True(entry.ToolCapabilityOverrideApplied);
        Assert.False(string.IsNullOrWhiteSpace(entry.OverrideReason));
        // The override redirected to the local tool-capable model (preferred over cloud).
        Assert.Contains(LocalToolModel, entry.OverrideReason!);
    }

    [Fact]
    public async Task ToolRequest_OnCapableModel_NoOverride()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        // No routing rules: requests fall through to the default tool-capable model (foundry gpt-5-mini).
        var response = await client.PostAsJsonAsync("/v1/chat/completions", ToolsPayload("hello capable model"));
        response.EnsureSuccessStatusCode();

        var entry = await GetSingleFeedEntryAsync(client);
        Assert.True(entry.RequestHadTools);
        Assert.False(entry.ToolCapabilityOverrideApplied);
    }

    [Fact]
    public async Task RequestWithoutTools_OnNonCapableModel_NoOverride()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        // Force routing to the non-tool-capable Ollama model, but send no tools.
        await CreateKeywordRuleAsync(client, "weather", OllamaModel);

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "what is the weather today" } }
        });
        response.EnsureSuccessStatusCode();

        var entry = await GetSingleFeedEntryAsync(client);
        Assert.False(entry.RequestHadTools);
        Assert.False(entry.ToolCapabilityOverrideApplied);
    }

    // ── E. Upstream outcome capture ───────────────────────────────────────────

    [Fact]
    public async Task UpstreamSuccess_RecordsStatusAndLatency()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "ping the upstream" } }
        });
        response.EnsureSuccessStatusCode();

        var entry = await GetSingleFeedEntryAsync(client);
        Assert.Equal(200, entry.UpstreamStatusCode);
        Assert.True(entry.UpstreamSucceeded);
        Assert.NotNull(entry.UpstreamLatencyMs);
        Assert.True(string.IsNullOrEmpty(entry.UpstreamError));
    }

    [Fact]
    public async Task UpstreamException_Returns502_WithOpenAiErrorJson_AndRecordsFailure()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "force-upstream-error please" } }
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<OpenAiErrorEnvelope>();
        Assert.NotNull(error);
        Assert.Equal("upstream_error", error!.Error.Type);
        Assert.False(string.IsNullOrWhiteSpace(error.Error.Message));

        var entry = await GetSingleFeedEntryAsync(client);
        Assert.False(entry.UpstreamSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(entry.UpstreamError));
    }

    [Fact]
    public async Task UpstreamTimeout_Returns504_AndRecordsFailure()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "force-upstream-timeout please" } }
        });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<OpenAiErrorEnvelope>();
        Assert.NotNull(error);
        Assert.Equal("upstream_timeout", error!.Error.Type);

        var entry = await GetSingleFeedEntryAsync(client);
        Assert.False(entry.UpstreamSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(entry.UpstreamError));
    }

    // ── F. Feed DTO surfaces the new facts ────────────────────────────────────

    [Fact]
    public async Task Feed_SurfacesUpstreamAndToolFacts()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["Telemetry:CapturePromptText"] = "true"
        });
        var client = factory.CreateClient();

        await CreateKeywordRuleAsync(client, "weather", OllamaModel);

        var response = await client.PostAsJsonAsync("/v1/chat/completions", ToolsPayload("what is the weather today"));
        response.EnsureSuccessStatusCode();

        var entry = await GetSingleFeedEntryAsync(client);
        // All new fields are surfaced on the feed DTO.
        Assert.Equal(200, entry.UpstreamStatusCode);
        Assert.True(entry.UpstreamSucceeded);
        Assert.NotNull(entry.UpstreamLatencyMs);
        Assert.True(entry.RequestHadTools);
        Assert.True(entry.ToolCapabilityOverrideApplied);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task CreateKeywordRuleAsync(HttpClient client, string keyword, string targetModel)
    {
        var rule = new RoutingRuleUpsertRequest(
            Name: $"force-{Guid.NewGuid():N}",
            Description: "",
            ConditionType: "PromptContainsKeyword",
            ConditionValue: keyword,
            TargetModel: targetModel,
            Priority: 1,
            Enabled: true);

        var created = await client.PostAsJsonAsync("/admin/rules", rule);
        created.EnsureSuccessStatusCode();
    }

    private static async Task<FeedRequestView> GetSingleFeedEntryAsync(HttpClient client)
    {
        var feed = await client.GetFromJsonAsync<FeedResponse>("/admin/telemetry/feed");
        Assert.NotNull(feed);
        return Assert.Single(feed!.Requests);
    }

    // Local DTOs mirroring the server shape, including the tool-calling/upstream fields under test.
    private sealed record FeedResponse(IReadOnlyList<FeedRequestView> Requests);

    private sealed record FeedRequestView(
        string TraceId,
        string SelectedModel,
        string Deployment,
        int? UpstreamStatusCode,
        double? UpstreamLatencyMs,
        bool UpstreamSucceeded,
        string? UpstreamError,
        bool RequestHadTools,
        bool ToolCapabilityOverrideApplied,
        string? OverrideReason);

    private sealed record OpenAiErrorEnvelope(OpenAiError Error);

    private sealed record OpenAiError(string? Message, string? Type);
}
