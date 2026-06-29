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
    public async Task ModelsEndpoint_ReturnsSeededConnections()
    {
        var response = await _client.GetAsync("/admin/models");

        response.EnsureSuccessStatusCode();
        var models = await response.Content.ReadFromJsonAsync<List<ModelConnectionDto>>();

        Assert.NotNull(models);
        Assert.Contains(models, model => model.Name == "ollama llama3.2" && model.Type == "ollama");
        Assert.Contains(models, model => model.Name == "foundry gpt-5-mini" && model.Type == "azure-openai");
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
            DefaultModel: "foundry gpt-5-mini",
            GenerateFirstRules: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setupStateResponse = await _client.GetAsync("/admin/setup/state");
        setupStateResponse.EnsureSuccessStatusCode();
        var setupState = await setupStateResponse.Content.ReadFromJsonAsync<SetupWizardResponse>();

        Assert.NotNull(setupState);
        Assert.True(setupState.IsCompleted);
        Assert.Equal("foundry gpt-5-mini", setupState.DefaultProfile);
    }

    [Fact]
    public async Task DashboardSnapshot_ReturnsConnectedClientsAndLiveRequests()
    {
        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "small",
                messages = new[] { new { role = "user", content = "dashboard telemetry" } }
            })
        };
        chatRequest.Headers.TryAddWithoutValidation("x-copilot-client", "copilot-cli");

        var chatResponse = await _client.SendAsync(chatRequest);
        chatResponse.EnsureSuccessStatusCode();

        var dashboardResponse = await _client.GetAsync("/admin/dashboard/snapshot");
        dashboardResponse.EnsureSuccessStatusCode();
        var snapshot = await dashboardResponse.Content.ReadFromJsonAsync<DashboardSnapshotResponse>();

        Assert.NotNull(snapshot);
        var cliClient = snapshot.ConnectedClients.Single(client => client.Client == "copilot-cli");
        Assert.True(cliClient.IsConnected);
        Assert.True(cliClient.RequestsLastFiveMinutes >= 1);
        Assert.Contains(snapshot.LiveRequests, request => request.Client == "copilot-cli");
    }
}
