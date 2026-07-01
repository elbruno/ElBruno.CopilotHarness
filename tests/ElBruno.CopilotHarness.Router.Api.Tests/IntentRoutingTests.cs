using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class IntentRoutingTests
{
    [Fact]
    public void SelectModel_MatchesIntentEqualsRule_WhenIntentMatches()
    {
        var options = CreateOptions();
        var body = ParseObject("""{ "messages": [ { "role": "user", "content": "hi" } ] }""");

        var decision = BasicModelRouter.SelectModel(body, options, ClassifierIntentNames.SimpleChat);

        Assert.Equal("ollama", decision.ProfileName);
        Assert.Contains("Simple chat", decision.Reason);
    }

    [Fact]
    public void SelectModel_RoutesCodeTaskIntent_ToCloudModel()
    {
        var options = CreateOptions();
        var body = ParseObject("""{ "messages": [ { "role": "user", "content": "refactor this" } ] }""");

        var decision = BasicModelRouter.SelectModel(body, options, ClassifierIntentNames.CodeTask);

        Assert.Equal("gpt5mini", decision.ProfileName);
    }

    [Fact]
    public void SelectModel_IgnoresIntentRules_WhenIntentIsNull()
    {
        var options = CreateOptions();
        var body = ParseObject("""{ "messages": [ { "role": "user", "content": "hi" } ] }""");

        var decision = BasicModelRouter.SelectModel(body, options, intent: null);

        // No intent → IntentEquals rules cannot match → default model.
        Assert.Equal("ollama", decision.ProfileName);
        Assert.Equal("Default model profile.", decision.Reason);
    }

    private static RoutingOptions CreateOptions() =>
        new()
        {
            DefaultProfile = "ollama",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new() { Deployment = "llama3.1:8b", Enabled = true, IsProcessor = true },
                ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Enabled = true, SupportsCustomTemperature = false }
            },
            RuleSet =
            [
                new RoutingRuleDefinition(1, "Simple chat", "", RoutingRuleConditionType.IntentEquals, ClassifierIntentNames.SimpleChat, "ollama", 10, true),
                new RoutingRuleDefinition(2, "Code tasks", "", RoutingRuleConditionType.IntentEquals, ClassifierIntentNames.CodeTask, "gpt5mini", 40, true)
            ]
        };

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Expected a JSON object.");
}
