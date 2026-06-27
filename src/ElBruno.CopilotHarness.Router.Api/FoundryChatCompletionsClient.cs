using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed class FoundryChatCompletionsClient(HttpClient httpClient, IOptions<FoundryOptions> options)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly FoundryOptions _options = options.Value;

    public Task<HttpResponseMessage> SendChatCompletionsAsync(
        JsonObject payload,
        ModelProfileOptions modelProfile,
        bool stream,
        CancellationToken cancellationToken)
    {
        var apiVersion = string.IsNullOrWhiteSpace(modelProfile.ApiVersion)
            ? FoundryOptions.DefaultApiVersion
            : modelProfile.ApiVersion;

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"openai/deployments/{Uri.EscapeDataString(modelProfile.Deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
