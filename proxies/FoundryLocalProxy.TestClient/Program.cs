// =============================================================================
//  FoundryLocalProxy — Interactive Console Client
//
//  Run while FoundryLocalProxy (or proxies/AppHost) is running.
//
//  Usage:
//    cd proxies/FoundryLocalProxy.TestClient
//    dotnet run
//
//  Features
//  --------
//  [1] Chat              — single message, streaming by default
//  [2] Conversation      — multi-turn session with history
//  [3] System prompt     — set an instruction, then chat
//  [4] List models       — GET /v1/models
//  [5] Switch model      — pick from models advertised by the proxy
//  [6] Toggle streaming  — flip between stream=true / stream=false
//  [7] Health check      — GET /health
//  [8] Run all tests     — automated suite (9 assertions)
//  [Q] Quit
// =============================================================================

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------
var baseUrl      = "http://localhost:5101";
var currentModel = "phi-4-mini";
var streaming    = true;
var history      = new List<(string role, string content)>();
string? sysprompt = null;

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

// Try to resolve actual default model from /health on startup.
try
{
    var h = JsonNode.Parse(await http.GetStringAsync("/health"));
    if (h?["model"]?.GetValue<string>() is { Length: > 0 } m) currentModel = m;
}
catch { /* proxy might not be up yet; user will see the error on first action */ }

// ---------------------------------------------------------------------------
// Main loop
// ---------------------------------------------------------------------------
while (true)
{
    ShowMenu();
    var key = Console.ReadKey(intercept: true).KeyChar;
    Console.WriteLine();

    switch (char.ToLower(key))
    {
        case '1': await DoChatAsync();         break;
        case '2': await DoConversationAsync(); break;
        case '3': await DoSystemPromptAsync(); break;
        case '4': await DoListModelsAsync();   break;
        case '5': await DoSwitchModelAsync();  break;
        case '6': DoToggleStreaming();          break;
        case '7': await DoHealthAsync();        break;
        case '8': await DoRunTestsAsync();      break;
        case 'q': Quit();                      return;
        default:
            WriteColor("  Unknown command.", ConsoleColor.DarkGray);
            break;
    }
}

// ---------------------------------------------------------------------------
// Menu
// ---------------------------------------------------------------------------
void ShowMenu()
{
    Console.Clear();
    var width = 64;
    var bar   = new string('═', width);
    WriteColor($"\n  {bar}", ConsoleColor.Cyan);
    WriteColor($"  FoundryLocalProxy  —  Interactive Client", ConsoleColor.Cyan);
    WriteColor($"  {baseUrl}  │  model: {currentModel}  │  streaming: {(streaming ? "ON" : "OFF")}",
        ConsoleColor.DarkCyan);
    WriteColor($"  {bar}\n", ConsoleColor.Cyan);

    MenuItem('1', "Chat",             "single message → response");
    MenuItem('2', "Conversation",     "multi-turn session  (/clear /system /exit)");
    MenuItem('3', "System prompt",    "set an instruction, then chat");
    MenuItem('4', "List models",      "GET /v1/models");
    MenuItem('5', "Switch model",     "pick from loaded models");
    MenuItem('6', "Toggle streaming", $"currently: {(streaming ? "ON" : "OFF")}");
    MenuItem('7', "Health check",     "GET /health");
    MenuItem('8', "Run all tests",    "automated suite (9 tests)");
    Console.WriteLine();
    MenuItem('Q', "Quit",             "");
    Console.WriteLine();
    WriteColor("  › ", ConsoleColor.Yellow, newline: false);
}

void MenuItem(char key, string name, string desc)
{
    Console.Write("  ");
    WriteColor($"[{key}]", ConsoleColor.Yellow, newline: false);
    Console.Write($" {name,-18}");
    WriteColor($"  {desc}", ConsoleColor.DarkGray);
}

// ---------------------------------------------------------------------------
// [1] Single chat message
// ---------------------------------------------------------------------------
async Task DoChatAsync()
{
    Section("Chat");
    var msg = Prompt("You");
    if (string.IsNullOrWhiteSpace(msg)) return;

    var msgs = new List<(string role, string content)> { ("user", msg) };
    await SendAndPrintAsync(msgs);
}

