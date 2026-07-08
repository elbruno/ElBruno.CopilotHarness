using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Locks in the tuned "start from zero" starter rule set produced by
/// <c>GenerateStarterRulesAsync</c>. A fresh database ships three seeded models
/// (<c>foundry local phi-4-mini</c> = processor/local, <c>ollama llama3.1</c> = alternative local,
/// <c>foundry gpt-5-mini</c> = cloud/large), so the wizard must emit exactly the golden rules
/// documented in <c>docs/Rules_Engine.md</c>. Uses its own isolated factory (fresh DB) so the
/// assertion is not affected by other tests mutating the shared fixture.
/// </summary>
public sealed class StarterRulesSeedTests
{
    private const string LocalModel = "foundry local phi-4-mini";
    private const string CloudModel = "foundry gpt-5-mini";

    [Fact]
    public async Task Wizard_OnFreshDatabase_SeedsTunedGoldenRuleSet()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/admin/rules/wizard", content: null);
        response.EnsureSuccessStatusCode();

        var rules = await response.Content.ReadFromJsonAsync<List<RoutingRuleDto>>();
        Assert.NotNull(rules);

        var expected = new (string Name, int Priority, string ConditionType, string TargetModel)[]
        {
            ("Simple chat", 5, "SemanticMatch", LocalModel),
            ("Code tasks", 6, "IntentEquals", CloudModel),
            ("Large prompts", 10, "PromptSizeAtLeast", CloudModel),
            ("System-guided prompts", 20, "HasSystemMessage", CloudModel),
            ("Streaming requests", 30, "IsStreaming", CloudModel),
            ("GitHub actions", 100, "SemanticMatch", LocalModel),
            ("Launch App actions", 110, "SemanticMatch", LocalModel),
            ("Dev environment actions", 112, "SemanticMatch", LocalModel),
            ("Build and test actions", 114, "SemanticMatch", LocalModel),
            ("Quick explanations", 116, "SemanticMatch", LocalModel),
            ("Short translations", 118, "SemanticMatch", LocalModel),
            ("Commit messages and summaries", 119, "SemanticMatch", LocalModel),
            ("Others actions", 120, "SemanticMatch", CloudModel),
        };

        Assert.Equal(expected.Length, rules!.Count);

        foreach (var (name, priority, conditionType, targetModel) in expected)
        {
            var rule = Assert.Single(rules, r => r.Name == name);
            Assert.Equal(priority, rule.Priority);
            Assert.Equal(conditionType, rule.ConditionType);
            Assert.Equal(targetModel, rule.TargetModel);
            Assert.True(rule.Enabled, $"Seeded rule '{name}' should be enabled.");
        }
    }
}
