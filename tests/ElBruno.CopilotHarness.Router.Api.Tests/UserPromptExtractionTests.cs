using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// GitHub Copilot prepends a large boilerplate system preamble to every request.
/// Routing and classification must look at the user's actual message, not the whole
/// payload, otherwise every request looks "large" and goes to the cloud model.
/// </summary>
public sealed class UserPromptExtractionTests
{
    private const string LargeSystemPreamble =
        "You are an expert AI programming assistant working with a user in VS Code. " +
        "Follow the user's requirements carefully. Keep answers short. ";

    [Fact]
    public void GetUserPromptText_ReturnsLastUserMessage_IgnoringSystemPreamble()
    {
        var body = ParseObject($$"""
        {
          "messages": [
            { "role": "system", "content": "{{LargeSystemPreamble}}" },
            { "role": "user", "content": "hi" }
          ]
        }
        """);

        Assert.Equal("hi", BasicModelRouter.GetUserPromptText(body));
        Assert.Equal(2, BasicModelRouter.GetUserPromptCharacterCount(body));
    }

    [Fact]
    public void GetUserPromptCharacterCount_ExcludesSystemPreamble()
    {
        var body = ParseObject($$"""
        {
          "messages": [
            { "role": "system", "content": "{{LargeSystemPreamble}}" },
            { "role": "user", "content": "hi" }
          ]
        }
        """);

        // The full payload is large, but the user's ask is tiny.
        Assert.True(BasicModelRouter.GetPromptCharacterCount(body) > 100);
        Assert.Equal(2, BasicModelRouter.GetUserPromptCharacterCount(body));
    }

    [Fact]
    public void PromptSizeAtLeastRule_DoesNotMatch_WhenOnlySystemPreambleIsLarge()
    {
        var options = CreateOptions(bigPromptThreshold: 100);
        var body = ParseObject($$"""
        {
          "messages": [
            { "role": "system", "content": "{{LargeSystemPreamble}}" },
            { "role": "user", "content": "hi" }
          ]
        }
        """);

        // With intent supplied, the simple-chat rule routes "hi" to the local model
        // even though the full payload exceeds the big-prompt threshold.
        var decision = BasicModelRouter.SelectModel(body, options, ClassifierIntentNames.SimpleChat);

        Assert.Equal("ollama", decision.ProfileName);
    }

    [Fact]
    public void PromptSizeAtLeastRule_Matches_WhenUserMessageItselfIsLarge()
    {
        var options = CreateOptions(bigPromptThreshold: 100);
        var largeUserMessage = new string('x', 250);
        var body = ParseObject($$"""
        {
          "messages": [
            { "role": "user", "content": "{{largeUserMessage}}" }
          ]
        }
        """);

        var decision = BasicModelRouter.SelectModel(body, options, intent: null);

        Assert.Equal("gpt5mini", decision.ProfileName);
        Assert.Contains("Large prompts", decision.Reason);
    }

    private static RoutingOptions CreateOptions(int bigPromptThreshold) =>
        new()
        {
            DefaultProfile = "ollama",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new() { Deployment = "llama3.2", Enabled = true, IsProcessor = true },
                ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Enabled = true }
            },
            RuleSet =
            [
                new RoutingRuleDefinition(1, "Simple chat", "", RoutingRuleConditionType.IntentEquals, ClassifierIntentNames.SimpleChat, "ollama", 10, true),
                new RoutingRuleDefinition(2, "Large prompts", "", RoutingRuleConditionType.PromptSizeAtLeast, bigPromptThreshold.ToString(), "gpt5mini", 20, true)
            ]
        };

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Expected a JSON object.");
}