// ---------------------------------------------------------------------------
// [2] Multi-turn conversation
// ---------------------------------------------------------------------------
async Task DoConversationAsync()
{
    Section("Conversation  (commands: /clear  /system <text>  /history  /exit)");
    history.Clear();

    while (true)
    {
        var input = Prompt($"You [{history.Count / 2 + 1}]");
        if (string.IsNullOrWhiteSpace(input)) continue;

        if (input.Equals("/exit",    StringComparison.OrdinalIgnoreCase)) break;

        if (input.Equals("/clear",   StringComparison.OrdinalIgnoreCase))
        {
            history.Clear(); sysprompt = null;
            WriteColor("  History cleared.", ConsoleColor.DarkGray);
            continue;
        }

        if (input.Equals("/history", StringComparison.OrdinalIgnoreCase))
        {
            if (history.Count == 0) { WriteColor("  (empty)", ConsoleColor.DarkGray); continue; }
            foreach (var (r, c) in history)
                WriteColor($"  [{r}] {c[..Math.Min(c.Length, 100)]}", ConsoleColor.DarkGray);
            continue;
        }

        if (input.StartsWith("/system ", StringComparison.OrdinalIgnoreCase))
        {
            sysprompt = input[8..].Trim();
            WriteColor($"  System prompt set: \"{sysprompt}\"", ConsoleColor.DarkGray);
            continue;
        }

        history.Add(("user", input));

        var msgs = new List<(string role, string content)>();
        if (!string.IsNullOrEmpty(sysprompt)) msgs.Add(("system", sysprompt));
        msgs.AddRange(history);

        var reply = await SendAndPrintAsync(msgs);
        if (reply is not null) history.Add(("assistant", reply));
    }

    history.Clear();
}

// ---------------------------------------------------------------------------
// [3] System prompt chat
// ---------------------------------------------------------------------------
async Task DoSystemPromptAsync()
{
    Section("System Prompt");
    var sys = Prompt("System instruction");
    if (string.IsNullOrWhiteSpace(sys)) return;
    var msg = Prompt("You");
    if (string.IsNullOrWhiteSpace(msg)) return;

    var msgs = new List<(string role, string content)>
    {
        ("system", sys),
        ("user",   msg)
    };
    await SendAndPrintAsync(msgs);
}

// ---------------------------------------------------------------------------
// [4] List models
// ---------------------------------------------------------------------------
async Task DoListModelsAsync()
{
    Section("GET /v1/models");
    try
    {
        var json = JsonNode.Parse(await http.GetStringAsync("/v1/models"))!;
        var data = json["data"]!.AsArray();
        WriteColor($"  {data.Count} model(s) available:\n", ConsoleColor.White);
        foreach (var m in data)
            WriteColor($"    • {m!["id"]}", ConsoleColor.Green);
    }
    catch (Exception ex) { Error(ex); }
    Pause();
}

// ---------------------------------------------------------------------------
// [5] Switch model
// ---------------------------------------------------------------------------
async Task DoSwitchModelAsync()
{
    Section("Switch Model");
    try
    {
        var json = JsonNode.Parse(await http.GetStringAsync("/v1/models"))!;
        var ids  = json["data"]!.AsArray()
                      .Select(m => m!["id"]!.GetValue<string>())
                      .ToList();

        for (int i = 0; i < ids.Count; i++)
        {
            var marker = ids[i] == currentModel ? " ◀ current" : "";
            WriteColor($"    [{i + 1}] {ids[i]}{marker}",
                ids[i] == currentModel ? ConsoleColor.Yellow : ConsoleColor.White);
        }

        Console.WriteLine();
        WriteColor("  Enter number (or Enter to cancel): ", ConsoleColor.DarkGray, newline: false);
        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out var idx) && idx >= 1 && idx <= ids.Count)
        {
            currentModel = ids[idx - 1];
            WriteColor($"  ✓ Model switched to: {currentModel}", ConsoleColor.Green);
        }
    }
    catch (Exception ex) { Error(ex); }
    Pause();
}

// ---------------------------------------------------------------------------
// [6] Toggle streaming
// ---------------------------------------------------------------------------
void DoToggleStreaming()
{
    streaming = !streaming;
    WriteColor($"  Streaming: {(streaming ? "ON" : "OFF")}", ConsoleColor.Yellow);
    Thread.Sleep(800);
}

// ---------------------------------------------------------------------------
// [7] Health check
// ---------------------------------------------------------------------------
async Task DoHealthAsync()
{
    Section("GET /health");
    try
    {
        var sw   = Stopwatch.StartNew();
        var json = JsonNode.Parse(await http.GetStringAsync("/health"))!;
        sw.Stop();
        WriteColor($"  status  : {json["status"]}", ConsoleColor.Green);
        WriteColor($"  proxy   : {json["proxy"]}", ConsoleColor.White);
        WriteColor($"  model   : {json["model"]}", ConsoleColor.White);
        WriteColor($"  utility : {json["utilityModel"]}", ConsoleColor.White);
        WriteColor($"  server  : {json["internalRestServer"]}", ConsoleColor.White);
        WriteColor($"  latency : {sw.ElapsedMilliseconds}ms", ConsoleColor.DarkGray);
    }
    catch (Exception ex) { Error(ex); }
    Pause();
}

