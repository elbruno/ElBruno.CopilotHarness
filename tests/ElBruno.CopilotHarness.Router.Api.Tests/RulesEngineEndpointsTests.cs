using System.Net;
using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class RulesEngineEndpointsTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RulesEngineEndpointsTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Rules_Wizard_GeneratesStarterRules()
    {
        var response = await _client.PostAsync("/admin/rules/wizard", content: null);
        response.EnsureSuccessStatusCode();
        var rules = await response.Content.ReadFromJsonAsync<List<RoutingRuleDto>>();

        Assert.NotNull(rules);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.TargetModel)));
    }

    [Fact]
    public async Task Rules_FullCrud_Lifecycle()
    {
        var create = new RoutingRuleUpsertRequest(
            Name: $"crud-rule-{Guid.NewGuid():N}",
            Description: "big prompts go to foundry",
            ConditionType: "PromptSizeAtLeast",
            ConditionValue: "1000",
            TargetModel: "foundry gpt-5-mini",
            Priority: 5,
            Enabled: true);

        var createResponse = await _client.PostAsJsonAsync("/admin/rules", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(created);
        Assert.Equal("PromptSizeAtLeast", created.ConditionType);

        var fetched = await _client.GetFromJsonAsync<RoutingRuleDto>($"/admin/rules/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(create.Name, fetched.Name);

        var update = create with { Priority = 1, ConditionValue = "2000" };
        var updateResponse = await _client.PutAsJsonAsync($"/admin/rules/{created.Id}", update);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(updated);
        Assert.Equal(1, updated.Priority);
        Assert.Equal("2000", updated.ConditionValue);

        var deleteResponse = await _client.DeleteAsync($"/admin/rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var afterDelete = await _client.GetAsync($"/admin/rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Rules_BadConditionType_ReturnsBadRequest()
    {
        var create = new RoutingRuleUpsertRequest(
            Name: $"bad-condition-{Guid.NewGuid():N}",
            Description: "",
            ConditionType: "NotARealCondition",
            ConditionValue: "",
            TargetModel: "foundry gpt-5-mini",
            Priority: 1,
            Enabled: true);

        var response = await _client.PostAsJsonAsync("/admin/rules", create);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rules_Test_MatchesExpectedRuleAndModel()
    {
        var create = new RoutingRuleUpsertRequest(
            Name: $"keyword-{Guid.NewGuid():N}",
            Description: "",
            ConditionType: "PromptContainsKeyword",
            ConditionValue: "translate",
            TargetModel: "ollama llama3.2",
            Priority: 1,
            Enabled: true);

        var created = await (await _client.PostAsJsonAsync("/admin/rules", create))
            .Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(created);

        try
        {
            var test = new RuleTestRequest(
                Prompt: "please translate this paragraph to Spanish",
                SystemMessage: null,
                Stream: false,
                RequestedModel: null);

            var response = await _client.PostAsJsonAsync("/admin/rules/test", test);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<RuleTestResponse>();

            Assert.NotNull(result);
            Assert.Equal(created.Name, result.MatchedRuleName);
            Assert.Equal("ollama llama3.2", result.SelectedModel);
        }
        finally
        {
            await _client.DeleteAsync($"/admin/rules/{created.Id}");
        }
    }

    [Fact]
    public async Task AnalyzerPrompt_ReturnsPromptForSemanticRules()
    {
        // Ensure at least one semantic rule exists so the analyzer prompt is non-trivial.
        var create = new RoutingRuleUpsertRequest(
            Name: $"semantic-{Guid.NewGuid():N}",
            Description: "Captures greetings and small talk such as hi and hola.",
            ConditionType: "SemanticMatch",
            ConditionValue: "",
            TargetModel: "ollama llama3.2",
            Priority: 7,
            Enabled: true);

        var created = await (await _client.PostAsJsonAsync("/admin/rules", create))
            .Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(created);

        try
        {
            var response = await _client.GetAsync("/admin/rules/analyzer-prompt");
            response.EnsureSuccessStatusCode();
            var prompt = await response.Content.ReadFromJsonAsync<RulesAnalyzerPromptResponse>();

            Assert.NotNull(prompt);
            Assert.True(prompt.SemanticRuleCount >= 1);
            Assert.Contains("routing analyzer", prompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(created.Name, prompt.SystemPrompt);
        }
        finally
        {
            await _client.DeleteAsync($"/admin/rules/{created.Id}");
        }
    }

    [Fact]
    public async Task Rules_Test_ConditionRule_ReturnsEnrichedMetadata()
    {
        var create = new RoutingRuleUpsertRequest(
            Name: $"keyword-{Guid.NewGuid():N}",
            Description: "",
            ConditionType: "PromptContainsKeyword",
            ConditionValue: "translate",
            TargetModel: "ollama llama3.2",
            Priority: 1,
            Enabled: true);

        var created = await (await _client.PostAsJsonAsync("/admin/rules", create))
            .Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(created);

        try
        {
            var test = new RuleTestRequest(
                Prompt: "please translate this paragraph to Spanish",
                SystemMessage: null,
                Stream: false,
                RequestedModel: null);

            var result = await (await _client.PostAsJsonAsync("/admin/rules/test", test))
                .Content.ReadFromJsonAsync<RuleTestResponse>();

            Assert.NotNull(result);
            // The enriched fields should always be present (defaults are fine when no trace facts exist).
            Assert.False(string.IsNullOrWhiteSpace(result.DecisionSource));
            Assert.Equal("ollama llama3.2", result.SelectedModel);
        }
        finally
        {
            await _client.DeleteAsync($"/admin/rules/{created.Id}");
        }
    }

    [Fact]
    public async Task DefaultModel_GetAndSet_RoundTrips()
    {
        var setResponse = await _client.PutAsJsonAsync(
            "/admin/rules/default",
            new SetDefaultModelRequest("ollama llama3.2"));
        setResponse.EnsureSuccessStatusCode();

        var current = await _client.GetFromJsonAsync<DefaultModelDto>("/admin/rules/default");
        Assert.NotNull(current);
        Assert.Equal("ollama llama3.2", current.ModelName);

        // restore default to avoid cross-test interference within the shared fixture
        await _client.PutAsJsonAsync("/admin/rules/default", new SetDefaultModelRequest("foundry gpt-5-mini"));
    }

    [Fact]
    public async Task ChatCompletions_RoutesThroughProvider_EndToEnd()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "foundry gpt-5-mini",
                messages = new[] { new { role = "user", content = "route me" } }
            })
        };
        request.Headers.TryAddWithoutValidation("x-copilot-client", "copilot-cli");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("stubbed assistant reply", body);
    }
}
