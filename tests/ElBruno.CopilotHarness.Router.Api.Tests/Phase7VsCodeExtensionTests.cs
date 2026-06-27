using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class Phase7VsCodeExtensionTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public Phase7VsCodeExtensionTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StatusSurface_ReturnsDashboardLinksAndHealthChecks()
    {
        var response = await _client.GetAsync("/v1/status");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", payload.GetProperty("state").GetString());
        Assert.True(payload.TryGetProperty("dashboardLinks", out var links));
        Assert.True(links.GetArrayLength() >= 4);
        Assert.Contains(links.EnumerateArray(), link => link.GetProperty("path").GetString() == "/v1/status");
        Assert.True(payload.TryGetProperty("healthChecks", out var checks));
        Assert.True(checks.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExplainRoutingSurface_ReturnsRoutingTraceDetails()
    {
        var routedResponse = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "explain this route" } }
        });
        routedResponse.EnsureSuccessStatusCode();
        var traceId = routedResponse.Headers.GetValues("x-harness-trace-id").Single();

        var response = await _client.GetAsync($"/v1/explain-routing/{traceId}");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(traceId, payload.GetProperty("traceId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("decision").GetProperty("profile").GetString()));
        Assert.True(payload.TryGetProperty("steps", out var steps));
        Assert.Contains(steps.EnumerateArray(), step => step.GetProperty("name").GetString() == "routing-decision");
    }

    [Fact]
    public async Task ExtensionCapabilities_ReturnsChatParticipantAndToolMetadata()
    {
        var response = await _client.GetAsync("/v1/extension/capabilities");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("@harness", payload.GetProperty("chatParticipant").GetProperty("participantName").GetString());
        Assert.True(payload.GetProperty("dashboardLinks").GetArrayLength() >= 4);
        Assert.True(payload.GetProperty("languageModelTools").GetArrayLength() >= 3);
        Assert.Contains(payload.GetProperty("languageModelTools").EnumerateArray(), tool =>
            tool.GetProperty("name").GetString() == "harness.explain-routing");
    }
}
