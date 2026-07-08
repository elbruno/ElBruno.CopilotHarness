using System.Net;
using ElBruno.CopilotHarness.Router.Api;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class FoundryLocalEndpointHealthCheckTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]    // /v1/models may 404 on some builds
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenEndpointReachable(HttpStatusCode statusCode)
    {
        var check = BuildCheck(statusCode);
        var result = await check.CheckHealthAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task CheckHealthAsync_ReturnsDegraded_WhenEndpointReturnsUnexpectedStatus(HttpStatusCode statusCode)
    {
        var check = BuildCheck(statusCode);
        var result = await check.CheckHealthAsync(BuildContext(), CancellationToken.None);
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains(((int)statusCode).ToString(), result.Description ?? string.Empty);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenEndpointUnreachable()
    {
        // Throwing factory simulates connection refused.
        var factory = new ThrowingHttpClientFactory();
        var check = new FoundryLocalEndpointHealthCheck(
            factory,
            Options.Create(new FoundryLocalOptions { Endpoint = "http://localhost:9999" }),
            NullLogger<FoundryLocalEndpointHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FoundryLocalEndpointHealthCheck BuildCheck(HttpStatusCode status)
    {
        var handler = new StubHandler(new HttpResponseMessage(status));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5101") };
        var factory = new FixedHttpClientFactory(client);
        return new FoundryLocalEndpointHealthCheck(
            factory,
            Options.Create(new FoundryLocalOptions { Endpoint = "http://localhost:5101" }),
            NullLogger<FoundryLocalEndpointHealthCheck>.Instance);
    }

    private static HealthCheckContext BuildContext() =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "foundry-local-endpoint",
                new StubHealthCheck(),
                HealthStatus.Unhealthy,
                [])
        };

    private sealed class StubHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy());
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = new ThrowingHandler();
            return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999") };
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
        }
    }
}
