using System.Net;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class ChatCompletionsProviderTests
{
    [Fact]
    public void Factory_SelectsProvider_ByType()
    {
        var azure = new AzureFoundryChatCompletionsProvider(new StubHttpClientFactory(new CapturingHandler()), CreateFoundryOptions());
        var ollama = new OllamaChatCompletionsProvider(new StubHttpClientFactory(new CapturingHandler()));
        var factory = new ChatCompletionsProviderFactory(new IChatCompletionsProvider[] { azure, ollama });

        Assert.Same(azure, factory.GetProvider(new ModelProfileOptions { Type = ModelProviderType.AzureOpenAI }));
        Assert.Same(ollama, factory.GetProvider(new ModelProfileOptions { Type = ModelProviderType.Ollama }));
    }

    [Fact]
    public void Factory_UnknownType_Throws()
    {
        var factory = new ChatCompletionsProviderFactory(Array.Empty<IChatCompletionsProvider>());

        Assert.Throws<InvalidOperationException>(() => factory.GetProvider(new ModelProfileOptions { Type = ModelProviderType.Ollama }));
    }

    [Fact]
    public async Task AzureProvider_BuildsAzureOpenAiRequest()
    {
        var handler = new CapturingHandler();
        var provider = new AzureFoundryChatCompletionsProvider(new StubHttpClientFactory(handler), CreateFoundryOptions());
        var model = new ModelProfileOptions
        {
            Type = ModelProviderType.AzureOpenAI,
            Endpoint = "https://my-foundry.openai.azure.com",
            Deployment = "gpt-5-mini",
            ApiVersion = "2024-10-21",
            ApiKey = "secret-key"
        };

        await provider.SendChatCompletionsAsync(Payload(), model, stream: false, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        var uri = handler.LastRequest.RequestUri!;
        Assert.Equal("my-foundry.openai.azure.com", uri.Host);
        Assert.Contains("openai/deployments/gpt-5-mini/chat/completions", uri.AbsolutePath);
        Assert.Contains("api-version=2024-10-21", uri.Query);
        Assert.Equal("secret-key", handler.LastRequest.Headers.GetValues("api-key").Single());
    }

    [Theory]
    [InlineData("https://my-foundry.openai.azure.com")]
    [InlineData("https://my-foundry.openai.azure.com/")]
    [InlineData("https://my-foundry.openai.azure.com/openai")]
    [InlineData("https://my-foundry.openai.azure.com/openai/v1")]
    [InlineData("https://my-foundry.openai.azure.com/openai/v1/")]
    public async Task AzureProvider_NormalizesEndpoint_AvoidsDoubledOpenAiPath(string endpoint)
    {
        var handler = new CapturingHandler();
        var provider = new AzureFoundryChatCompletionsProvider(new StubHttpClientFactory(handler), CreateFoundryOptions());
        var model = new ModelProfileOptions
        {
            Type = ModelProviderType.AzureOpenAI,
            Endpoint = endpoint,
            Deployment = "gpt-5-mini",
            ApiVersion = "2024-10-21",
            ApiKey = "secret-key"
        };

        await provider.SendChatCompletionsAsync(Payload(), model, stream: false, CancellationToken.None);

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("my-foundry.openai.azure.com", uri.Host);
        Assert.Equal("/openai/deployments/gpt-5-mini/chat/completions", uri.AbsolutePath);
        Assert.DoesNotContain("openai/v1/openai", uri.AbsolutePath);
    }

    [Fact]
    public async Task AzureProvider_FallsBackToSharedFoundryConfig()
    {
        var handler = new CapturingHandler();
        var provider = new AzureFoundryChatCompletionsProvider(new StubHttpClientFactory(handler), CreateFoundryOptions("https://shared.openai.azure.com", "shared-key"));
        var model = new ModelProfileOptions { Type = ModelProviderType.AzureOpenAI, Deployment = "gpt-5-mini" };

        await provider.SendChatCompletionsAsync(Payload(), model, stream: false, CancellationToken.None);

        Assert.Equal("shared.openai.azure.com", handler.LastRequest!.RequestUri!.Host);
        Assert.Equal("shared-key", handler.LastRequest.Headers.GetValues("api-key").Single());
    }

    [Fact]
    public async Task OllamaProvider_BuildsOpenAiCompatibleRequest_NoApiKey()
    {
        var handler = new CapturingHandler();
        var provider = new OllamaChatCompletionsProvider(new StubHttpClientFactory(handler));
        var model = new ModelProfileOptions
        {
            Type = ModelProviderType.Ollama,
            Endpoint = "http://localhost:11434",
            Deployment = "llama3.1:8b"
        };

        await provider.SendChatCompletionsAsync(Payload(), model, stream: true, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("localhost", uri.Host);
        Assert.Equal(11434, uri.Port);
        Assert.Contains("v1/chat/completions", uri.AbsolutePath);
        Assert.False(handler.LastRequest.Headers.Contains("api-key"));

        var body = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.Equal("llama3.1:8b", (string?)body["model"]);
        Assert.True((bool)body["stream"]!);
    }

    private static JsonObject Payload() =>
        new()
        {
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "hi" })
        };

    private static IOptions<FoundryOptions> CreateFoundryOptions(string endpoint = "https://default.openai.azure.com", string apiKey = "default-key") =>
        Options.Create(new FoundryOptions { Endpoint = endpoint, ApiKey = apiKey });

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
