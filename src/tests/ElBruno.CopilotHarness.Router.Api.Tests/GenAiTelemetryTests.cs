using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.Telemetry;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Unit tests for GenAI token-usage capture: parsing usage from streaming and non-streaming
/// upstream bodies, injecting <c>stream_options.include_usage</c>, and projecting token facts.
/// </summary>
public sealed class GenAiTelemetryTests
{
    [Fact]
    public void ExtractNonStreamingUsage_ReadsPromptAndCompletionTokens()
    {
        var body = """
            {
              "id": "chatcmpl-1",
              "model": "gpt-5-mini",
              "choices": [ { "message": { "role": "assistant", "content": "hi" } } ],
              "usage": { "prompt_tokens": 11, "completion_tokens": 7, "total_tokens": 18 }
            }
            """;

        var usage = UpstreamResponseForwarder.ExtractNonStreamingUsage(body);

        Assert.NotNull(usage);
        Assert.Equal(11, usage!.InputTokens);
        Assert.Equal(7, usage.OutputTokens);
        Assert.Equal(18, usage.TotalTokens);
        Assert.Equal("gpt-5-mini", usage.ResponseModel);
    }

    [Fact]
    public void ExtractNonStreamingUsage_ReturnsNull_WhenNoUsage()
    {
        var body = """{ "id": "chatcmpl-1", "choices": [] }""";

        Assert.Null(UpstreamResponseForwarder.ExtractNonStreamingUsage(body));
    }

    [Fact]
    public void ExtractStreamingUsage_ReadsFinalUsageChunk()
    {
        var body = string.Join("\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"hel\"}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}",
            "",
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":3,\"total_tokens\":7,\"model\":\"llama3.1:8b\"}}",
            "",
            "data: [DONE]",
            "");

        var usage = UpstreamResponseForwarder.ExtractStreamingUsage(body);

        Assert.NotNull(usage);
        Assert.Equal(4, usage!.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
        Assert.Equal(7, usage.TotalTokens);
    }

    [Fact]
    public void ExtractStreamingUsage_ReturnsNull_WhenNoUsageChunk()
    {
        var body = string.Join("\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}",
            "",
            "data: [DONE]",
            "");

        Assert.Null(UpstreamResponseForwarder.ExtractStreamingUsage(body));
    }

    [Fact]
    public void EnsureStreamUsageRequested_AddsIncludeUsage()
    {
        var payload = new JsonObject { ["stream"] = true };

        var changed = OpenAiApiUtilities.EnsureStreamUsageRequested(payload);

        Assert.True(changed);
        Assert.True(payload["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void EnsureStreamUsageRequested_NoOp_WhenAlreadyRequested()
    {
        var payload = new JsonObject
        {
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true }
        };

        Assert.False(OpenAiApiUtilities.EnsureStreamUsageRequested(payload));
    }

    [Fact]
    public void BuildUpstreamFacts_ProjectsTokenUsage()
    {
        var outcome = new RequestOutcome(
            StatusCode: 200,
            LatencyMs: 42.0,
            Succeeded: true,
            Error: null,
            HadTools: false,
            ToolOverrideApplied: false,
            OverrideReason: null,
            TokensIn: 11,
            TokensOut: 7,
            TokensTotal: 18,
            ResponseModel: "gpt-5-mini");

        var facts = OpenAiApiUtilities.BuildUpstreamFacts(outcome).ToDictionary(f => f.Key, f => f.Value);

        Assert.Equal("11", facts["gen_ai.usage.input_tokens"]);
        Assert.Equal("7", facts["gen_ai.usage.output_tokens"]);
        Assert.Equal("18", facts["gen_ai.usage.total_tokens"]);
        Assert.Equal("gpt-5-mini", facts["gen_ai.response.model"]);
    }

    [Fact]
    public void SystemFor_MapsProviderTypes()
    {
        Assert.Equal("ollama", GenAiTelemetry.SystemFor(Router.Core.ModelProviderType.Ollama));
        Assert.Equal("azure.ai.openai", GenAiTelemetry.SystemFor(Router.Core.ModelProviderType.AzureOpenAI));
    }
}
