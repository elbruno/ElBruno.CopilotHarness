using System.Net;
using System.Net.Http.Json;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class RouterApiEndpointsTests : IClassFixture<RouterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RouterApiEndpointsTests(RouterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task HealthEndpoints_ReturnSuccessfulStatusCode(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.True(response.IsSuccessStatusCode, $"{path} returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task ChatCompletions_ReturnsExplainabilityHeaders()
    {
        var response = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new[] { new { role = "system", content = "You are a router." }, new { role = "user", content = "hello" } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("x-harness-model-profile"));
        Assert.True(response.Headers.Contains("x-harness-model-deployment"));
        Assert.True(response.Headers.Contains("x-harness-routing-reason"));
    }

    [Fact]
    public async Task ChatCompletions_StreamingPassthrough_WorksAtSmokeLevel()
    {
        var response = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            stream = true,
            messages = new[] { new { role = "user", content = "stream please" } }
        });

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("data: {\"id\":\"evt-1\"}", body);
    }
}
