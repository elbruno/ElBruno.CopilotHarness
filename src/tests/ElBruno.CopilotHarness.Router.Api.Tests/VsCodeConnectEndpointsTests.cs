using System.Net;
using System.Text.Json.Nodes;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class VsCodeConnectEndpointsTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public VsCodeConnectEndpointsTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task VsCodeConfig_ReturnsPasteableCustomEndpointConfig()
    {
        var response = await _client.GetAsync("/v1/vscode-config");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        var array = JsonNode.Parse(json)!.AsArray();
        var entry = array[0]!.AsObject();

        Assert.Equal("customendpoint", (string?)entry["vendor"]);
        Assert.Equal("chat-completions", (string?)entry["apiType"]);

        var model = entry["models"]!.AsArray()[0]!.AsObject();
        var url = (string?)model["url"];
        Assert.NotNull(url);
        Assert.EndsWith("/v1/chat/completions", url);
        Assert.Equal("elbruno.copilotharness", (string?)model["id"]);
        Assert.True((bool)model["toolCalling"]!);
    }

    [Fact]
    public async Task VsCodeConfig_HonorsModelIdQuery()
    {
        var json = await _client.GetStringAsync("/v1/vscode-config?modelId=my-router");
        var model = JsonNode.Parse(json)!.AsArray()[0]!.AsObject()["models"]!.AsArray()[0]!.AsObject();

        Assert.Equal("my-router", (string?)model["id"]);
        Assert.Equal("my-router", (string?)model["name"]);
    }

    [Fact]
    public async Task ConnectPage_RendersHtmlWithChatUrl()
    {
        var response = await _client.GetAsync("/connect");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Connect", html);
        Assert.Contains("/v1/chat/completions", html);
        Assert.Contains("customendpoint", html);
    }
}
