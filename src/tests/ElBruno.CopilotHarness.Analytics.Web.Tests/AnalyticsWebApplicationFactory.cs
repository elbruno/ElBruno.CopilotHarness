using ElBruno.CopilotHarness.Analytics.Web;
using ElBruno.CopilotHarness.Analytics.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.CopilotHarness.Analytics.Web.Tests;

public sealed class AnalyticsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IUsageTelemetryApiClient _client;

    public AnalyticsWebApplicationFactory(IUsageTelemetryApiClient client)
    {
        _client = client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(_client);
        });
    }
}
