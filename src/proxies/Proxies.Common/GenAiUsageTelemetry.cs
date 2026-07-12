using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proxies.Common;

/// <summary>
/// Shared contract for parsed GenAI token usage emitted by OpenAI-compatible providers.
/// </summary>
/// <param name="InputTokens">Prompt/input token count (when available).</param>
/// <param name="OutputTokens">Completion/output token count (when available).</param>
/// <param name="TotalTokens">Total token count (when available).</param>
/// <param name="ResponseModel">Model id reported by the upstream response/chunk.</param>
public sealed record GenAiUsageRecord(
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    string? ResponseModel);

/// <summary>
/// OpenAI-compatible helpers for stream usage requests and usage parsing.
/// </summary>
public static class GenAiUsageTelemetry
{
    /// <summary>
    /// Ensures <c>stream_options.include_usage=true</c> for streaming requests.
    /// Returns <c>true</c> when the payload was changed.
    /// </summary>
    public static bool EnsureStreamUsageRequested(JsonObject requestBody)
    {
        ArgumentNullException.ThrowIfNull(requestBody);

        if (requestBody["stream_options"] is JsonObject existing)
        {
            if (existing["include_usage"]?.GetValue<bool>() == true)
            {
                return false;
            }

            existing["include_usage"] = true;
            return true;
        }

        requestBody["stream_options"] = new JsonObject { ["include_usage"] = true };
        return true;
    }

    /// <summary>
    /// Parses usage from a non-streaming JSON response body.
    /// </summary>
    public static GenAiUsageRecord? ExtractNonStreamingUsage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body) is JsonObject obj
                ? TryExtractUsageFromResponseObject(obj)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scans SSE body lines from tail to head for the final usage chunk.
    /// </summary>
    public static GenAiUsageRecord? ExtractStreamingUsage(string sseBody)
    {
        if (string.IsNullOrWhiteSpace(sseBody))
        {
            return null;
        }

        var lines = sseBody.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var usage = TryExtractUsageFromSseDataLine(lines[i]);
            if (usage is not null)
            {
                return usage;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses usage from one SSE <c>data:</c> line (returns null when no usage is present).
    /// </summary>
    public static GenAiUsageRecord? TryExtractUsageFromSseDataLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        var json = trimmed["data:".Length..].Trim();
        if (json.Length == 0 || string.Equals(json, "[DONE]", StringComparison.Ordinal))
        {
            return null;
        }

        if (!json.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject obj
                ? TryExtractUsageFromResponseObject(obj)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses usage from an OpenAI-compatible response/chunk JSON object.
    /// </summary>
    public static GenAiUsageRecord? TryExtractUsageFromResponseObject(JsonObject responseOrChunk)
    {
        if (responseOrChunk["usage"] is not JsonObject usage)
        {
            return null;
        }

        var input = TryReadInt(usage["prompt_tokens"]) ?? TryReadInt(usage["input_tokens"]);
        var output = TryReadInt(usage["completion_tokens"]) ?? TryReadInt(usage["output_tokens"]);
        var total = TryReadInt(usage["total_tokens"]);
        if (input is null && output is null && total is null)
        {
            return null;
        }

        total ??= (input ?? 0) + (output ?? 0);
        return new GenAiUsageRecord(
            input,
            output,
            total,
            responseOrChunk["model"]?.GetValue<string>());
    }

    private static int? TryReadInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue) &&
            longValue <= int.MaxValue &&
            longValue >= int.MinValue)
        {
            return (int)longValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            var rounded = Convert.ToInt32(Math.Truncate(doubleValue));
            return rounded;
        }

        if (value.TryGetValue<string>(out var stringValue) &&
            int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
