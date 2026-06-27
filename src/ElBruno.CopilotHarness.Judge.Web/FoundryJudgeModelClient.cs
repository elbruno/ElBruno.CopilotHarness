using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Judge.Web;

public sealed record JudgeModelExecutionRequest(
    PromptRecordEntity PromptRecord,
    BenchmarkModelRequest Model);

public interface IJudgeModelClient
{
    Task<JudgeModelResponse> GenerateAsync(JudgeModelExecutionRequest request, CancellationToken cancellationToken);
}

public sealed class FoundryJudgeModelClient(HttpClient httpClient, IOptions<FoundryOptions> options) : IJudgeModelClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly FoundryOptions _options = options.Value;

    public async Task<JudgeModelResponse> GenerateAsync(JudgeModelExecutionRequest request, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = request.Model.Deployment,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = request.PromptRecord.SystemMessage ?? "You are a benchmark evaluation model."
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = request.PromptRecord.Prompt
                }
            },
            ["temperature"] = 0
        };

        var apiVersion = string.IsNullOrWhiteSpace(request.Model.ApiVersion)
            ? FoundryOptions.DefaultApiVersion
            : request.Model.ApiVersion;

        var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            $"openai/deployments/{Uri.EscapeDataString(request.Model.Deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.Add("api-key", _options.ApiKey);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var startedAt = DateTimeOffset.UtcNow;
        using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var latencyMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Foundry call failed for deployment {request.Model.Deployment}: {(int)response.StatusCode} {body}");
        }

        var json = JsonNode.Parse(body) as JsonObject;
        var responseText = ExtractResponseText(json) ?? body;
        var inputTokens = TryReadInt(json?["usage"]?["prompt_tokens"]) ?? TryReadInt(json?["usage"]?["input_tokens"]);
        var outputTokens = TryReadInt(json?["usage"]?["completion_tokens"]) ?? TryReadInt(json?["usage"]?["output_tokens"]);

        return new JudgeModelResponse(responseText, inputTokens, outputTokens, latencyMs);
    }

    private static string? ExtractResponseText(JsonObject? json)
    {
        if (json is null || json["choices"] is not JsonArray choices || choices.Count == 0 || choices[0] is not JsonObject firstChoice)
        {
            return null;
        }

        var content = firstChoice["message"]?["content"];
        return content is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static int? TryReadInt(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (node is JsonValue longValue && longValue.TryGetValue<long>(out var parsedLong) && parsedLong is >= int.MinValue and <= int.MaxValue)
        {
            return (int)parsedLong;
        }

        return null;
    }
}
