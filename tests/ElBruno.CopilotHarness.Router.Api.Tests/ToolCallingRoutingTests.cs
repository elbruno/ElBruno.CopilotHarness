using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Unit tests for the tool-calling capability helpers in <see cref="OpenAiApiUtilities"/> and the
/// upstream-outcome → trace-fact projection. These cover cases A/B and the fact projection used by E/F.
/// </summary>
public sealed class ToolCallingRoutingTests
{
    // ── A. RequestHasTools ────────────────────────────────────────────────────

    [Fact]
    public void RequestHasTools_True_WhenNonEmptyToolsArray()
    {
        var body = ParseObject("""
            { "messages": [], "tools": [ { "type": "function", "function": { "name": "get_weather" } } ] }
            """);

        Assert.True(OpenAiApiUtilities.RequestHasTools(body));
    }

    [Fact]
    public void RequestHasTools_False_WhenToolsMissing()
    {
        var body = ParseObject("""{ "messages": [] }""");

        Assert.False(OpenAiApiUtilities.RequestHasTools(body));
    }

    [Fact]
    public void RequestHasTools_False_WhenToolsEmptyArray()
    {
        var body = ParseObject("""{ "messages": [], "tools": [] }""");

        Assert.False(OpenAiApiUtilities.RequestHasTools(body));
    }

    [Fact]
    public void RequestHasTools_False_WhenToolsNull()
    {
        var body = ParseObject("""{ "messages": [], "tools": null }""");

        Assert.False(OpenAiApiUtilities.RequestHasTools(body));
    }

    [Fact]
    public void RequestHasTools_False_WhenBodyNull()
    {
        Assert.False(OpenAiApiUtilities.RequestHasTools(null));
    }

    // ── B. FindToolCapableModel ───────────────────────────────────────────────

    [Fact]
    public void FindToolCapableModel_ReturnsEnabledCapableModel_PreferringAzure()
    {
        var options = new RoutingOptions
        {
            DefaultProfile = "ollama",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new() { Deployment = "llama3.2", Type = ModelProviderType.Ollama, Enabled = true, SupportsToolCalling = false },
                ["local-capable"] = new() { Deployment = "qwen", Type = ModelProviderType.Ollama, Enabled = true, SupportsToolCalling = true },
                ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Type = ModelProviderType.AzureOpenAI, Enabled = true, SupportsToolCalling = true }
            }
        };

        var result = OpenAiApiUtilities.FindToolCapableModel(options, excludeProfileName: "ollama");

        Assert.NotNull(result);
        // Azure model is preferred over the Ollama one even though both are tool-capable.
        Assert.Equal("gpt5mini", result!.Value.ProfileName);
        Assert.Equal("gpt-5-mini", result.Value.Profile.Deployment);
    }

    [Fact]
    public void FindToolCapableModel_ExcludesSpecifiedProfile()
    {
        var options = new RoutingOptions
        {
            DefaultProfile = "gpt5mini",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Type = ModelProviderType.AzureOpenAI, Enabled = true, SupportsToolCalling = true },
                ["other-capable"] = new() { Deployment = "gpt-4o", Type = ModelProviderType.AzureOpenAI, Enabled = true, SupportsToolCalling = true }
            }
        };

        var result = OpenAiApiUtilities.FindToolCapableModel(options, excludeProfileName: "gpt5mini");

        Assert.NotNull(result);
        Assert.Equal("other-capable", result!.Value.ProfileName);
    }

    [Fact]
    public void FindToolCapableModel_ReturnsNull_WhenNoCapableModel()
    {
        var options = new RoutingOptions
        {
            DefaultProfile = "ollama",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new() { Deployment = "llama3.2", Type = ModelProviderType.Ollama, Enabled = true, SupportsToolCalling = false },
                ["phi"] = new() { Deployment = "phi3", Type = ModelProviderType.Ollama, Enabled = true, SupportsToolCalling = false }
            }
        };

        var result = OpenAiApiUtilities.FindToolCapableModel(options, excludeProfileName: null);

        Assert.Null(result);
    }

    [Fact]
    public void FindToolCapableModel_IgnoresDisabledCapableModels()
    {
        var options = new RoutingOptions
        {
            DefaultProfile = "ollama",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new() { Deployment = "llama3.2", Type = ModelProviderType.Ollama, Enabled = true, SupportsToolCalling = false },
                ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Type = ModelProviderType.AzureOpenAI, Enabled = false, SupportsToolCalling = true }
            }
        };

        var result = OpenAiApiUtilities.FindToolCapableModel(options, excludeProfileName: "ollama");

        Assert.Null(result);
    }

    // ── BuildUpstreamFacts (projection consumed by E/F) ───────────────────────

    [Fact]
    public void BuildUpstreamFacts_SuccessOutcome_ProjectsStatusAndLatency()
    {
        var outcome = new RequestOutcome(
            StatusCode: 200,
            LatencyMs: 12.5,
            Succeeded: true,
            Error: null,
            HadTools: true,
            ToolOverrideApplied: false,
            OverrideReason: null);

        var facts = OpenAiApiUtilities.BuildUpstreamFacts(outcome).ToDictionary(f => f.Key, f => f.Value);

        Assert.Equal("200", facts["upstream.status"]);
        Assert.Equal("12.5", facts["upstream.latencyMs"]);
        Assert.Equal("true", facts["upstream.succeeded"]);
        Assert.Equal("true", facts["request.hadTools"]);
        Assert.Equal("false", facts["routing.toolOverride"]);
        Assert.False(facts.ContainsKey("upstream.error"));
        Assert.False(facts.ContainsKey("routing.toolOverrideReason"));
    }

    [Fact]
    public void BuildUpstreamFacts_FailureOutcome_ProjectsErrorAndOverrideReason()
    {
        var outcome = new RequestOutcome(
            StatusCode: 502,
            LatencyMs: null,
            Succeeded: false,
            Error: "boom",
            HadTools: true,
            ToolOverrideApplied: true,
            OverrideReason: "routed to capable model");

        var facts = OpenAiApiUtilities.BuildUpstreamFacts(outcome).ToDictionary(f => f.Key, f => f.Value);

        Assert.Equal("502", facts["upstream.status"]);
        Assert.Equal("false", facts["upstream.succeeded"]);
        Assert.Equal("boom", facts["upstream.error"]);
        Assert.Equal("true", facts["routing.toolOverride"]);
        Assert.Equal("routed to capable model", facts["routing.toolOverrideReason"]);
        Assert.False(facts.ContainsKey("upstream.latencyMs"));
    }

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Expected a JSON object.");
}
