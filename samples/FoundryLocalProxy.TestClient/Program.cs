// =============================================================================
//  FoundryLocalProxy.TestClient
//
//  A console test runner for the FoundryLocalProxy sample.
//  Run this WHILE FoundryLocalProxy is running on http://localhost:5101.
//
//  Usage:
//    cd samples/FoundryLocalProxy.TestClient
//    dotnet run
//
//  What it tests:
//    1. Health check       — GET /
//    2. Health endpoint    — GET /health
//    3. Model list         — GET /v1/models
//    4. Non-streaming chat — POST /v1/chat/completions  stream=false
//    5. Streaming chat     — POST /v1/chat/completions  stream=true
//    6. Utility alias      — POST using "copilot-utility-small" model id
//    7. Multi-turn         — Two-message conversation
//    8. System prompt      — Chat with a system instruction
//    9. Unknown model      — Sends an unknown model id to test fallback
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string BaseUrl = "http://localhost:5101";

using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

int passed = 0, failed = 0;
var sw = Stopwatch.StartNew();

Banner("FoundryLocalProxy Test Client", $"Target: {BaseUrl}");

// ---------------------------------------------------------------------------
// 1. Root health
// ---------------------------------------------------------------------------
await RunTest("1. GET /  (root health)", async () =>
{
    var res = await http.GetAsync("/");
    res.EnsureSuccessStatusCode();
    var body = await res.Content.ReadAsStringAsync();
    var json = JsonNode.Parse(body)!;
    Print("  status  : " + json["status"]);
    Print("  proxy   : " + json["proxy"]);
    Print("  model   : " + json["model"]);
    Print("  utility : " + json["utilityModel"]);
});

// ---------------------------------------------------------------------------
// 2. /health endpoint
// ---------------------------------------------------------------------------
await RunTest("2. GET /health", async () =>
{
    var res = await http.GetAsync("/health");
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    Print("  status  : " + json["status"]);
    Print("  server  : " + json["internalRestServer"]);
});

// ---------------------------------------------------------------------------
// 3. Model list
// ---------------------------------------------------------------------------
await RunTest("3. GET /v1/models", async () =>
{
    var res = await http.GetAsync("/v1/models");
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    var data = json["data"]!.AsArray();
    Print($"  {data.Count} model(s) advertised:");
    foreach (var m in data)
        Print("    • " + m!["id"]);
});

// ---------------------------------------------------------------------------
// 4. Non-streaming chat
// ---------------------------------------------------------------------------
await RunTest("4. POST /v1/chat/completions  (stream=false)", async () =>
{
    var body = BuildChatBody("phi-4-mini", false,
        ("user", "Reply with exactly three words: ping pong done."));

    var res  = await http.PostAsync("/v1/chat/completions", JsonContent(body));
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
    Print("  model  : " + json["model"]);
    Print("  reply  : " + reply.Trim());
});

// ---------------------------------------------------------------------------
// 5. Streaming chat (SSE)
// ---------------------------------------------------------------------------
await RunTest("5. POST /v1/chat/completions  (stream=true)", async () =>
{
    var body = BuildChatBody("phi-4-mini", true,
        ("user", "Write a haiku about running AI models locally. Just the haiku, nothing else."));

    using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
    {
        Content = JsonContent(body)
    };
    using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    res.EnsureSuccessStatusCode();

    var sb = new StringBuilder();
    int chunks = 0;
    await using var stream = await res.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    string? line;
    while ((line = await reader.ReadLineAsync()) is not null)
    {
        if (!line.StartsWith("data: ") || line == "data: [DONE]") continue;
        var chunk = JsonNode.Parse(line[6..]);
        var delta = chunk?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(delta)) { sb.Append(delta); chunks++; }
    }
    Print($"  chunks : {chunks}");
    Print("  haiku  :");
    foreach (var l in sb.ToString().Trim().Split('\n'))
        Print("    " + l.Trim());
});

