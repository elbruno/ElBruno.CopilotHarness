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
    /// When <paramref name="annotationFactory"/> is supplied, the response is rewritten to append the
    /// footer it produces (from the captured usage) to the assistant message — non-streaming bodies are
    /// buffered and rewritten, streaming responses get an extra content chunk injected before <c>[DONE]</c>.
    /// </summary>
    public static async Task<TokenUsage?> ForwardAsync(
        HttpResponseMessage upstream,
        HttpResponse response,
        bool stream,
        bool captureUsage,
        CancellationToken cancellationToken,
        Func<TokenUsage?, string>? annotationFactory = null)
    {
        await using var source = await upstream.Content.ReadAsStreamAsync(cancellationToken);

        if (annotationFactory is not null)
        {
            return stream
                ? await ForwardStreamingAnnotatedAsync(source, response, annotationFactory, cancellationToken)
                : await ForwardNonStreamingAnnotatedAsync(source, response, annotationFactory, cancellationToken);
        }

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

    /// <summary>Buffers a non-streaming body, appends the footer to the assistant message, fixes Content-Length, and writes it.</summary>
    private static async Task<TokenUsage?> ForwardNonStreamingAnnotatedAsync(
        Stream source,
        HttpResponse response,
        Func<TokenUsage?, string> annotationFactory,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        var original = memory.ToArray();
        var text = Encoding.UTF8.GetString(original);
        var usage = ExtractNonStreamingUsage(text);

        var toWrite = original;
        try
        {
            if (JsonNode.Parse(text) is JsonObject root
                && root["choices"] is JsonArray choices
                && choices.Count > 0
                && choices[0] is JsonObject choice
                && choice["message"] is JsonObject message)
            {
                var existing = message["content"]?.GetValue<string>() ?? string.Empty;
                message["content"] = existing + annotationFactory(usage);
                toWrite = Encoding.UTF8.GetBytes(root.ToJsonString());
            }
        }
        catch (JsonException)
        {
            // Unexpected shape — forward the original body untouched.
            toWrite = original;
        }

        // CopyHeaders already copied the upstream Content-Length; overwrite before the first body write.
        response.Headers.ContentLength = toWrite.Length;
        await response.Body.WriteAsync(toWrite, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        return usage;
    }

    /// <summary>Streams SSE line-by-line, tracking id/model/usage, and injects a footer content chunk before <c>[DONE]</c>.</summary>
    private static async Task<TokenUsage?> ForwardStreamingAnnotatedAsync(
        Stream source,
        HttpResponse response,
        Func<TokenUsage?, string> annotationFactory,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(source, Encoding.UTF8);
        TokenUsage? usage = null;
        string? id = null;
        string? model = null;
        var injected = false;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = trimmed["data:".Length..].Trim();
                if (string.Equals(json, "[DONE]", StringComparison.Ordinal))
                {
                    injected = await InjectFooterChunkAsync(response, id, model, usage, annotationFactory, injected, cancellationToken);
                }
                else if (json.Length > 0)
                {
                    try
                    {
                        if (JsonNode.Parse(json) is JsonObject obj)
                        {
                            id ??= obj["id"]?.GetValue<string>();
                            model ??= obj["model"]?.GetValue<string>();
                            var chunkUsage = ReadUsage(obj);
                            if (chunkUsage is not null)
                            {
                                usage = chunkUsage;
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Best-effort parse; still forward the line verbatim below.
                    }
                }
            }

            await WriteLineAsync(response, line, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        // Some providers end the stream without an explicit [DONE]; inject at end if we still can.
        if (!injected)
        {
            await InjectFooterChunkAsync(response, id, model, usage, annotationFactory, injected, cancellationToken);
        }

        return usage;
    }

    private static async Task<bool> InjectFooterChunkAsync(
        HttpResponse response,
        string? id,
        string? model,
        TokenUsage? usage,
        Func<TokenUsage?, string> annotationFactory,
        bool alreadyInjected,
        CancellationToken cancellationToken)
    {
        if (alreadyInjected || string.IsNullOrEmpty(id))
        {
            return alreadyInjected;
        }

        var chunk = BuildFooterChunk(id!, model, annotationFactory(usage));
        await WriteLineAsync(response, "data: " + chunk, cancellationToken);
        await WriteLineAsync(response, string.Empty, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        return true;
    }

    private static string BuildFooterChunk(string id, string? model, string footer)
    {
        var chunk = new JsonObject
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["model"] = model,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = new JsonObject { ["content"] = footer },
                    ["finish_reason"] = null
                }
            }
        };
        return chunk.ToJsonString();
    }

    private static Task WriteLineAsync(HttpResponse response, string line, CancellationToken cancellationToken)
        => response.Body.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken).AsTask();

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
