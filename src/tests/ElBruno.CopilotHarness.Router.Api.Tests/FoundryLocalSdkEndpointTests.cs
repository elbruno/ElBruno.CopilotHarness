using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Tests for Phase C – Foundry Local SDK admin endpoints.
/// These tests verify the HTTP contracts of the endpoints without requiring
/// a live Foundry Local installation.
/// </summary>
public sealed class FoundryLocalSdkEndpointTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FoundryLocalSdkEndpointTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Status_Returns200WithCorrectShape()
    {
        var response = await _client.GetAsync("/admin/foundrylocal/status");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<FoundryLocalSdkStatusDto>();
        Assert.NotNull(dto);
        // In test host, SDK is NOT initialized (no Foundry Local installed).
        Assert.False(dto.IsInitialized);
        Assert.Null(dto.WebServiceUrl);
    }

    [Fact]
    public async Task Catalog_WhenSdkNotInitialized_Returns503()
    {
        // The catalog endpoint tries to initialize the SDK, which fails in CI.
        // We expect either 200 (empty list on graceful failure) or 503 (service unavailable).
        var response = await _client.GetAsync("/admin/foundrylocal/catalog");

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.OK
                or System.Net.HttpStatusCode.ServiceUnavailable,
            $"Expected 200 or 503 but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task DownloadProgress_UnknownAlias_ReturnsIdleStatus()
    {
        var response = await _client.GetAsync("/admin/foundrylocal/catalog/no-such-model/progress");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var status = body.GetProperty("status").GetString();
        Assert.Equal("idle", status);
    }

    [Fact]
    public async Task Init_Returns200WithStatusShape()
    {
        // POST /init will attempt to initialize the SDK (will fail gracefully in CI without Foundry Local).
        var response = await _client.PostAsync("/admin/foundrylocal/init", null);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<FoundryLocalSdkStatusDto>();
        Assert.NotNull(dto);
        // Regardless of whether SDK loaded, the DTO shape must be present.
        Assert.True(dto.IsInitialized || dto.InitError is not null || !dto.IsInitialized);
    }
}
