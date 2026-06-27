using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class Phase8ContinuousEvaluationTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public Phase8ContinuousEvaluationTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Shadow Routing ───────────────────────────────────────────────────────

    [Fact]
    public async Task ShadowConfig_GetReturnsDefaultConfig()
    {
        var response = await _client.GetAsync("/admin/phase8/shadow/config");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("enabled", out _));
        Assert.True(payload.TryGetProperty("shadowProfile", out _));
        Assert.True(payload.TryGetProperty("samplingRate", out _));
    }

    [Fact]
    public async Task ShadowConfig_UpdateAndRetrieve()
    {
        var update = new { enabled = true, shadowProfile = "big", samplingRate = 0.25 };
        var putResponse = await _client.PutAsJsonAsync("/admin/phase8/shadow/config", update);
        putResponse.EnsureSuccessStatusCode();

        var getResponse = await _client.GetAsync("/admin/phase8/shadow/config");
        getResponse.EnsureSuccessStatusCode();

        var payload = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("enabled").GetBoolean());
        Assert.Equal("big", payload.GetProperty("shadowProfile").GetString());
        Assert.Equal(0.25, payload.GetProperty("samplingRate").GetDouble(), 2);
    }

    [Fact]
    public async Task ShadowResults_ReturnsEmptyListInitially()
    {
        var response = await _client.GetAsync("/admin/phase8/shadow/results?count=10");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    // ─── Rule Confidence ──────────────────────────────────────────────────────

    [Fact]
    public async Task RuleConfidence_RecordAndRetrieve()
    {
        var ruleKey = "test-rule-" + Guid.NewGuid().ToString("N");

        await _client.PostAsync($"/admin/phase8/rules/confidence/{ruleKey}/record?successful=true", null);
        await _client.PostAsync($"/admin/phase8/rules/confidence/{ruleKey}/record?successful=true", null);
        await _client.PostAsync($"/admin/phase8/rules/confidence/{ruleKey}/record?successful=false", null);

        var response = await _client.GetAsync($"/admin/phase8/rules/confidence/{ruleKey}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ruleKey, payload.GetProperty("ruleKey").GetString());
        Assert.Equal(3, payload.GetProperty("totalInvocations").GetInt32());
        Assert.Equal(2, payload.GetProperty("successfulInvocations").GetInt32());

        var confidence = payload.GetProperty("confidenceScore").GetDouble();
        Assert.Equal(2.0 / 3.0, confidence, 3);
    }

    [Fact]
    public async Task RuleConfidence_UnknownRuleKey_Returns404()
    {
        var response = await _client.GetAsync("/admin/phase8/rules/confidence/nonexistent-rule-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RuleConfidence_ListAllScores_ReturnsArray()
    {
        var response = await _client.GetAsync("/admin/phase8/rules/confidence");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    // ─── Benchmarks ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BenchmarkRun_CreateListGet()
    {
        var create = new
        {
            name = "Phase8 Test Run",
            description = "Integration test benchmark",
            profiles = new[] { "small", "big" },
            items = new[]
            {
                new { itemId = "item-1", prompt = "What is 2+2?", systemMessage = (string?)null },
                new { itemId = "item-2", prompt = "Write hello world in C#", systemMessage = "Be concise" }
            }
        };

        var postResponse = await _client.PostAsJsonAsync("/admin/phase8/benchmark/runs", create);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var created = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = created.GetProperty("runId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(runId));
        Assert.Equal("Phase8 Test Run", created.GetProperty("name").GetString());
        Assert.Equal("pending", created.GetProperty("status").GetString());
        Assert.Equal(4, created.GetProperty("totalItems").GetInt32());

        var getResponse = await _client.GetAsync($"/admin/phase8/benchmark/runs/{runId}");
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(runId, fetched.GetProperty("runId").GetString());

        var listResponse = await _client.GetAsync("/admin/phase8/benchmark/runs?page=1&pageSize=10");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.True(list.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task BenchmarkRun_RecordResultsAndRetrieve()
    {
        var create = new
        {
            name = "Result Test",
            description = "",
            profiles = new[] { "small" },
            items = new[] { new { itemId = "i1", prompt = "Test prompt", systemMessage = (string?)null } }
        };

        var postResponse = await _client.PostAsJsonAsync("/admin/phase8/benchmark/runs", create);
        var created = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = created.GetProperty("runId").GetString()!;

        var result = new
        {
            itemId = "i1",
            profile = "small",
            deployment = "gpt-4o-mini",
            promptHash = "abcd1234",
            latencyMs = 312.5,
            promptTokens = 10,
            completionTokens = 25,
            statusCode = 200,
            judgeVerdict = "pass",
            judgeScore = 0.92,
            metricsJson = "{}"
        };

        var resultPost = await _client.PostAsJsonAsync($"/admin/phase8/benchmark/runs/{runId}/results", result);
        Assert.Equal(HttpStatusCode.Created, resultPost.StatusCode);

        var resultsGet = await _client.GetAsync($"/admin/phase8/benchmark/runs/{runId}/results");
        resultsGet.EnsureSuccessStatusCode();
        var results = await resultsGet.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("i1", results[0].GetProperty("itemId").GetString());
    }

    // ─── Human Approval Workflow ──────────────────────────────────────────────

    [Fact]
    public async Task Approvals_CreateListReview()
    {
        var create = new
        {
            changeType = "rule-update",
            title = "Increase big prompt threshold",
            description = "Change from 2500 to 3000 chars",
            payloadJson = "{\"BigPromptCharacterThreshold\":3000}",
            expiresAtUtc = (DateTimeOffset?)null
        };

        var postResponse = await _client.PostAsJsonAsync("/admin/phase8/approvals", create);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var created = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var approvalId = created.GetProperty("approvalId").GetString()!;
        Assert.Equal("pending", created.GetProperty("status").GetString());

        var getResponse = await _client.GetAsync($"/admin/phase8/approvals/{approvalId}");
        getResponse.EnsureSuccessStatusCode();

        var listResponse = await _client.GetAsync("/admin/phase8/approvals?status=pending");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 1);

        var review = new { approved = true, reviewedBy = "engineer@test.com", reviewNotes = "LGTM" };
        var reviewResponse = await _client.PutAsJsonAsync($"/admin/phase8/approvals/{approvalId}/review", review);
        reviewResponse.EnsureSuccessStatusCode();

        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("approved", reviewed.GetProperty("status").GetString());
        Assert.Equal("engineer@test.com", reviewed.GetProperty("reviewedBy").GetString());
    }

    [Fact]
    public async Task Approvals_DoubleReview_ReturnsConflict()
    {
        var create = new
        {
            changeType = "test",
            title = "Double review test",
            description = "",
            payloadJson = "{}",
            expiresAtUtc = (DateTimeOffset?)null
        };

        var postResponse = await _client.PostAsJsonAsync("/admin/phase8/approvals", create);
        var created = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var approvalId = created.GetProperty("approvalId").GetString()!;

        var review = new { approved = true, reviewedBy = "a@b.com", reviewNotes = (string?)null };
        await _client.PutAsJsonAsync($"/admin/phase8/approvals/{approvalId}/review", review);

        var conflict = await _client.PutAsJsonAsync($"/admin/phase8/approvals/{approvalId}/review", review);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    // ─── Team & Project Profiles ──────────────────────────────────────────────

    [Fact]
    public async Task TeamProfiles_UpsertGetListDelete()
    {
        var teamId = "team-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var upsert = new
        {
            displayName = "Backend Engineers",
            defaultProfile = "big",
            rulesJson = "{\"preferBig\":true}",
            enabled = true
        };

        var putResponse = await _client.PutAsJsonAsync($"/admin/phase8/teams/{teamId}", upsert);
        putResponse.EnsureSuccessStatusCode();
        var team = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(teamId, team.GetProperty("teamId").GetString());
        Assert.Equal("big", team.GetProperty("defaultProfile").GetString());

        var getResponse = await _client.GetAsync($"/admin/phase8/teams/{teamId}");
        getResponse.EnsureSuccessStatusCode();

        var listResponse = await _client.GetAsync("/admin/phase8/teams");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 1);

        var deleteResponse = await _client.DeleteAsync($"/admin/phase8/teams/{teamId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var notFound = await _client.GetAsync($"/admin/phase8/teams/{teamId}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task ProjectProfiles_UpsertGetListDelete()
    {
        var teamId = "team-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var projectId = "proj-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var upsert = new
        {
            teamId,
            displayName = "Copilot Harness Core",
            defaultProfile = "small",
            rulesJson = "{}",
            enabled = true
        };

        var putResponse = await _client.PutAsJsonAsync($"/admin/phase8/projects/{projectId}", upsert);
        putResponse.EnsureSuccessStatusCode();
        var proj = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(projectId, proj.GetProperty("projectId").GetString());
        Assert.Equal(teamId, proj.GetProperty("teamId").GetString());

        var listByTeam = await _client.GetAsync($"/admin/phase8/projects?teamId={teamId}");
        listByTeam.EnsureSuccessStatusCode();
        var list = await listByTeam.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetArrayLength());

        var deleteResponse = await _client.DeleteAsync($"/admin/phase8/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
