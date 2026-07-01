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

    private sealed class StubProvider(HttpStatusCode status, string body) : IChatCompletionsProvider
    {
        public ModelProviderType ProviderType => ModelProviderType.Ollama;

        public Task<HttpResponseMessage> SendChatCompletionsAsync(
            JsonObject payload,
            ModelProfileOptions model,
            bool stream,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
