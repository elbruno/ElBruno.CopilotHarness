using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Sends chat completion requests to a specific model connection. Implementations are provider-specific.
/// </summary>
public interface IChatCompletionsProvider
{
    ModelProviderType ProviderType { get; }

    Task<HttpResponseMessage> SendChatCompletionsAsync(
        JsonObject payload,
        ModelProfileOptions model,
        bool stream,
        CancellationToken cancellationToken);
}

/// <summary>Selects the right <see cref="IChatCompletionsProvider"/> for a resolved model connection.</summary>
public interface IChatCompletionsProviderFactory
{
    IChatCompletionsProvider GetProvider(ModelProfileOptions model);
}

public sealed class ChatCompletionsProviderFactory(IEnumerable<IChatCompletionsProvider> providers)
    : IChatCompletionsProviderFactory
{
    private readonly IReadOnlyDictionary<ModelProviderType, IChatCompletionsProvider> _providers =
        providers.ToDictionary(provider => provider.ProviderType);

    public IChatCompletionsProvider GetProvider(ModelProfileOptions model)
    {
        if (_providers.TryGetValue(model.Type, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"No chat completions provider registered for type '{model.Type}'.");
    }
}

/// <summary>
/// Azure OpenAI / Azure AI Foundry provider. Uses the model connection's own endpoint and API key when
/// present, otherwise falls back to the shared Foundry configuration for backward compatibility.
/// </summary>
public sealed class AzureFoundryChatCompletionsProvider(IHttpClientFactory httpClientFactory, IOptions<FoundryOptions> foundryOptions)
    : IChatCompletionsProvider
{
    private readonly FoundryOptions _foundry = foundryOptions.Value;

    public ModelProviderType ProviderType => ModelProviderType.AzureOpenAI;

    public Task<HttpResponseMessage> SendChatCompletionsAsync(
        JsonObject payload,
        ModelProfileOptions model,
        bool stream,
        CancellationToken cancellationToken)
    {
        var endpoint = string.IsNullOrWhiteSpace(model.Endpoint) ? _foundry.Endpoint : model.Endpoint;
        var apiKey = string.IsNullOrWhiteSpace(model.ApiKey) ? _foundry.ApiKey : model.ApiKey;
        var apiVersion = string.IsNullOrWhiteSpace(model.ApiVersion) ? FoundryOptions.DefaultApiVersion : model.ApiVersion;

        var client = httpClientFactory.CreateClient("model-provider");
        client.BaseAddress = FoundryOptions.GetAzureResourceBase(endpoint);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"openai/deployments/{Uri.EscapeDataString(model.Deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}

/// <summary>
/// Ollama provider. Targets the OpenAI-compatible chat completions endpoint exposed by Ollama
/// (<c>{endpoint}/v1/chat/completions</c>); no API key is required.
/// </summary>
public sealed class OllamaChatCompletionsProvider(IHttpClientFactory httpClientFactory) : IChatCompletionsProvider
{
    public ModelProviderType ProviderType => ModelProviderType.Ollama;

    public Task<HttpResponseMessage> SendChatCompletionsAsync(
        JsonObject payload,
        ModelProfileOptions model,
        bool stream,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("model-provider");
        client.BaseAddress = FoundryOptions.GetNormalizedEndpoint(model.Endpoint);

        var body = (JsonObject)payload.DeepClone();
        body["model"] = model.Deployment;
        body["stream"] = stream;

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