// ---------------------------------------------------------------------------
// [8] Automated test suite
// ---------------------------------------------------------------------------
async Task DoRunTestsAsync()
{
    Section("Automated Test Suite");
    int passed = 0, failed = 0;
    var total = Stopwatch.StartNew();

    await Test("GET /  (root health)", async () =>
    {
        var json = JsonNode.Parse(await http.GetStringAsync("/"))!;
        if (json["status"]?.GetValue<string>() != "ok") throw new Exception("status != ok");
        WriteColor($"    status={json["status"]}  model={json["model"]}", ConsoleColor.DarkGray);
    });

    await Test("GET /health", async () =>
    {
        var json = JsonNode.Parse(await http.GetStringAsync("/health"))!;
        if (json["status"]?.GetValue<string>() != "ok") throw new Exception("status != ok");
        WriteColor($"    server={json["internalRestServer"]}", ConsoleColor.DarkGray);
    });

    await Test("GET /v1/models", async () =>
    {
        var json = JsonNode.Parse(await http.GetStringAsync("/v1/models"))!;
        var data = json["data"]!.AsArray();
        if (data.Count == 0) throw new Exception("no models returned");
        WriteColor($"    {data.Count} model(s): {string.Join(", ", data.Select(m => m!["id"]))}", ConsoleColor.DarkGray);
    });

    await Test("POST chat  stream=false", async () =>
    {
        var body = Body(currentModel, false, ("user", "Reply with exactly three words: ping pong done."));
        var json = JsonNode.Parse(await (await http.PostAsync("/v1/chat/completions", Content(body))).Content.ReadAsStringAsync())!;
        var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
        WriteColor($"    reply: {reply.Trim()}", ConsoleColor.DarkGray);
    });

    await Test("POST chat  stream=true  (SSE)", async () =>
    {
        var body = Body(currentModel, true, ("user", "One word answer: hello."));
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = Content(body) };
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var sb = new StringBuilder();
        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (!line.StartsWith("data: ") || line == "data: [DONE]") continue;
            var delta = JsonNode.Parse(line[6..])?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            if (delta is not null) sb.Append(delta);
        }
        WriteColor($"    reply: {sb.ToString().Trim()}", ConsoleColor.DarkGray);
    });

    await Test("Utility alias  (copilot-utility-small)", async () =>
    {
        var body = Body("copilot-utility-small", false, ("user", "Say: ok"));
        var json = JsonNode.Parse(await (await http.PostAsync("/v1/chat/completions", Content(body))).Content.ReadAsStringAsync())!;
        var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
        WriteColor($"    model rewritten to: {json["model"]}  reply: {reply.Trim()[..Math.Min(reply.Trim().Length,40)]}", ConsoleColor.DarkGray);
    });

    await Test("Multi-turn recall", async () =>
    {
        var body = Body(currentModel, false,
            ("user",      "My secret number is 42."),
            ("assistant", "Got it! I'll remember."),
            ("user",      "What is my secret number? Just the number."));
        var json  = JsonNode.Parse(await (await http.PostAsync("/v1/chat/completions", Content(body))).Content.ReadAsStringAsync())!;
        var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
        if (!reply.Contains("42")) throw new Exception($"Expected '42' in reply, got: {reply.Trim()}");
        WriteColor($"    recalled: {reply.Trim()}", ConsoleColor.DarkGray);
    });

    await Test("System prompt", async () =>
    {
        var body = Body(currentModel, false,
            ("system", "You are a robot. Every reply must start with 'BEEP:'."),
            ("user",   "Say hello."));
        var json  = JsonNode.Parse(await (await http.PostAsync("/v1/chat/completions", Content(body))).Content.ReadAsStringAsync())!;
        var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>().Trim();
        WriteColor($"    reply: {reply[..Math.Min(reply.Length, 80)]}", ConsoleColor.DarkGray);
    });

    await Test("Unknown model → fallback", async () =>
    {
        var body = Body("this-model-does-not-exist", false, ("user", "Reply: ok"));
        var json = JsonNode.Parse(await (await http.PostAsync("/v1/chat/completions", Content(body))).Content.ReadAsStringAsync())!;
        var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
        WriteColor($"    fell back to: {json["model"]}  reply: {reply.Trim()}", ConsoleColor.DarkGray);
    });

    total.Stop();
    Console.WriteLine();
    Console.WriteLine("  " + new string('─', 50));
    var color = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
    WriteColor($"  {passed} passed  /  {failed} failed  —  {total.ElapsedMilliseconds}ms", color);
    Pause();

    async Task Test(string name, Func<Task> action)
    {
        WriteColor($"\n  ┌─ {name}", ConsoleColor.Cyan);
        var sw = Stopwatch.StartNew();
        try
        {
            await action();
            WriteColor($"  └─ PASS  ({sw.ElapsedMilliseconds}ms)", ConsoleColor.Green);
            passed++;
        }
        catch (Exception ex)
        {
            WriteColor($"  └─ FAIL  ({sw.ElapsedMilliseconds}ms): {ex.Message}", ConsoleColor.Red);
            failed++;
        }
    }
}

