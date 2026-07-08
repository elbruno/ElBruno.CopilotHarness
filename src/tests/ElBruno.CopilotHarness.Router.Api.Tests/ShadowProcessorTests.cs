using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api.Admin;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Tests for Phase B – Shadow processor model A/B evaluation.
/// Verifies:
///  - IsShadowProcessor flag persists on create/update
///  - Single shadow processor invariant (only one at a time)
///  - A/B summary endpoint returns correct shape
/// </summary>
public sealed class ShadowProcessorTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ShadowProcessorTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── IsShadowProcessor flag ────────────────────────────────────────────────

    [Fact]
    public async Task CreateModel_WithIsShadowProcessor_PersistsFlag()
    {
        var request = new ModelConnectionUpsertRequest(
            Name: $"shadow-test-{Guid.NewGuid():N}",
            Type: "ollama",
            Endpoint: "http://localhost:11434",
            ModelName: "llama3.1:8b",
            ApiVersion: string.Empty,
            ApiKey: null,
            Enabled: true,
            IsProcessor: false,
            IsShadowProcessor: true,
            SupportsCustomTemperature: true,
            SupportsToolCalling: true);

        var create = await _client.PostAsJsonAsync("/admin/models", request);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ModelConnectionDto>();

        Assert.NotNull(created);
        Assert.True(created.IsShadowProcessor);
        Assert.False(created.IsProcessor);
    }

    [Fact]
    public async Task SeedModels_NoShadowProcessorByDefault()
    {
        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");

        Assert.NotNull(models);
        // None of the seeded models should be shadow processor by default.
        Assert.DoesNotContain(models, m => m.IsShadowProcessor);
    }

    [Fact]
    public async Task SingleShadowProcessorInvariant_PromotingNew_ClearsPrevious()
    {
        // Create two models and mark both as shadow processor — only the last one should hold the flag.
        var nameA = $"shadow-a-{Guid.NewGuid():N}";
        var nameB = $"shadow-b-{Guid.NewGuid():N}";

        async Task<ModelConnectionDto> CreateShadow(string name)
        {
            var r = await _client.PostAsJsonAsync("/admin/models", new ModelConnectionUpsertRequest(
                Name: name, Type: "ollama", Endpoint: "http://localhost:11434",
                ModelName: "llama3.1:8b", ApiVersion: string.Empty, ApiKey: null,
                Enabled: true, IsProcessor: false, IsShadowProcessor: true,
                SupportsCustomTemperature: true, SupportsToolCalling: true));
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<ModelConnectionDto>())!;
        }

        var a = await CreateShadow(nameA);
        var b = await CreateShadow(nameB);

        var models = await _client.GetFromJsonAsync<List<ModelConnectionDto>>("/admin/models");
        Assert.NotNull(models);

        var aUpdated = models.FirstOrDefault(m => m.Id == a.Id);
        var bUpdated = models.FirstOrDefault(m => m.Id == b.Id);

        // b is the latest; a should have been demoted.
        Assert.NotNull(aUpdated);
        Assert.NotNull(bUpdated);
        Assert.False(aUpdated.IsShadowProcessor, "Previous shadow processor should have been demoted.");
        Assert.True(bUpdated.IsShadowProcessor, "New shadow processor should be set.");
    }

    // ── A/B summary endpoint ──────────────────────────────────────────────────

    [Fact]
    public async Task AbClassifier_EmptyTraces_ReturnsSafeDefaults()
    {
        var result = await _client.GetFromJsonAsync<AbClassifierSummaryResponse>(
            "/admin/benchmarks/ab-classifier?limit=10");

        Assert.NotNull(result);
        // With no real traces (test environment), TracesWithShadow should be 0.
        Assert.Equal(0, result.TracesWithShadow);
        Assert.Null(result.AgreementRate);
        Assert.Empty(result.IntentBreakdown);
    }

    [Fact]
    public async Task AbClassifier_Returns200_WithCorrectShape()
    {
        var response = await _client.GetAsync("/admin/benchmarks/ab-classifier");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AbClassifierSummaryResponse>();
        Assert.NotNull(result);
        Assert.True(result.TotalTracesInWindow >= 0);
        Assert.True(result.TracesWithShadow >= 0);
        Assert.True(result.AgreementCount >= 0);
        Assert.True(result.DisagreementCount >= 0);
    }
}
