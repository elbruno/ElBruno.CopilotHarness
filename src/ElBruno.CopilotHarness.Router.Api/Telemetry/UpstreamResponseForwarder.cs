using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Router.Api.Telemetry;

/// <summary>
/// Forwards an upstream chat-completion response body to the client while best-effort capturing
/// token usage from the payload. Streaming responses are teed (bytes flow to the client immediately,
/// a bounded tail is scanned for the final usage chunk); non-streaming responses are buffered and parsed.
/// Capture never alters the bytes sent to the client and never throws into the proxy path.
/// </summary>
public static class UpstreamResponseForwarder
{
    // Keep only the tail of the streamed body in memory; the OpenAI usage chunk is always last.
    private const int MaxTailBytes = 128 * 1024;

    /// <summary>
    /// Copies <paramref name="upstream"/>'s content to <paramref name="response"/>. When
    /// <paramref name="captureUsage"/> is true, returns the parsed <see cref="TokenUsage"/> (or null
    /// when none was present); when false, copies straight through and returns null.
    /// </summary>
    public static async Task<TokenUsage?> ForwardAsync(
        HttpResponseMessage upstream,
        HttpResponse response,
        bool stream,
        bool captureUsage,
        CancellationToken cancellationToken)
    {
        await using var source = await upstream.Content.ReadAsStreamAsync(cancellationToken);

        if (!captureUsage)
        {
            await source.CopyToAsync(response.Body, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
            return null;
        }

        var buffer = new byte[8192];
        var tail = new List<byte>(Math.Min(MaxTailBytes, 64 * 1024));
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (stream)
            {
                // Preserve real-time delivery for SSE clients (VS Code Copilot).
                await response.Body.FlushAsync(cancellationToken);
            }

            for (var i = 0; i < read; i++)
            {
                tail.Add(buffer[i]);
            }

            if (tail.Count > MaxTailBytes)
            {
                tail.RemoveRange(0, tail.Count - MaxTailBytes);
            }
        }

        await response.Body.FlushAsync(cancellationToken);

        try
        {
            var text = Encoding.UTF8.GetString(tail.ToArray());
            return stream ? ExtractStreamingUsage(text) : ExtractNonStreamingUsage(text);
        }
        catch
        {
            // Telemetry is best-effort; never let usage parsing affect the client.
            return null;
        }
    }

    /// <summary>Parses usage from a single non-streaming JSON completion body.</summary>
    public static TokenUsage? ExtractNonStreamingUsage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(body);
            return node is JsonObject obj ? ReadUsage(obj) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Scans SSE <c>data:</c> lines from the end for the final chunk carrying a usage object.</summary>
    public static TokenUsage? ExtractStreamingUsage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var lines = body.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line["data:".Length..].Trim();
            if (json.Length == 0 || string.Equals(json, "[DONE]", StringComparison.Ordinal))
            {
                continue;
            }

            if (!json.Contains("\"usage\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(json) is JsonObject obj)
                {
                    var usage = ReadUsage(obj);
                    if (usage is not null)
                    {
                        return usage;
                    }
                }
            }
            catch (JsonException)
            {
                // Chunk may be truncated at the tail boundary; keep scanning earlier lines.
            }
        }

        return null;
    }

    private static TokenUsage? ReadUsage(JsonObject root)
    {
        if (root["usage"] is not JsonObject usage)
        {
            return null;
        }

        var input = ReadLong(usage, "prompt_tokens") ?? ReadLong(usage, "input_tokens");
        var output = ReadLong(usage, "completion_tokens") ?? ReadLong(usage, "output_tokens");
        var total = ReadLong(usage, "total_tokens");
        if (input is null && output is null && total is null)
        {
            return null;
        }

        total ??= (input ?? 0) + (output ?? 0);
        var model = root["model"]?.GetValue<string>();
        return new TokenUsage(input, output, total, model);
    }

    private static long? ReadLong(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return (long)d;
        }

        return null;
    }
}
