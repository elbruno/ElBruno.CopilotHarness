using System.Net;
using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class ModelRegistryEndpointsTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ModelRegistryEndpointsTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Models_FreshDb_SeedsExampleConnections()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");

        Assert.NotNull(models);
        Assert.Contains(models, m => m.Name == "ollama llama3.2" && m.Type == "ollama");
        Assert.Contains(models, m => m.Name == "foundry gpt-5-mini" && m.Type == "azure-openai");
    }

    [Fact]
    public async Task Models_Seed_FlagsTemperatureCapability()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");

        var ollama = models!.First(m => m.Name == "ollama llama3.2");
        var gpt5 = models.First(m => m.Name == "foundry gpt-5-mini");

        // gpt-5-mini rejects custom temperature; ollama accepts it. (The processor
        // flag is mutable global state exercised by other tests, so it is asserted
        // separately in Models_SettingProcessor_ClearsItOnOtherModels.)
        Assert.True(ollama.SupportsCustomTemperature);
        Assert.False(gpt5.SupportsCustomTemperature);
    }

    [Fact]
    public async Task Models_SettingProcessor_ClearsItOnOtherModels()
    {
        // Create a brand new model and flag it as the processor.
        var create = new ModelConnectionUpsertRequest(
            Name: $"processor-{Guid.NewGuid():N}",
            Type: "ollama",
            Endpoint: "http://localhost:11434",
            ModelName: "phi3",
            ApiVersion: "2024-10-21",
            ApiKey: null,
            Enabled: true,
            IsProcessor: true,
            SupportsCustomTemperature: true);

        var created = await (await _client.PostAsJsonAsync("/admin/models", create))
            .Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(created);
        Assert.True(created!.IsProcessor);

        // Exactly one model is the processor across the registry.
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");
        Assert.Single(models!, m => m.IsProcessor);
        Assert.Equal(created.Id, models!.Single(m => m.IsProcessor).Id);
    }

    [Fact]
    public async Task Models_FullCrud_Lifecycle()
    {
        var create = new ModelConnectionUpsertRequest(
            Name: $"crud-model-{Guid.NewGuid():N}",
            Type: "azure-openai",
            Endpoint: "https://crud.openai.azure.com",
            ModelName: "gpt-5.5",
            ApiVersion: "2024-10-21",
            ApiKey: "super-secret",
            Enabled: true);

        var createResponse = await _client.PostAsJsonAsync("/admin/models", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(created);
        Assert.True(created.HasApiKey);

        // List + Get
        var fetched = await _client.GetFromJsonAsync<ModelConnectionDto>($"/admin/models/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(create.Name, fetched.Name);
        Assert.Equal("gpt-5.5", fetched.ModelName);

        // Update (leave key unchanged when null)
        var update = create with { ModelName = "gpt-5.5-turbo", ApiKey = null };
        var updateResponse = await _client.PutAsJsonAsync($"/admin/models/{created.Id}", update);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(updated);
        Assert.Equal("gpt-5.5-turbo", updated.ModelName);
        Assert.True(updated.HasApiKey);

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/admin/models/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var afterDelete = await _client.GetAsync($"/admin/models/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Models_ApiKey_NeverReturnedInPlaintext()
    {
        var create = new ModelConnectionUpsertRequest(
            Name: $"secret-model-{Guid.NewGuid():N}",
            Type: "azure-openai",
            Endpoint: "https://secret.openai.azure.com",
            ModelName: "gpt-5-mini",
            ApiVersion: "2024-10-21",
            ApiKey: "plaintext-should-not-leak",
            Enabled: true);

        var createResponse = await _client.PostAsJsonAsync("/admin/models", create);
        createResponse.EnsureSuccessStatusCode();

        var listJson = await _client.GetStringAsync("/admin/models");
        Assert.DoesNotContain("plaintext-should-not-leak", listJson);
    }

    [Fact]
    public async Task Models_ClearApiKey_WithEmptyString()
    {
        var create = new ModelConnectionUpsertRequest(
            Name: $"clear-key-{Guid.NewGuid():N}",
            Type: "azure-openai",
            Endpoint: "https://clear.openai.azure.com",
            ModelName: "gpt-5-mini",
            ApiVersion: "2024-10-21",
            ApiKey: "to-be-cleared",
            Enabled: true);

        var created = await (await _client.PostAsJsonAsync("/admin/models", create))
            .Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(created);
        Assert.True(created.HasApiKey);

        var cleared = await (await _client.PutAsJsonAsync($"/admin/models/{created.Id}", create with { ApiKey = "" }))
            .Content.ReadFromJsonAsync<ModelConnectionDto>();
        Assert.NotNull(cleared);
        Assert.False(cleared.HasApiKey);
    }

    [Fact]
    public async Task ModelTest_ReturnsConnectivityResult()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");
        var foundry = models!.First(m => m.Name == "foundry gpt-5-mini");

        var response = await _client.PostAsync($"/admin/models/{foundry.Id}/test", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ModelConnectionTestResponse>();

        Assert.NotNull(result);
        Assert.True(result.Success);
    }
}
