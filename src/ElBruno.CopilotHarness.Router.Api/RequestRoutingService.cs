using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api;

public interface IRequestRoutingService
{
    Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken);
    Task<RoutingDecision> SelectModelAsync(JsonObject requestBody, CancellationToken cancellationToken);
}

public sealed class RequestRoutingService(IRoutingConfigurationStore configurationStore) : IRequestRoutingService
{
    private readonly IRoutingConfigurationStore _configurationStore = configurationStore;

    public Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken) =>
        _configurationStore.GetRoutingOptionsAsync(cancellationToken);

    public async Task<RoutingDecision> SelectModelAsync(JsonObject requestBody, CancellationToken cancellationToken)
    {
        var options = await _configurationStore.GetRoutingOptionsAsync(cancellationToken);
        return BasicModelRouter.SelectModel(requestBody, options);
    }
}

public static class OpenAiApiUtilities
{
    public static async Task<(JsonObject? RequestBody, IResult? ErrorResult)> ParseJsonObjectRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        JsonNode? payload;
        try
        {
            payload = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return (null, OpenAiBadRequest("The request body must be valid JSON."));
        }

        if (payload is not JsonObject requestBody)
        {
            return (null, OpenAiBadRequest("The request body must be a JSON object."));
        }

        return (requestBody, null);
    }

    public static IResult OpenAiBadRequest(string message) => Results.BadRequest(new
    {
        error = new
        {
            message,
            type = "invalid_request_error",
            param = (string?)null,
            code = (string?)null
        }
    });

    public static void CopyHeaders(HttpResponseMessage source, HttpResponse target)
    {
        foreach (var header in source.Headers)
        {
            target.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in source.Content.Headers)
        {
            target.Headers[header.Key] = header.Value.ToArray();
        }

        target.Headers.Remove("transfer-encoding");
        target.Headers.Remove("connection");
        target.Headers.Remove("keep-alive");
        target.Headers.Remove("proxy-authenticate");
        target.Headers.Remove("proxy-authorization");
        target.Headers.Remove("te");
        target.Headers.Remove("trailer");
        target.Headers.Remove("upgrade");
    }

    public static void AddRoutingHeaders(HttpResponse response, RoutingDecision routingDecision)
    {
        response.Headers["x-harness-model-profile"] = routingDecision.ProfileName;
        response.Headers["x-harness-model-deployment"] = routingDecision.Profile.Deployment;
        response.Headers["x-harness-routing-reason"] = routingDecision.Reason;
    }
}
