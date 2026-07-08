using System.Net;
using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Tests for GET /admin/models/{id}/status.
/// The endpoint probes the model's endpoint; because no real Ollama / Foundry Local
/// process is running in the test environment the status should be "unreachable"
/// for local models and "unavailable" for Azure models.
/// </summary>
public sealed class ModelStatusEndpointTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ModelStatusEndpointTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> GetOllamaModelIdAsync()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");
        return models!.First(m => m.Type == "ollama").Id;
    }

    private async Task<string?> GetFoundryLocalModelIdAsync()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");
        return models!.FirstOrDefault(m => m.Type == "foundry-local")?.Id;
    }

    private async Task<string> GetAzureModelIdAsync()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");
        return models!.First(m => m.Type == "azure-openai").Id;
    }

    [Fact]
    public async Task Status_Ollama_WhenNotRunning_ReturnsUnreachable()
    {
        var id = await GetOllamaModelIdAsync();

        var status = await _client.GetFromJsonAsync<ModelStatusDto>($"/admin/models/{id}/status");

        Assert.NotNull(status);
        Assert.Equal("unreachable", status.Status);
        Assert.False(status.IsEndpointReachable);
    }

    [Fact]
    public async Task Status_Azure_ReturnsUnavailable()
    {
        var id = await GetAzureModelIdAsync();

        var status = await _client.GetFromJsonAsync<ModelStatusDto>($"/admin/models/{id}/status");

        Assert.NotNull(status);
        Assert.Equal("unavailable", status.Status);
        // Azure models report "unavailable" — the dedicated /test endpoint is for Azure.
        Assert.False(status.IsEndpointReachable);
        Assert.False(status.IsModelAvailable);
    }

    [Fact]
    public async Task Status_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/admin/models/does-not-exist-xyz/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Status_Ollama_HasDetailsString()
    {
        var id = await GetOllamaModelIdAsync();

        var status = await _client.GetFromJsonAsync<ModelStatusDto>($"/admin/models/{id}/status");

        // Details should be non-null for both reachable and unreachable paths.
        Assert.NotNull(status?.Details);
        Assert.NotEmpty(status.Details);
    }

    [Fact]
    public async Task Status_Ollama_ResponseIsSerializable()
    {
        var id = await GetOllamaModelIdAsync();

        var response = await _client.GetAsync($"/admin/models/{id}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
