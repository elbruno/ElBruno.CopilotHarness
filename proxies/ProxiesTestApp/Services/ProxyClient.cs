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
public sealed record ModelInfo(string Id, bool Loaded);
public sealed record ModelsResult(bool Ok, IReadOnlyList<string> Models, string? Error, IReadOnlyList<ModelInfo>? ModelInfos = null);

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
            var items  = json?["data"]?.AsArray()
                             .Select(m => new ModelInfo(
                                 Id:     m?["id"]?.GetValue<string>() ?? "",
                                 Loaded: m?["loaded"]?.GetValue<bool>() ?? true))  // non-FoundryLocal proxies don't set it → treat as loaded
                             .Where(m => m.Id.Length > 0)
                             .ToList() ?? [];
            var ids = items.Select(m => m.Id).ToList();
            return new ModelsResult(true, ids, null, items);
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

    // -----------------------------------------------------------------------
    // Foundry Local model management
    // -----------------------------------------------------------------------

    public sealed record ModelStatusEntry(
        string Alias, bool Loaded, bool Cached,
        double ParamsB, double SizeGb, int RamGb, string Description);

    public sealed record ModelStatusResult(bool Ok, IReadOnlyList<ModelStatusEntry> Models, string? Error);

    public sealed record LoadProgress(
        string Stage, int Progress, string Message,
        string? Alias = null, string? ModelId = null);

    public async Task<ModelStatusResult> GetModelStatusAsync(string proxyName, CancellationToken ct = default)
    {
        try
        {
            var client = factory.CreateClient(proxyName);
            var json   = await client.GetFromJsonAsync<JsonObject>("/v1/models/status", ct);
            var models = json?["models"]?.AsArray().Select(m => new ModelStatusEntry(
                Alias:       m?["alias"]?.GetValue<string>()       ?? "",
                Loaded:      m?["loaded"]?.GetValue<bool>()        ?? false,
                Cached:      m?["cached"]?.GetValue<bool>()        ?? false,
                ParamsB:     m?["params_b"]?.GetValue<double>()    ?? 0,
                SizeGb:      m?["size_gb"]?.GetValue<double>()     ?? 0,
                RamGb:       m?["ram_gb"]?.GetValue<int>()         ?? 0,
                Description: m?["description"]?.GetValue<string>() ?? ""
            )).ToList() ?? [];
            return new ModelStatusResult(true, models, null);
        }
        catch (Exception ex) { return new ModelStatusResult(false, [], ex.Message); }
    }

    public async IAsyncEnumerable<LoadProgress> LoadModelAsync(
        string proxyName, string alias,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = factory.CreateClient(proxyName);
        string? connectError = null;
        HttpResponseMessage? resp = null;
        try
        {
            resp = await client.PostAsync($"/v1/models/{Uri.EscapeDataString(alias)}/load",
                new StringContent("", Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) { connectError = ex.Message; }

        if (connectError is not null)
        {
            yield return new LoadProgress("error", 0, connectError);
            yield break;
        }

        if (resp!.StatusCode == System.Net.HttpStatusCode.Conflict ||
            resp.StatusCode  == System.Net.HttpStatusCode.NotFound  ||
            resp.StatusCode  == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            var msg  = JsonNode.Parse(body)?["error"]?.GetValue<string>() ?? body;
            yield return new LoadProgress("error", 0, msg);
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var json = JsonNode.Parse(line[6..]);
            if (json is null) continue;
            yield return new LoadProgress(
                Stage:    json["stage"]?.GetValue<string>()    ?? "",
                Progress: json["progress"]?.GetValue<int>()    ?? 0,
                Message:  json["message"]?.GetValue<string>()  ?? "",
                Alias:    json["alias"]?.GetValue<string>(),
                ModelId:  json["model_id"]?.GetValue<string>());
        }
    }

    public async Task<(bool Ok, string Message)> UnloadModelAsync(string proxyName, string alias, CancellationToken ct = default)
    {
        try
        {
            var client = factory.CreateClient(proxyName);
            var resp   = await client.PostAsync($"/v1/models/{Uri.EscapeDataString(alias)}/unload",
                new StringContent("", Encoding.UTF8, "application/json"), ct);
            var json   = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (resp.IsSuccessStatusCode)
                return (true, json?["message"]?.GetValue<string>() ?? "Unloaded.");
            return (false, json?["error"]?.GetValue<string>() ?? resp.ReasonPhrase ?? "Error");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Ok, string Message)> DeleteModelAsync(string proxyName, string alias, CancellationToken ct = default)
    {
        try
        {
            var client = factory.CreateClient(proxyName);
            var resp   = await client.DeleteAsync($"/v1/models/{Uri.EscapeDataString(alias)}", ct);
            var json   = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (resp.IsSuccessStatusCode)
                return (true, json?["message"]?.GetValue<string>() ?? "Deleted.");
            return (false, json?["error"]?.GetValue<string>() ?? resp.ReasonPhrase ?? "Error");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
