using System.Net.Http.Json;
using System.Text.Json;

namespace ElBruno.CopilotHarness.Judge.Web.Tests;

public sealed class JudgeEndpointsTests : IClassFixture<JudgeWebApplicationFactory>
{
    private readonly HttpClient _client;

    public JudgeEndpointsTests(JudgeWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PromptRecords_ImportAndList_Work()
    {
        var response = await _client.PostAsJsonAsync("/judge/prompt-records/import", new
        {
            records = new[]
            {
                new
                {
                    source = "router-trace",
                    clientId = "vscode",
                    endpoint = "/v1/chat/completions",
                    prompt = "write a summary",
                    systemMessage = "you are a helpful assistant",
                    requestedModel = "gpt-5-mini",
                    referenceAnswer = "summary",
                    metadata = new { traceId = "trace-1" }
                }
            }
        });

        response.EnsureSuccessStatusCode();

        var records = await _client.GetFromJsonAsync<JsonElement[]>("/judge/prompt-records");
        Assert.NotNull(records);
        Assert.Single(records!);
        Assert.Equal("router-trace", records[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task ReplayBenchmark_StoresResultsAndReport()
    {
        var importResponse = await _client.PostAsJsonAsync("/judge/prompt-records/import", new
        {
            records = new[]
            {
                new
                {
                    source = "router-trace",
                    clientId = "copilot-cli",
                    endpoint = "/v1/responses",
                    prompt = "describe the difference",
                    systemMessage = "be concise",
                    requestedModel = "small",
                    referenceAnswer = "difference"
                }
            }
        });

        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        var promptRecordId = imported![0].GetProperty("id").GetGuid();

        var runResponse = await _client.PostAsJsonAsync("/judge/benchmarks/replay", new
        {
            name = "replay-one",
            promptRecordIds = new[] { promptRecordId },
            models = new[]
            {
                new { profileName = "small", deployment = "gpt-small", apiVersion = "2024-10-21" },
                new { profileName = "big", deployment = "gpt-big", apiVersion = "2024-10-21" }
            }
        });

        runResponse.EnsureSuccessStatusCode();
        var run = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = run!.GetProperty("id").GetGuid();

        var reportResponse = await _client.GetAsync($"/judge/reports/{runId}");
        reportResponse.EnsureSuccessStatusCode();
        var report = await reportResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Completed", report!.GetProperty("run").GetProperty("status").GetString());
        var prompts = report.GetProperty("prompts").EnumerateArray().ToList();
        Assert.Single(prompts);
        var results = prompts[0].GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.GetProperty("isWinner").GetBoolean());
        Assert.Contains(report.GetProperty("models").EnumerateArray(), model => model.GetProperty("profileName").GetString() == "big");
    }

    [Fact]
    public async Task ManualBenchmark_ReturnsRunSummary()
    {
        var response = await _client.PostAsJsonAsync("/judge/benchmarks/manual", new
        {
            name = "manual-check",
            prompt = "summarize the plan",
            systemMessage = "be helpful",
            referenceAnswer = "plan",
            models = new[]
            {
                new { profileName = "local", deployment = "gpt-local", apiVersion = "2024-10-21" }
            }
        });

        response.EnsureSuccessStatusCode();
        var run = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("manual-check", run!.GetProperty("name").GetString());
        Assert.Equal("manual", run.GetProperty("mode").GetString());
        Assert.Equal("Completed", run.GetProperty("status").GetString());
    }
}
