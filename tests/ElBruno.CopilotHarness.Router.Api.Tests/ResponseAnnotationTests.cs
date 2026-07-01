using System.Net.Http.Json;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Api.Telemetry;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

/// <summary>
/// Tests for the demo "routing footer" (response annotation) feature: the footer builder, the
/// runtime ON/OFF toggle endpoint, and end-to-end injection into a plain (non-tool) chat response
/// while being skipped for tool/agentic requests.
/// </summary>
public sealed class ResponseAnnotationTests
{
    // ── Footer builder (unit) ─────────────────────────────────────────────────

    [Fact]
    public void BuildFooter_IncludesRuleModelSourceAndTokens()
    {
        var footer = RoutingAnnotation.BuildFooter(
            profileName: "ollama llama3.1",
            deployment: "llama3.1:8b",
            reason: "Matched rule 'Simple chat'. Local greeting.",
            clientDisplayName: "vscode",
            toolOverride: false,
            usage: new TokenUsage(11, 7, 18, "llama3.1:8b"));

        Assert.StartsWith("\n\n---\n🧭 Copilot Harness", footer);
        Assert.Contains("rule ‘Simple chat’", footer);
        Assert.Contains("ollama llama3.1", footer);
        Assert.Contains("llama3.1:8b", footer);
        Assert.Contains("src: vscode", footer);
        Assert.Contains("18 tok", footer);
        Assert.Contains("(11→7)", footer);
        Assert.DoesNotContain("tool-override", footer);
    }

    [Fact]
    public void BuildFooter_ShowsToolOverride_AndToleratesMissingUsage()
    {
        var footer = RoutingAnnotation.BuildFooter(
            profileName: "foundry gpt-5-mini",
            deployment: "gpt-5-mini",
            reason: "no rule matched",
            clientDisplayName: "copilot-cli",
            toolOverride: true,
            usage: null);

        Assert.Contains("tool-override", footer);
        Assert.Contains("src: copilot-cli", footer);
        Assert.DoesNotContain("rule", footer);
        Assert.DoesNotContain("tok", footer);
    }

    // ── Runtime toggle endpoint ───────────────────────────────────────────────

    [Fact]
    public async Task ResponseAnnotationSetting_DefaultsOff_AndToggles()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<ResponseAnnotationSettingDto>("/admin/settings/response-annotation");
        Assert.NotNull(initial);
        Assert.False(initial!.Enabled);

        var put = await client.PutAsJsonAsync("/admin/settings/response-annotation", new ResponseAnnotationSettingDto(true));
        put.EnsureSuccessStatusCode();
        var updated = await put.Content.ReadFromJsonAsync<ResponseAnnotationSettingDto>();
        Assert.True(updated!.Enabled);

        var afterGet = await client.GetFromJsonAsync<ResponseAnnotationSettingDto>("/admin/settings/response-annotation");
        Assert.True(afterGet!.Enabled);
    }

    [Fact]
    public async Task ResponseAnnotationSetting_SeedsFromConfiguration()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["ResponseAnnotation:Enabled"] = "true"
        });
        var client = factory.CreateClient();

        var setting = await client.GetFromJsonAsync<ResponseAnnotationSettingDto>("/admin/settings/response-annotation");
        Assert.True(setting!.Enabled);
    }

    // ── End-to-end injection ──────────────────────────────────────────────────

    [Fact]
    public async Task PlainChat_WhenEnabled_AppendsFooterToAssistantMessage()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["ResponseAnnotation:Enabled"] = "true"
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "hello there" } }
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("stubbed assistant reply", body);
        // The emoji is \u-escaped in JSON transport; assert on the text portion of the footer.
        Assert.Contains("Copilot Harness", body);
    }

    [Fact]
    public async Task PlainChat_WhenDisabled_DoesNotAppendFooter()
    {
        using var factory = RouterApiWebApplicationFactory.Create();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "hello there" } }
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("stubbed assistant reply", body);
        Assert.DoesNotContain("Copilot Harness", body);
    }

    [Fact]
    public async Task ToolRequest_WhenEnabled_DoesNotAppendFooter()
    {
        using var factory = RouterApiWebApplicationFactory.Create(new Dictionary<string, string?>
        {
            ["ResponseAnnotation:Enabled"] = "true"
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "user", content = "call the tool" } },
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new { name = "get_weather", description = "Get the weather", parameters = new { } }
                }
            }
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        // Agentic/tool calls are excluded so the footer never corrupts the tool-calling loop.
        Assert.DoesNotContain("Copilot Harness", body);
    }
}
