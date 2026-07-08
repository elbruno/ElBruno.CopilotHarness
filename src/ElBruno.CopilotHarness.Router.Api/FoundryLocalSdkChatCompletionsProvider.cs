using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Microsoft Foundry Local provider backed by the direct .NET SDK
/// (<c>Microsoft.AI.Foundry.Local</c>).
/// <para>
/// Unlike <see cref="FoundryLocalChatCompletionsProvider"/>, this provider does NOT require the
/// caller to configure an endpoint URL. Instead it:
/// <list type="number">
///   <item>Lazily initializes <see cref="FoundryLocalSdkService"/> (which starts the SDK's embedded
///       OpenAI-compatible web server on a dynamically assigned port).</item>
///   <item>Auto-discovers the web service URL from <see cref="FoundryLocalSdkService.WebServiceUrl"/>.</item>
///   <item>Proxies the request to that URL using the same streaming path as the HTTP-based providers.</item>
/// </list>
/// </para>
/// Requires Foundry Local to be installed on the host machine
/// (<c>winget install Microsoft.FoundryLocal</c>).
/// </summary>
public sealed class FoundryLocalSdkChatCompletionsProvider(
    IHttpClientFactory httpClientFactory,
    FoundryLocalSdkService sdk,
    ILogger<FoundryLocalSdkChatCompletionsProvider> logger) : IChatCompletionsProvider
{
    public ModelProviderType ProviderType => ModelProviderType.FoundryLocalSdk;

    public async Task<HttpResponseMessage> SendChatCompletionsAsync(
        JsonObject payload,
        ModelProfileOptions model,
        bool stream,
        CancellationToken cancellationToken)
    {
        // 1. Ensure the SDK is running (lazy-init; subsequent calls are fast no-ops).
        await sdk.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!sdk.IsInitialized)
        {
            var errorMessage = sdk.InitError ?? "Foundry Local SDK failed to initialize.";
            logger.LogError("FoundryLocalSdkChatCompletionsProvider: SDK not initialized. {Error}", errorMessage);
            throw new InvalidOperationException(
                $"Foundry Local SDK is not available: {errorMessage}. " +
                "Ensure Foundry Local is installed (winget install Microsoft.FoundryLocal) and " +
                "the process has permission to start it.");
        }

        // 2. Auto-discover the SDK web service URL — no manual endpoint config required.
        var endpoint = sdk.WebServiceUrl
            ?? throw new InvalidOperationException(
                "Foundry Local SDK web service URL is not available. " +
                "The SDK may have initialized without starting its web service.");

        // 3. Build the OpenAI-compatible request body with the model deployment name.
        var body = (JsonObject)PayloadSanitizer.Sanitize(payload, model).DeepClone();
        body["model"] = model.Deployment;
        body["stream"] = stream;

        // 4. Forward to the SDK's embedded web server (same as the HTTP-based provider path).
        var client = httpClientFactory.CreateClient("model-provider");
        client.BaseAddress = FoundryOptions.GetNormalizedEndpoint(endpoint);
        client.Timeout = TimeSpan.FromMinutes(5);

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        logger.LogDebug(
            "FoundryLocalSdkChatCompletionsProvider: forwarding to {Endpoint} model={Deployment} stream={Stream}",
            endpoint, model.Deployment, stream);

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }
}