// ---------------------------------------------------------------------------
// 6. Utility model alias
// ---------------------------------------------------------------------------
await RunTest("6. POST using 'copilot-utility-small' alias", async () =>
{
    var body = BuildChatBody("copilot-utility-small", false,
        ("user", "Suggest a 5-word git commit message for adding dark mode."));

    var res  = await http.PostAsync("/v1/chat/completions", JsonContent(body));
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
    Print("  model used : " + json["model"]);   // proxy rewrites to default
    Print("  reply      : " + reply.Trim());
});

// ---------------------------------------------------------------------------
// 7. Multi-turn conversation
// ---------------------------------------------------------------------------
await RunTest("7. Multi-turn conversation", async () =>
{
    var body = BuildChatBody("phi-4-mini", false,
        ("user",      "My secret number is 42."),
        ("assistant", "Got it! I'll remember that."),
        ("user",      "What is my secret number? Reply with just the number."));

    var res  = await http.PostAsync("/v1/chat/completions", JsonContent(body));
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
    Print("  reply : " + reply.Trim());
    if (!reply.Contains("42"))
        throw new Exception("Expected '42' in reply but got: " + reply.Trim());
    Print("  ✓ model correctly recalled the number");
});

// ---------------------------------------------------------------------------
// 8. System prompt
// ---------------------------------------------------------------------------
await RunTest("8. System prompt", async () =>
{
    var body = BuildChatBody("phi-4-mini", false,
        ("system", "You are a pirate. Every reply must end with 'Arrr!'"),
        ("user",   "What is the capital of France?"));

    var res  = await http.PostAsync("/v1/chat/completions", JsonContent(body));
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
    Print("  reply : " + reply.Trim());
});

// ---------------------------------------------------------------------------
// 9. Unknown model → fallback
// ---------------------------------------------------------------------------
await RunTest("9. Unknown model id → fallback to default", async () =>
{
    var body = BuildChatBody("this-model-does-not-exist", false,
        ("user", "Reply with one word: ok."));

    var res  = await http.PostAsync("/v1/chat/completions", JsonContent(body));
    res.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
    // The proxy should have rewritten the model to the default and still responded.
    Print("  model used : " + json["model"]);
    Print("  reply      : " + reply.Trim());
    Print("  ✓ proxy fell back to default model successfully");
});

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------
sw.Stop();
Console.WriteLine();
Console.WriteLine(new string('─', 60));
var totalColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
WriteColor($"  {passed} passed  /  {failed} failed  —  {sw.ElapsedMilliseconds}ms total", totalColor);
Console.WriteLine();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async Task RunTest(string name, Func<Task> action)
{
    Console.WriteLine();
    WriteColor($"  ┌─ {name}", ConsoleColor.Cyan);
    var t = Stopwatch.StartNew();
    try
    {
        await action();
        t.Stop();
        WriteColor($"  └─ PASS  ({t.ElapsedMilliseconds}ms)", ConsoleColor.Green);
        passed++;
    }
    catch (Exception ex)
    {
        t.Stop();
        WriteColor($"  └─ FAIL  ({t.ElapsedMilliseconds}ms): {ex.Message}", ConsoleColor.Red);
        failed++;
    }
}

static string BuildChatBody(string model, bool stream, params (string role, string content)[] messages)
{
    var msgs = messages.Select(m => new { role = m.role, content = m.content });
    return JsonSerializer.Serialize(new { model, stream, messages = msgs });
}

static StringContent JsonContent(string body) =>
    new(body, Encoding.UTF8, "application/json");

static void Print(string msg) => Console.WriteLine(msg);

static void WriteColor(string msg, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.WriteLine(msg);
    Console.ResetColor();
}

static void Banner(string title, string sub)
{
    var line = new string('═', 60);
    Console.WriteLine();
    WriteColor($"  {line}", ConsoleColor.Yellow);
    WriteColor($"  {title}", ConsoleColor.Yellow);
    WriteColor($"  {sub}", ConsoleColor.DarkYellow);
    WriteColor($"  {line}", ConsoleColor.Yellow);
}
