using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed class FoundryEndpointHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<FoundryOptions> options,
    ILogger<FoundryEndpointHealthCheck> logger) : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly FoundryOptions _options = options.Value;
    private readonly ILogger<FoundryEndpointHealthCheck> _logger = logger;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FoundryOptions.GetNormalizedEndpoint(_options.Endpoint));
            using var response = await _httpClientFactory.CreateClient("foundry-health")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode ||
                response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden or
                System.Net.HttpStatusCode.NotFound)
            {
                return HealthCheckResult.Healthy($"Foundry endpoint reachable (status {(int)response.StatusCode}).");
            }

            return HealthCheckResult.Degraded($"Foundry endpoint reachable but returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Foundry endpoint health probe failed.");
            return HealthCheckResult.Unhealthy("Foundry endpoint is not reachable.", ex);
        }
    }
}
