using System.Diagnostics;
using Proxies.Common;
using Proxies.ServiceDefaults;
using Xunit;

namespace Proxies.Common.Tests;

public sealed class GenAiUsageTelemetryTests
{
    static GenAiUsageTelemetryTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == LlmActivity.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        });
    }

    [Fact]
    public void EnsureStreamUsageRequested_AddsIncludeUsage()
    {
        var body = new System.Text.Json.Nodes.JsonObject { ["stream"] = true };

        var changed = GenAiUsageTelemetry.EnsureStreamUsageRequested(body);

        Assert.True(changed);
        Assert.True(body["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void ExtractNonStreamingUsage_ReadsOpenAiUsageShape()
    {
        var response = """
            {
              "model":"gpt-5-mini",
              "usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}
            }
            """;

        var usage = GenAiUsageTelemetry.ExtractNonStreamingUsage(response);

        Assert.NotNull(usage);
        Assert.Equal(11, usage!.InputTokens);
        Assert.Equal(7, usage.OutputTokens);
        Assert.Equal(18, usage.TotalTokens);
        Assert.Equal("gpt-5-mini", usage.ResponseModel);
    }

    [Fact]
    public void ExtractStreamingUsage_ReadsFinalUsageChunk()
    {
        var sse = string.Join('\n',
            "data: {\"choices\":[{\"delta\":{\"content\":\"hel\"}}]}",
            "",
            "data: {\"model\":\"llama3.1:8b\",\"choices\":[],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":3,\"total_tokens\":7}}",
            "",
            "data: [DONE]");

        var usage = GenAiUsageTelemetry.ExtractStreamingUsage(sse);

        Assert.NotNull(usage);
        Assert.Equal(4, usage!.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
        Assert.Equal(7, usage.TotalTokens);
        Assert.Equal("llama3.1:8b", usage.ResponseModel);
    }

    [Theory]
    [InlineData("FoundryProxy", "azure_openai")]
    [InlineData("FoundryLocalProxy", "foundry_local")]
    [InlineData("OllamaProxy", "ollama")]
    public void LlmActivity_SetResult_ProjectsUsageTags_ForAllProxies(string proxyName, string expectedSystem)
    {
        using var activity = LlmActivity.StartChat(proxyName, "requested-model", streaming: true);
        Assert.NotNull(activity);
        var usage = new GenAiUsageRecord(13, 5, 18, "response-model");

        LlmActivity.SetResult(
            activity,
            latencyMs: 42,
            inputTokens: usage.InputTokens,
            outputTokens: usage.OutputTokens,
            totalTokens: usage.TotalTokens,
            responseModel: usage.ResponseModel);

        var tags = activity!.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString());
        Assert.Equal(expectedSystem, tags["gen_ai.system"]);
        Assert.Equal("requested-model", tags["gen_ai.request.model"]);
        Assert.Equal("13", tags["gen_ai.usage.input_tokens"]);
        Assert.Equal("5", tags["gen_ai.usage.output_tokens"]);
        Assert.Equal("18", tags["gen_ai.usage.total_tokens"]);
        Assert.Equal("response-model", tags["gen_ai.response.model"]);
    }
}
