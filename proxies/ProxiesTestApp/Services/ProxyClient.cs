using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxiesTestApp.Services;

public sealed class ChatMessage(string role, string content)
{
    public string Role    { get; }    = role;
    public string Content { get; set; } = content;
}

public sealed record HealthResult(bool Ok, string? Model, string? Proxy, long LatencyMs, string? Error);
public sealed record ModelsResult(bool Ok, IReadOnlyList<string> Models, string? Error);

public sealed class ProxyClient(IHttpClientFactory factory)
{
    // -----------------------------------------------------------------------
    // Health
    // -----------------------------------------------------------------------
    public async Task<HealthResult> GetHealthAsync(string proxyName, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var client = factory.CreateClient(proxyName);
            var json   = await client.GetFromJsonAsync<JsonObject>("/health", ct);
            sw.Stop();
            return new HealthResult(
                Ok: json?["status"]?.GetValue<string>() == "ok",
                Model:     json?["model"]?.GetValue<string>(),
                Proxy:     json?["proxy"]?.GetValue<string>(),
                LatencyMs: sw.ElapsedMilliseconds,
                Error:     null);
        }
        catch (Exception ex)
        {
            return new HealthResult(false, null, null, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Models
    // -----------------------------------------------------------------------
    public async Task<ModelsResult> GetModelsAsync(string proxyName, CancellationToken ct = default)
    {
        try
        {
            var client = factory.CreateClient(proxyName);
            var json   = await client.GetFromJsonAsync<JsonObject>("/v1/models", ct);
            var ids    = json?["data"]?.AsArray()
                             .Select(m => m?["id"]?.GetValue<string>() ?? "")
                             .Where(s => s.Length > 0)
                             .ToList() ?? [];
            return new ModelsResult(true, ids, null);
        }
        catch (Exception ex)
        {
            return new ModelsResult(false, [], ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Non-streaming chat
    // -----------------------------------------------------------------------
    public async Task<(string? Reply, long LatencyMs, string? Error)> ChatAsync(
        string proxyName, string model, IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var client  = factory.CreateClient(proxyName);
            var body    = BuildBody(model, stream: false, messages);
            var res     = await client.PostAsync("/v1/chat/completions", body, ct);
            res.EnsureSuccessStatusCode();
            var json    = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
            var reply   = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
            sw.Stop();
            return (reply.Trim(), sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            return (null, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Streaming chat — yields tokens via IAsyncEnumerable
    // -----------------------------------------------------------------------
    public async IAsyncEnumerable<string> StreamChatAsync(
        string proxyName, string model, IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? connectError = null;
        HttpResponseMessage? res = null;
        try
        {
            var client = factory.CreateClient(proxyName);
            var req    = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = BuildBody(model, stream: true, messages)
            };
            res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex) { connectError = ex.Message; }

        if (connectError is not null)
        {
            yield return $"\n\n⚠ {connectError}";
            yield break;
        }

        await using var stream = await res!.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try   { line = await reader.ReadLineAsync(ct); }
            catch { break; }
            if (line is null) break;
            if (!line.StartsWith("data: ") || line == "data: [DONE]") continue;
            var delta = JsonNode.Parse(line[6..])?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            if (delta is not null) yield return delta;
        }
    }

    // -----------------------------------------------------------------------
    private static StringContent BuildBody(string model, bool stream, IReadOnlyList<ChatMessage> messages)
    {
        var msgs = messages.Select(m => new { role = m.Role, content = m.Content });
        var json = JsonSerializer.Serialize(new { model, stream, messages = msgs });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