// ---------------------------------------------------------------------------
// Core: send request and stream/print response; returns assistant text
// ---------------------------------------------------------------------------
async Task<string?> SendAndPrintAsync(List<(string role, string content)> messages)
{
    Console.WriteLine();
    WriteColor($"  {currentModel}  [{(streaming ? "streaming" : "full")}]", ConsoleColor.DarkGray);
    WriteColor("  ─────────────────────────────────────────────", ConsoleColor.DarkGray);

    var bodyStr = Body(currentModel, streaming, messages.ToArray());
    var sw = Stopwatch.StartNew();

    try
    {
        if (streaming)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = Content(bodyStr)
            };
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode)
            {
                Error(new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}"));
                return null;
            }

            var sb   = new StringBuilder();
            int toks = 0;
            Console.Write("  ");
            await using var stream = await res.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (!line.StartsWith("data: ") || line == "data: [DONE]") continue;
                var delta = JsonNode.Parse(line[6..])?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
                if (delta is null) continue;
                Console.Write(delta);
                sb.Append(delta);
                toks++;
            }

            sw.Stop();
            Console.WriteLine();
            WriteColor($"\n  ─────────────────────────────────────────────", ConsoleColor.DarkGray);
            WriteColor($"  {toks} chunks  │  {sw.ElapsedMilliseconds}ms", ConsoleColor.DarkGray);
            return sb.ToString();
        }
        else
        {
            var res  = await http.PostAsync("/v1/chat/completions", Content(bodyStr));
            if (!res.IsSuccessStatusCode)
            {
                Error(new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}"));
                return null;
            }
            var json  = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
            var reply = json["choices"]![0]!["message"]!["content"]!.GetValue<string>().Trim();
            sw.Stop();
            Console.Write("  ");
            Console.WriteLine(reply);
            WriteColor($"\n  ─────────────────────────────────────────────", ConsoleColor.DarkGray);
            WriteColor($"  {sw.ElapsedMilliseconds}ms", ConsoleColor.DarkGray);
            return reply;
        }
    }
    catch (Exception ex) { Error(ex); return null; }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static string Body(string model, bool stream, params (string role, string content)[] messages)
{
    var msgs = messages.Select(m => new { role = m.role, content = m.content });
    return JsonSerializer.Serialize(new { model, stream, messages = msgs });
}

static StringContent Content(string body) =>
    new(body, Encoding.UTF8, "application/json");

static string Prompt(string label)
{
    Console.WriteLine();
    WriteColor($"  {label}: ", ConsoleColor.Yellow, newline: false);
    return Console.ReadLine()?.Trim() ?? "";
}

static void Section(string title)
{
    Console.Clear();
    WriteColor($"\n  ── {title} ─────────────────────────────\n", ConsoleColor.Cyan);
}

static void Pause()
{
    Console.WriteLine();
    WriteColor("  Press any key to return to menu…", ConsoleColor.DarkGray);
    Console.ReadKey(intercept: true);
}

static void Error(Exception ex) =>
    WriteColor($"\n  ✗ Error: {ex.Message}", ConsoleColor.Red);

static void Quit()
{
    Console.Clear();
    WriteColor("\n  Goodbye!\n", ConsoleColor.Cyan);
}

static void WriteColor(string msg, ConsoleColor color, bool newline = true)
{
    Console.ForegroundColor = color;
    if (newline) Console.WriteLine(msg);
    else         Console.Write(msg);
    Console.ResetColor();
}
