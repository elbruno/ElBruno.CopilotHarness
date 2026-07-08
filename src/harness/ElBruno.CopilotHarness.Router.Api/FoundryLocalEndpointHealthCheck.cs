using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Health check that probes the configured Foundry Local REST endpoint (FoundryLocalProxy or SDK-direct).
/// A successful TCP connection — regardless of HTTP status — indicates the service is reachable.
/// Reports <see cref="HealthStatus.Degraded"/> when reachable but returning an unexpected status,
/// and <see cref="HealthStatus.Unhealthy"/> when the endpoint is unreachable.
/// </summary>
public sealed class FoundryLocalEndpointHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<FoundryLocalOptions> options,
    ILogger<FoundryLocalEndpointHealthCheck> logger) : IHealthCheck
{
    private readonly FoundryLocalOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Probe GET /v1/models — a standard OpenAI-compatible discovery endpoint.
            var probeUrl = new Uri(FoundryOptions.GetNormalizedEndpoint(_options.Endpoint), "v1/models");
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            using var response = await httpClientFactory
                .CreateClient("foundry-local-health")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return response.IsSuccessStatusCode ||
                   response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                       or System.Net.HttpStatusCode.Forbidden
                       or System.Net.HttpStatusCode.NotFound
                ? HealthCheckResult.Healthy($"Foundry Local endpoint reachable at {_options.Endpoint} (status {(int)response.StatusCode}).")
                : HealthCheckResult.Degraded($"Foundry Local endpoint reachable but returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Foundry Local endpoint health probe failed for {Endpoint}.", _options.Endpoint);
            return HealthCheckResult.Unhealthy(
                $"Foundry Local endpoint at {_options.Endpoint} is not reachable. " +
                "Start FoundryLocalProxy or ensure Foundry Local is running.", ex);
        }
    }
}
