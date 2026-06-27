using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed class FoundryChatCompletionsClient(HttpClient httpClient, IOptions<FoundryOptions> options)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly FoundryOptions _options = options.Value;

    public Task<HttpResponseMessage> SendChatCompletionsAsync(JsonObject payload, bool stream, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"openai/deployments/{FoundryOptions.DeploymentName}/chat/completions?api-version={FoundryOptions.ApiVersion}")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
