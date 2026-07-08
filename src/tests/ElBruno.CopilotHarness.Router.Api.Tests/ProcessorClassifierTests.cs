using System.Net;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class ProcessorClassifierTests
{
    [Fact]
    public async Task DeterministicClassifier_MapsKeywords_ToFixedVocabulary()
    {
        var options = CreateOptions();

        Assert.Equal(ClassifierIntentNames.SimpleChat, Classify("hi", options).Intent);
        Assert.Equal(ClassifierIntentNames.GithubActions, Classify("push to gh now", options).Intent);
        Assert.Equal(ClassifierIntentNames.LaunchApp, Classify("launch the app please", options).Intent);
        Assert.Equal(ClassifierIntentNames.LaunchApp, Classify("stop the app", options).Intent);
        Assert.Equal(ClassifierIntentNames.CodeTask, Classify("refactor the parser", options).Intent);
        Assert.Equal(ClassifierIntentNames.LongForm, Classify(new string('x', 3000), options).Intent);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProcessorClassifier_UsesProcessorModel_WhenItReturnsValidJson()
    {
        var options = CreateOptions();
        var json = """{"choices":[{"message":{"content":"{\"intent\":\"github-actions\",\"complexity\":\"low\",\"confidence\":0.88,\"reasoning\":\"git push\"}"}}]}""";
        var agent = CreateAgent(new StubProvider(HttpStatusCode.OK, json));

        var result = await agent.ClassifyAsync(Body("push to gh"), new RoutingContext([]), options, CancellationToken.None);

        Assert.Equal("processor-model", result.Source);
        Assert.Equal(ClassifierIntentNames.GithubActions, result.Intent);
        Assert.Equal("ollama", result.ProcessorModel);
        Assert.Equal(0.88, result.Confidence, 2);
    }

    [Fact]
    public async Task ProcessorClassifier_FallsBack_WhenNoProcessorModelConfigured()
    {
        var options = CreateOptions(includeProcessor: false);
        var agent = CreateAgent(new StubProvider(HttpStatusCode.OK, "{}"));

        var result = await agent.ClassifyAsync(Body("hi"), new RoutingContext([]), options, CancellationToken.None);

        Assert.Equal("deterministic", result.Source);
        Assert.Equal(ClassifierIntentNames.SimpleChat, result.Intent);
    }

    [Fact]
    public async Task ProcessorClassifier_FallsBack_WhenProcessorReturnsError()
    {
        var options = CreateOptions();
        var agent = CreateAgent(new StubProvider(HttpStatusCode.InternalServerError, "boom"));

        var result = await agent.ClassifyAsync(Body("refactor the parser"), new RoutingContext([]), options, CancellationToken.None);

        Assert.Equal("deterministic", result.Source);
        Assert.Equal(ClassifierIntentNames.CodeTask, result.Intent);
    }

    [Fact]
    public async Task ProcessorClassifier_FallsBack_WhenResponseIsUnparseable()
    {
        var options = CreateOptions();
        var agent = CreateAgent(new StubProvider(HttpStatusCode.OK, "not json at all"));

        var result = await agent.ClassifyAsync(Body("hi"), new RoutingContext([]), options, CancellationToken.None);

        Assert.Equal("deterministic", result.Source);
    }

    [Theory]
    [InlineData(ModelProviderType.FoundryLocal, "foundry-local-phi4")]
    [InlineData(ModelProviderType.Ollama, "ollama-llama31")]
    [InlineData(ModelProviderType.AzureOpenAI, "gpt5mini-proc")]
    public async Task ProcessorClassifier_WorksWithAnyProviderType_WhenIsProcessorTrue(
        ModelProviderType providerType, string profileKey)
    {
        var json = """{"choices":[{"message":{"content":"{\"intent\":\"code-task\",\"complexity\":\"high\",\"confidence\":0.9,\"reasoning\":\"test\"}"}}]}""";
        var provider = new StubProvider(HttpStatusCode.OK, json, providerType);

        var profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Enabled = true, SupportsCustomTemperature = false },
            [profileKey] = new() { Type = providerType, Deployment = "some-model", Enabled = true, IsProcessor = true }
        };
        var options = new RoutingOptions
        {
            DefaultProfile = "gpt5mini",
            Profiles = profiles,
            Rules = new BasicRulesOptions { BigPromptCharacterThreshold = 2500 }
        };

        var factory = new ChatCompletionsProviderFactory(new IChatCompletionsProvider[] { provider });
        var agent = new ProcessorModelClassificationAgent(
            factory,
            new DeterministicClassificationAgent(),
            Options.Create(new ClassifierOptions { Enabled = true, PreviewChars = 200, TimeoutMs = 4000 }),
            NullLogger<ProcessorModelClassificationAgent>.Instance);

        var result = await agent.ClassifyAsync(Body("refactor the module"), new RoutingContext([]), options, CancellationToken.None);

        Assert.Equal("processor-model", result.Source);
        Assert.Equal(ClassifierIntentNames.CodeTask, result.Intent);
        Assert.Equal(profileKey, result.ProcessorModel);
    }

    [Theory]
    [InlineData(true,  true)]   // SupportsCustomTemperature=true  → temperature present
    [InlineData(false, false)]  // SupportsCustomTemperature=false → temperature absent (Azure gpt-5.x pattern)
    public async Task ProcessorClassifier_StripsTemperature_WhenProcessorDoesNotSupportIt(
        bool supportsCustomTemperature, bool expectTemperatureInPayload)
    {
        var json = """{"choices":[{"message":{"content":"{\"intent\":\"simple-chat\",\"complexity\":\"low\",\"confidence\":0.95,\"reasoning\":\"test\"}"}}]}""";
        var capturingProvider = new CapturingStubProvider(HttpStatusCode.OK, json);

        var profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Enabled = true, SupportsCustomTemperature = false },
            ["azure-proc"] = new()
            {
                Type = ModelProviderType.AzureOpenAI,
                Deployment = "gpt-5-mini",
                Enabled = true,
                IsProcessor = true,
                SupportsCustomTemperature = supportsCustomTemperature
            }
        };
        var options = new RoutingOptions
        {
            DefaultProfile = "gpt5mini",
            Profiles = profiles,
            Rules = new BasicRulesOptions { BigPromptCharacterThreshold = 2500 }
        };

        var factory = new ChatCompletionsProviderFactory(new IChatCompletionsProvider[] { capturingProvider });
        var agent = new ProcessorModelClassificationAgent(
            factory,
            new DeterministicClassificationAgent(),
            Options.Create(new ClassifierOptions { Enabled = true, PreviewChars = 200, TimeoutMs = 4000 }),
            NullLogger<ProcessorModelClassificationAgent>.Instance);

        var result = await agent.ClassifyAsync(Body("hi"), new RoutingContext([]), options, CancellationToken.None);

        Assert.Equal("processor-model", result.Source);
        var capturedPayload = capturingProvider.LastPayload!;
        if (expectTemperatureInPayload)
        {
            Assert.True(capturedPayload.ContainsKey("temperature"),
                "temperature should be present when SupportsCustomTemperature=true");
        }
        else
        {
            Assert.False(capturedPayload.ContainsKey("temperature"),
                "temperature must be stripped for models that reject non-default temperature (e.g. Azure gpt-5.x)");
        }
    }

    private static ClassificationResult Classify(string prompt, RoutingOptions options) =>
        DeterministicClassificationAgent.Classify(Body(prompt), new RoutingContext([]), options);

    private static ProcessorModelClassificationAgent CreateAgent(IChatCompletionsProvider provider)
    {
        var factory = new ChatCompletionsProviderFactory(new[] { provider });
        return new ProcessorModelClassificationAgent(
            factory,
            new DeterministicClassificationAgent(),
            Options.Create(new ClassifierOptions { Enabled = true, PreviewChars = 200, TimeoutMs = 4000 }),
            NullLogger<ProcessorModelClassificationAgent>.Instance);
    }

    private static RoutingOptions CreateOptions(bool includeProcessor = true)
    {
        var profiles = new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt5mini"] = new() { Deployment = "gpt-5-mini", Enabled = true, SupportsCustomTemperature = false }
        };

        if (includeProcessor)
        {
            profiles["ollama"] = new() { Type = ModelProviderType.Ollama, Deployment = "llama3.1:8b", Enabled = true, IsProcessor = true };
        }

        return new RoutingOptions
        {
            DefaultProfile = "gpt5mini",
            Profiles = profiles,
            Rules = new BasicRulesOptions { BigPromptCharacterThreshold = 2500 }
        };
    }

    private static JsonObject Body(string prompt) =>
        new()
        {
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt })
        };

    private sealed class StubProvider(
        HttpStatusCode status,
        string body,
        ModelProviderType providerType = ModelProviderType.Ollama) : IChatCompletionsProvider
    {
        public ModelProviderType ProviderType => providerType;

        public Task<HttpResponseMessage> SendChatCompletionsAsync(
            JsonObject payload,
            ModelProfileOptions model,
            bool stream,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class CapturingStubProvider(HttpStatusCode status, string body) : IChatCompletionsProvider
    {
        public ModelProviderType ProviderType => ModelProviderType.AzureOpenAI;
        public JsonObject? LastPayload { get; private set; }

        public Task<HttpResponseMessage> SendChatCompletionsAsync(
            JsonObject payload,
            ModelProfileOptions model,
            bool stream,
            CancellationToken cancellationToken)
        {
            LastPayload = payload;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
