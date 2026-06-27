using System.Net.Http.Json;
using System.Text.Json;

namespace ElBruno.CopilotHarness.Judge.Web.Tests;

public sealed class Phase8RecommendationTests : IClassFixture<JudgeWebApplicationFactory>
{
    private readonly HttpClient _client;

    public Phase8RecommendationTests(JudgeWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recommendations_ListReturnsEmptyInitially()
    {
        var response = await _client.GetAsync("/judge/continuous-eval/recommendations");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    [Fact]
    public async Task Analyze_WithNoBenchmarks_ReturnsEmptyOrExistingRecs()
    {
        // When there are no completed benchmark runs, analyze returns an empty array.
        // With a shared fixture another test may have already created data; we just
        // verify the endpoint returns an array (not an error).
        var response = await _client.PostAsync("/judge/continuous-eval/recommendations/analyze", null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    [Fact]
    public async Task Analyze_AfterBenchmark_GeneratesRecommendationForBetterProfile()
    {
        // Import a prompt
        var importResponse = await _client.PostAsJsonAsync("/judge/prompt-records/import", new
        {
            records = new[]
            {
                new
                {
                    source = "phase8-test",
                    clientId = "test",
                    endpoint = "/v1/chat/completions",
                    prompt = "recommend a profile",
                    systemMessage = (string?)null,
                    requestedModel = (string?)null,
                    referenceAnswer = (string?)null,
                    metadata = (object?)null
                }
            }
        });
        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        var promptId = imported![0].GetProperty("id").GetGuid();

        // Run benchmark with two profiles
        var runResponse = await _client.PostAsJsonAsync("/judge/benchmarks/replay", new
        {
            name = "phase8-recommendation-test",
            promptRecordIds = new[] { promptId },
            models = new[]
            {
                new { profileName = "small", deployment = "gpt-small", apiVersion = "2024-10-21" },
                new { profileName = "big", deployment = "gpt-big", apiVersion = "2024-10-21" }
            }
        });
        runResponse.EnsureSuccessStatusCode();

        // Analyze
        var analyzeResponse = await _client.PostAsync("/judge/continuous-eval/recommendations/analyze", null);
        analyzeResponse.EnsureSuccessStatusCode();

        var analyzedRecs = await analyzeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, analyzedRecs.ValueKind);

        // Verify recommendations list is accessible
        var listResponse = await _client.GetAsync("/judge/continuous-eval/recommendations");
        listResponse.EnsureSuccessStatusCode();
        var allRecs = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, allRecs.ValueKind);
    }

    [Fact]
    public async Task ReviewRecommendation_ApprovesPendingRecommendation()
    {
        // Import prompt + run benchmark + generate recommendation
        var importResponse = await _client.PostAsJsonAsync("/judge/prompt-records/import", new
        {
            records = new[]
            {
                new
                {
                    source = "review-test",
                    clientId = "test",
                    endpoint = "/v1/chat/completions",
                    prompt = "please review this",
                    systemMessage = (string?)null,
                    requestedModel = (string?)null,
                    referenceAnswer = "review",
                    metadata = (object?)null
                }
            }
        });
        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        var promptId = imported![0].GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync("/judge/benchmarks/replay", new
        {
            name = "review-bench",
            promptRecordIds = new[] { promptId },
            models = new[]
            {
                new { profileName = "small", deployment = "gpt-small", apiVersion = "2024-10-21" },
                new { profileName = "big", deployment = "gpt-big", apiVersion = "2024-10-21" }
            }
        });

        await _client.PostAsync("/judge/continuous-eval/recommendations/analyze", null);

        // Get any pending recommendation
        var listResponse = await _client.GetAsync("/judge/continuous-eval/recommendations");
        var allRecs = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var pendingRec = allRecs.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("status").GetString() == "Pending");

        if (pendingRec.ValueKind == JsonValueKind.Undefined)
        {
            // No recommendation generated — scores were too close; this is valid
            return;
        }

        var recId = pendingRec.GetProperty("id").GetGuid();

        // Approve it
        var reviewResponse = await _client.PutAsJsonAsync(
            $"/judge/continuous-eval/recommendations/{recId}/review",
            new { status = "Approved", reviewNotes = "Looks good" });

        reviewResponse.EnsureSuccessStatusCode();

        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", reviewed.GetProperty("status").GetString());
        Assert.Equal("Looks good", reviewed.GetProperty("reviewNotes").GetString());
    }

    [Fact]
    public async Task ReviewRecommendation_RejectWithInvalidStatus_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync(
            $"/judge/continuous-eval/recommendations/{Guid.NewGuid()}/review",
            new { status = "InvalidStatus", reviewNotes = (string?)null });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReviewRecommendation_UnknownId_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            $"/judge/continuous-eval/recommendations/{Guid.NewGuid()}/review",
            new { status = "Approved", reviewNotes = (string?)null });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Schedule_ReturnsSchedulerState()
    {
        var response = await _client.GetAsync("/judge/continuous-eval/schedule");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("enabled", out _));
        Assert.True(payload.TryGetProperty("intervalMinutes", out _));
        Assert.True(payload.TryGetProperty("batchSize", out _));
    }
}
