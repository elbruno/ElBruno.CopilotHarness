using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
    public async Task Models_ReturnsOpenAiListContract()
    {
        var payload = await _client.GetFromJsonAsync<JsonElement>("/v1/models");

        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.Equal("list", payload.GetProperty("object").GetString());
        Assert.True(payload.TryGetProperty("data", out var data));
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.True(data.GetArrayLength() >= 3);

        var firstModel = data.EnumerateArray().First();
        Assert.Equal("model", firstModel.GetProperty("object").GetString());
        Assert.False(string.IsNullOrWhiteSpace(firstModel.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(firstModel.GetProperty("owned_by").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(firstModel.GetProperty("deployment").GetString()));
    }

    [Fact]
    public async Task Responses_ReturnsMinimalOpenAiResponsesEnvelope()
    {
        var response = await _client.PostAsJsonAsync("/v1/responses", new
        {
            instructions = "Be brief.",
            input = "hello responses"
        });

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("x-harness-model-profile"));
        Assert.True(response.Headers.Contains("x-harness-model-deployment"));
        Assert.True(response.Headers.Contains("x-harness-routing-reason"));
        Assert.Equal("response", payload.GetProperty("object").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("output_text").GetString()));
        Assert.True(payload.TryGetProperty("output", out var output));
        Assert.Equal(JsonValueKind.Array, output.ValueKind);
        var firstMessage = output.EnumerateArray().First();
        Assert.Equal("message", firstMessage.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Responses_StreamingRequest_ReturnsOpenAiErrorEnvelope()
    {
        var response = await _client.PostAsJsonAsync("/v1/responses", new
        {
            input = "hello responses",
            stream = true
        });

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(payload.TryGetProperty("error", out var error));
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Contains("/v1/responses", error.GetProperty("message").GetString());
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
    public async Task Models_ReturnsOpenAiListShapeWithConfiguredProfiles()
    {
        var payload = await _client.GetFromJsonAsync<JsonElement>("/v1/models");

        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.Equal("list", payload.GetProperty("object").GetString());

        var models = payload.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, models.ValueKind);

        var modelIds = models.EnumerateArray()
            .Select(model => model.GetProperty("id").GetString())
            .OfType<string>()
            .ToList();

        Assert.Contains("local", modelIds);
        Assert.Contains("small", modelIds);
        Assert.Contains("big", modelIds);
    }

    [Fact]
    public async Task Responses_UsesCompatibilityEnvelopeAndHeaders()
    {
        var response = await _client.PostAsJsonAsync("/v1/responses", new
        {
            model = "big",
            instructions = "You are concise.",
            input = "Hello from responses endpoint."
        });

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("response", payload.GetProperty("object").GetString());
        Assert.Equal("completed", payload.GetProperty("status").GetString());
        var selectedDeployment = response.Headers.GetValues("x-harness-model-deployment").Single();
        Assert.Equal(selectedDeployment, payload.GetProperty("model").GetString());
        Assert.Equal("stubbed assistant reply", payload.GetProperty("output_text").GetString());
        Assert.True(response.Headers.Contains("x-harness-model-profile"));
        Assert.True(response.Headers.Contains("x-harness-model-deployment"));
        Assert.True(response.Headers.Contains("x-harness-routing-reason"));
    }

    [Fact]
    public async Task Responses_InvalidInputType_ReturnsOpenAiErrorEnvelope()
    {
        var response = await _client.PostAsJsonAsync("/v1/responses", new
        {
            input = new { unexpected = true }
        });

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(payload.TryGetProperty("error", out var error));
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Equal("The input field must be a string or an array.", error.GetProperty("message").GetString());
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

    [Fact]
    public async Task ChatCompletions_InvalidJson_ReturnsOpenAiErrorEnvelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(payload.TryGetProperty("error", out var error));
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task ChatCompletions_NonObjectJson_ReturnsOpenAiErrorEnvelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(payload.TryGetProperty("error", out var error));
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Equal("The request body must be a JSON object.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Admin_SetupWizardAndValidationFlow_Works()
    {
        var setupResponse = await _client.PostAsJsonAsync("/admin/setup/wizard", new
        {
            localDeployment = "gpt-local",
            smallDeployment = "gpt-small",
            bigDeployment = "gpt-big",
            defaultProfile = "small",
            generateFirstRules = true
        });

        setupResponse.EnsureSuccessStatusCode();

        var models = await _client.GetFromJsonAsync<JsonElement>("/admin/models");
        var rules = await _client.GetFromJsonAsync<JsonElement>("/admin/rules/basic");
        var validation = await _client.GetFromJsonAsync<JsonElement>("/admin/system/validation");

        Assert.Equal(JsonValueKind.Array, models.ValueKind);
        Assert.True(models.GetArrayLength() >= 3);
        Assert.Equal(JsonValueKind.Object, rules.ValueKind);
        Assert.True(rules.TryGetProperty("defaultProfile", out _));
        Assert.True(validation.TryGetProperty("checks", out var checks));
        Assert.Equal(JsonValueKind.Array, checks.ValueKind);
        var checkNames = checks.EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .OfType<string>()
            .ToList();
        Assert.Contains("setup-completed", checkNames);
    }

    [Fact]
    public async Task Admin_PlaygroundEvaluate_ReturnsRoutingDecision()
    {
        var response = await _client.PostAsJsonAsync("/admin/playground/evaluate", new
        {
            prompt = "route me",
            systemMessage = "you are a specialist",
            stream = false,
            requestedProfile = "big"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.TryGetProperty("profile", out var profile));
        Assert.Equal("big", profile.GetString());
        Assert.True(payload.TryGetProperty("deployment", out var deployment));
        Assert.False(string.IsNullOrWhiteSpace(deployment.GetString()));
        Assert.True(payload.TryGetProperty("reason", out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason.GetString()));
    }
}
