using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api;

public interface IRequestRoutingService
{
    Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken);
    Task<RoutingDecision> SelectModelAsync(JsonObject requestBody, CancellationToken cancellationToken);
    Task<RoutingSelectionResult> SelectModelWithTraceAsync(JsonObject requestBody, CancellationToken cancellationToken);
    Task<RoutingSelectionResult> SelectModelWithTraceAsync(
        JsonObject requestBody,
        RoutingRequestMetadata? requestMetadata,
        CancellationToken cancellationToken);
}

public sealed class RequestRoutingService(
    IRoutingConfigurationStore configurationStore,
    IRoutingWorkflow routingWorkflow) : IRequestRoutingService
{
    private readonly IRoutingConfigurationStore _configurationStore = configurationStore;
    private readonly IRoutingWorkflow _routingWorkflow = routingWorkflow;

    public Task<RoutingOptions> GetRoutingOptionsAsync(CancellationToken cancellationToken) =>
        _configurationStore.GetRoutingOptionsAsync(cancellationToken);

    public async Task<RoutingDecision> SelectModelAsync(JsonObject requestBody, CancellationToken cancellationToken) =>
        (await SelectModelWithTraceAsync(requestBody, cancellationToken)).Decision;

    public async Task<RoutingSelectionResult> SelectModelWithTraceAsync(JsonObject requestBody, CancellationToken cancellationToken)
    {
        return await SelectModelWithTraceAsync(requestBody, null, cancellationToken);
    }

    public async Task<RoutingSelectionResult> SelectModelWithTraceAsync(
        JsonObject requestBody,
        RoutingRequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var options = await _configurationStore.GetRoutingOptionsAsync(cancellationToken);
        return await _routingWorkflow.RouteAsync(requestBody, options, requestMetadata, cancellationToken);
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

    public static void AddRoutingHeaders(HttpResponse response, RoutingSelectionResult routingSelection)
    {
        response.Headers["x-harness-model-profile"] = SanitizeHeaderValue(routingSelection.Decision.ProfileName);
        response.Headers["x-harness-model-deployment"] = SanitizeHeaderValue(routingSelection.Decision.Profile.Deployment);
        response.Headers["x-harness-routing-reason"] = SanitizeHeaderValue(routingSelection.Decision.Reason);
        response.Headers["x-harness-trace-id"] = SanitizeHeaderValue(routingSelection.TraceId);
        response.Headers["x-harness-client-id"] = SanitizeHeaderValue(routingSelection.Client.Id);
        response.Headers["x-harness-client-source"] = SanitizeHeaderValue(routingSelection.Client.Source);
        if (!string.IsNullOrWhiteSpace(routingSelection.Client.Version))
        {
            response.Headers["x-harness-client-version"] = SanitizeHeaderValue(routingSelection.Client.Version);
        }
    }

    /// <summary>
    /// HTTP header values must be ASCII with no control characters. Routing reasons can contain
    /// arrows (→) and LLM-generated text with arbitrary Unicode, so map common symbols to ASCII and
    /// drop anything else outside the printable range to avoid Kestrel rejecting the response.
    /// </summary>
    public static string SanitizeHeaderValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\u2192': // →
                case '\u2794':
                case '\u27A1':
                    builder.Append("->");
                    break;
                case '\u2018':
                case '\u2019': // ' '
                    builder.Append('\'');
                    break;
                case '\u201C':
                case '\u201D': // " "
                    builder.Append('"');
                    break;
                case '\u2013':
                case '\u2014': // – —
                    builder.Append('-');
                    break;
                case '\u2026': // …
                    builder.Append("...");
                    break;
                default:
                    // Keep printable ASCII (space through tilde); drop everything else.
                    if (ch >= 0x20 && ch <= 0x7E)
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    public static RoutingRequestMetadata BuildRequestMetadata(HttpRequest request, JsonObject? requestBody, string? requestId = null)
    {
        var userAgent = request.Headers.UserAgent.ToString();
        var metadataClient = TryGetClientObject(requestBody?["metadata"]);
        var rootClient = TryGetClientObject(requestBody?["client"]);

        var payloadClientName = GetNodeString(metadataClient?["name"])
                                ?? GetNodeString(metadataClient?["id"])
                                ?? GetNodeString(metadataClient?["surface"])
                                ?? GetNodeString(rootClient?["name"])
                                ?? GetNodeString(rootClient?["id"])
                                ?? GetNodeString(rootClient?["surface"]);
        var payloadClientVersion = GetNodeString(metadataClient?["version"])
                                   ?? GetNodeString(rootClient?["version"]);

        var headerClientName = request.Headers["x-copilot-client"].FirstOrDefault()
                               ?? request.Headers["x-client-name"].FirstOrDefault()
                               ?? request.Headers["x-client-id"].FirstOrDefault();
        var headerClientVersion = request.Headers["x-copilot-client-version"].FirstOrDefault()
                                  ?? request.Headers["x-client-version"].FirstOrDefault();

        var source = "unknown";
        var rawClient = payloadClientName;
        if (!string.IsNullOrWhiteSpace(rawClient))
        {
            source = "payload";
        }
        else if (!string.IsNullOrWhiteSpace(headerClientName))
        {
            source = "header";
            rawClient = headerClientName;
        }
        else if (!string.IsNullOrWhiteSpace(userAgent))
        {
            source = "user-agent";
            rawClient = userAgent;
        }

        var normalizedClient = NormalizeClientId(rawClient);
        var version = payloadClientVersion
                      ?? headerClientVersion
                      ?? InferClientVersionFromUserAgent(userAgent, normalizedClient);

        return new RoutingRequestMetadata(
            Id: normalizedClient,
            Source: source,
            Version: string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            UserAgent: string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            RequestId: requestId,
            Endpoint: request.Path.HasValue ? request.Path.Value : null);
    }

    public static string? GetRequestedModel(JsonObject? requestBody) =>
        GetNodeString(requestBody?["model"]);

    private static JsonObject? TryGetClientObject(JsonNode? node)
    {
        if (node is JsonObject root && root["client"] is JsonObject nestedClient)
        {
            return nestedClient;
        }

        return node as JsonObject;
    }

    private static string? InferClientVersionFromUserAgent(string userAgent, string clientId)
    {
        if (string.IsNullOrWhiteSpace(userAgent) || string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var token = clientId switch
        {
            "copilot-cli" => "copilot-cli/",
            "copilot-app" => "copilot-app/",
            "vscode" => "vscode/",
            _ => null
        };

        if (token is null)
        {
            return null;
        }

        var index = userAgent.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + token.Length;
        if (start >= userAgent.Length)
        {
            return null;
        }

        var remaining = userAgent[start..];
        var end = remaining.IndexOf(' ');
        return (end < 0 ? remaining : remaining[..end]).Trim();
    }

    private static string NormalizeClientId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("copilot-cli", StringComparison.Ordinal))
        {
            return "copilot-cli";
        }

        if (normalized.Contains("visual studio code", StringComparison.Ordinal) ||
            normalized.Contains("vscode", StringComparison.Ordinal) ||
            normalized.Contains("copilot-chat", StringComparison.Ordinal))
        {
            return "vscode";
        }

        if (normalized.Contains("copilot-app", StringComparison.Ordinal) ||
            normalized.Contains("copilot app", StringComparison.Ordinal))
        {
            return "copilot-app";
        }

        return normalized;
    }

    private static string? GetNodeString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text.Trim();
        }

        return null;
    }
}
