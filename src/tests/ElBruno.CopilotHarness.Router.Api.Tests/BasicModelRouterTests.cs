using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class BasicModelRouterTests
{
    [Fact]
    public void SelectModel_UsesExplicitProfile_WhenEnabledProfileIsRequested()
    {
        var router = CreateRouter();
        var body = ParseObject("""
            {
              "model": "big",
              "messages": [
                { "role": "user", "content": "hello" }
              ]
            }
            """);

        var decision = router.SelectModel(body);

        Assert.Equal("big", decision.ProfileName);
        Assert.Equal("Explicit model profile requested by client.", decision.Reason);
    }

    [Fact]
    public void SelectModel_PrefersBigProfile_WhenSystemMessageExists()
    {
        var router = CreateRouter();
        var body = ParseObject("""
            {
              "messages": [
                { "role": "system", "content": "You are an expert." },
                { "role": "user", "content": "short prompt" }
              ]
            }
            """);

        var decision = router.SelectModel(body);

        Assert.Equal("big", decision.ProfileName);
        Assert.Equal("System message detected by basic rule.", decision.Reason);
    }

    [Fact]
    public void SelectModel_UsesBigProfile_WhenPromptExceedsThreshold()
    {
        var router = CreateRouter();
        var body = ParseObject($$"""
            {
              "messages": [
                { "role": "user", "content": "{{new string('x', 80)}}" }
              ]
            }
            """);

        var decision = router.SelectModel(body);

        Assert.Equal("big", decision.ProfileName);
        Assert.Equal("Prompt size exceeded threshold.", decision.Reason);
    }

    [Fact]
    public void SelectModel_UsesStreamingProfile_WhenStreamingRequested()
    {
        var router = CreateRouter();
        var body = ParseObject("""
            {
              "stream": true,
              "messages": [
                { "role": "user", "content": "short prompt" }
              ]
            }
            """);

        var decision = router.SelectModel(body);

        Assert.Equal("small", decision.ProfileName);
        Assert.Equal("Streaming request matched basic streaming rule.", decision.Reason);
    }

    [Fact]
    public void SelectModel_UsesDefaultProfile_WhenNoOtherRuleMatches()
    {
        var router = CreateRouter();
        var body = ParseObject("""
            {
              "messages": [
                { "role": "user", "content": "short prompt" }
              ]
            }
            """);

        var decision = router.SelectModel(body);

        Assert.Equal("small", decision.ProfileName);
        Assert.Equal("Default model profile.", decision.Reason);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    private static BasicModelRouter CreateRouter()
    {
        var options = new RoutingOptions
        {
            DefaultProfile = "small",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["local"] = new() { Deployment = "gpt-local", Enabled = true },
                ["small"] = new() { Deployment = "gpt-small", Enabled = true },
                ["big"] = new() { Deployment = "gpt-big", Enabled = true }
            },
            Rules = new BasicRulesOptions
            {
                BigPromptCharacterThreshold = 50,
                BigProfile = "big",
                StreamingProfile = "small",
                PreferBigWhenSystemMessageExists = true,
                PreferStreamingProfileWhenStreaming = true
            }
        };

        return new BasicModelRouter(Options.Create(options));
    }

    private static JsonObject ParseObject(string json)
    {
        return JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidOperationException("Expected a JSON object.");
    }
}
