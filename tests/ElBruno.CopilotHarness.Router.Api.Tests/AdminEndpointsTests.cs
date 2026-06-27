using System.Net;
using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class AdminEndpointsTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AdminEndpointsTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsThreeSeededProfiles()
    {
        var response = await _client.GetAsync("/admin/models");

        response.EnsureSuccessStatusCode();
        var models = await response.Content.ReadFromJsonAsync<List<ModelProfileDto>>();

        Assert.NotNull(models);
        Assert.Contains(models, model => model.Name == "local");
        Assert.Contains(models, model => model.Name == "small");
        Assert.Contains(models, model => model.Name == "big");
    }

    [Fact]
    public async Task BasicRulesEndpoint_AllowsUpdates()
    {
        var update = new BasicRulesUpdateRequest(
            DefaultProfile: "small",
            BigPromptCharacterThreshold: 1234,
            BigProfile: "big",
            StreamingProfile: "small",
            PreferBigWhenSystemMessageExists: true,
            PreferStreamingProfileWhenStreaming: false);

        var updateResponse = await _client.PutAsJsonAsync("/admin/rules/basic", update);
        updateResponse.EnsureSuccessStatusCode();

        var readResponse = await _client.GetAsync("/admin/rules/basic");
        readResponse.EnsureSuccessStatusCode();
        var rules = await readResponse.Content.ReadFromJsonAsync<BasicRulesDto>();

        Assert.NotNull(rules);
        Assert.Equal(1234, rules.BigPromptCharacterThreshold);
        Assert.False(rules.PreferStreamingProfileWhenStreaming);
    }

    [Fact]
    public async Task SetupWizard_UpdatesSetupState()
    {
        var response = await _client.PostAsJsonAsync("/admin/setup/wizard", new SetupWizardRequest(
            LocalDeployment: "gpt-local",
            SmallDeployment: "gpt-small",
            BigDeployment: "gpt-big",
            DefaultProfile: "small",
            GenerateFirstRules: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setupStateResponse = await _client.GetAsync("/admin/setup/state");
        setupStateResponse.EnsureSuccessStatusCode();
        var setupState = await setupStateResponse.Content.ReadFromJsonAsync<SetupWizardResponse>();

        Assert.NotNull(setupState);
        Assert.True(setupState.IsCompleted);
        Assert.Equal("small", setupState.DefaultProfile);
    }
}
