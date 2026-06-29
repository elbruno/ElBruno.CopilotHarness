using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class RuleBasedModelRouterTests
{
    [Fact]
    public void RuleSet_RequestedModel_OverridesRules()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "always-ollama", "", RoutingRuleConditionType.Always, "", "ollama llama3.2", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "model": "foundry gpt-5-mini", "messages": [ { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
        Assert.Contains("Explicit", decision.Reason);
    }

    [Fact]
    public void RuleSet_PromptSizeAtLeast_Matches()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "big-prompts", "", RoutingRuleConditionType.PromptSizeAtLeast, "50", "foundry gpt-5-mini", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject($$"""
            { "messages": [ { "role": "user", "content": "{{new string('x', 80)}}" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
        Assert.Equal("Matched rule 'big-prompts'.", decision.Reason);
    }

    [Fact]
    public void RuleSet_IsStreaming_Matches()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "stream-rule", "", RoutingRuleConditionType.IsStreaming, "", "ollama llama3.2", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "stream": true, "messages": [ { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("ollama llama3.2", decision.ProfileName);
    }

    [Fact]
    public void RuleSet_HasSystemMessage_Matches()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "system-rule", "", RoutingRuleConditionType.HasSystemMessage, "", "foundry gpt-5-mini", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "system", "content": "be brief" }, { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
    }

    [Fact]
    public void RuleSet_PromptContainsKeyword_Matches()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "keyword-rule", "", RoutingRuleConditionType.PromptContainsKeyword, "refactor", "ollama llama3.2", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "please REFACTOR this method" } ] }
            """), options);

        Assert.Equal("ollama llama3.2", decision.ProfileName);
    }

    [Fact]
    public void RuleSet_PromptMatchesRegex_Matches()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "regex-rule", "", RoutingRuleConditionType.PromptMatchesRegex, @"\bSELECT\b.+\bFROM\b", "foundry gpt-5-mini", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "select id from users" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
    }

    [Fact]
    public void RuleSet_InvalidRegex_DoesNotMatch_FallsBackToDefault()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "bad-regex", "", RoutingRuleConditionType.PromptMatchesRegex, "(", "ollama llama3.2", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "anything" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
    }

    [Fact]
    public void RuleSet_PriorityOrder_FirstMatchWins()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(2, "low-priority", "", RoutingRuleConditionType.Always, "", "ollama llama3.2", 20, true),
            new RoutingRuleDefinition(1, "high-priority", "", RoutingRuleConditionType.Always, "", "foundry gpt-5-mini", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
        Assert.Equal("Matched rule 'high-priority'.", decision.Reason);
    }

    [Fact]
    public void RuleSet_DisabledRule_Skipped()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "disabled", "", RoutingRuleConditionType.Always, "", "ollama llama3.2", 10, false));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
        Assert.Equal("Default model profile.", decision.Reason);
    }

    [Fact]
    public void RuleSet_NoMatch_FallsBackToDefault()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "streaming-only", "", RoutingRuleConditionType.IsStreaming, "", "ollama llama3.2", 10, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("foundry gpt-5-mini", decision.ProfileName);
        Assert.Equal("Default model profile.", decision.Reason);
    }

    [Fact]
    public void RuleSet_TargetMissing_FallsThroughToNextRule()
    {
        var options = CreateOptions(
            new RoutingRuleDefinition(1, "missing-target", "", RoutingRuleConditionType.Always, "", "does-not-exist", 10, true),
            new RoutingRuleDefinition(2, "fallback-rule", "", RoutingRuleConditionType.Always, "", "ollama llama3.2", 20, true));

        var decision = BasicModelRouter.SelectModel(ParseObject("""
            { "messages": [ { "role": "user", "content": "hi" } ] }
            """), options);

        Assert.Equal("ollama llama3.2", decision.ProfileName);
        Assert.Equal("Matched rule 'fallback-rule'.", decision.Reason);
    }

    private static RoutingOptions CreateOptions(params RoutingRuleDefinition[] rules) =>
        new()
        {
            DefaultProfile = "foundry gpt-5-mini",
            Profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama llama3.2"] = new() { Type = ModelProviderType.Ollama, Deployment = "llama3.2", Endpoint = "http://localhost:11434", Enabled = true },
                ["foundry gpt-5-mini"] = new() { Type = ModelProviderType.AzureOpenAI, Deployment = "gpt-5-mini", Enabled = true }
            },
            RuleSet = rules
        };

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Expected a JSON object.");
}
