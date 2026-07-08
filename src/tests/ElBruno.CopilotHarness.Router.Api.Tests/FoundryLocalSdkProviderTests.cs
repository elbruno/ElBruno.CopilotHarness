using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Tests for Phase D – FoundryLocalSdk model provider type.
/// Validates the new ModelProviderType.FoundryLocalSdk value, its string mapping
/// (foundry-local-sdk), and persistence through the model registry.
/// </summary>
public sealed class FoundryLocalSdkProviderTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FoundryLocalSdkProviderTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SeedModels_ContainsFoundryLocalSdkEntry()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");

        Assert.NotNull(models);
        // The seed should include the foundry-local-sdk entry.
        var sdkEntry = models.FirstOrDefault(m => m.Type == "foundry-local-sdk");
        Assert.NotNull(sdkEntry);
        Assert.Equal("foundry local sdk phi-4-mini", sdkEntry.Name);
        Assert.Equal("phi-4-mini", sdkEntry.ModelName);
        // Endpoint is empty — auto-discovered from SDK at runtime.
        Assert.True(string.IsNullOrWhiteSpace(sdkEntry.Endpoint));
    }

    [Fact]
    public async Task CreateModel_WithFoundryLocalSdkType_Persists()
    {
        var name = $"test-sdk-{Guid.NewGuid():N}";
        var request = new ModelConnectionUpsertRequest(
            Name: name,
            Type: "foundry-local-sdk",
            Endpoint: string.Empty,
            ModelName: "phi-4-mini",
            ApiVersion: string.Empty,
            ApiKey: null,
            Enabled: true,
            IsProcessor: false,
            IsShadowProcessor: false,
            SupportsCustomTemperature: true,
            SupportsToolCalling: true);

        var create = await _client.PostAsJsonAsync("/admin/models", request);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ModelConnectionDto>();

        Assert.NotNull(created);
        Assert.Equal("foundry-local-sdk", created.Type);
        Assert.Equal("phi-4-mini", created.ModelName);
        Assert.True(string.IsNullOrWhiteSpace(created.Endpoint));
    }

    [Fact]
    public void FoundryLocalSdk_IsLocalProvider()
    {
        // The new enum value must satisfy IsLocalProvider() since it runs on the local machine.
        Assert.True(ModelProviderType.FoundryLocalSdk.IsLocalProvider());
    }

    [Fact]
    public void FoundryLocal_IsLocalProvider_StillTrue()
    {
        // Existing FoundryLocal HTTP type must still satisfy IsLocalProvider().
        Assert.True(ModelProviderType.FoundryLocal.IsLocalProvider());
    }

    [Fact]
    public void AzureOpenAI_IsLocalProvider_False()
    {
        Assert.False(ModelProviderType.AzureOpenAI.IsLocalProvider());
    }
}
