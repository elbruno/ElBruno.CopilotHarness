using ElBruno.CopilotHarness.Analytics.Web.Components;
using ElBruno.CopilotHarness.Analytics.Web.Services;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<IUsageTelemetryApiClient, UsageTelemetryApiClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["TelemetryApi:BaseUrl"] ?? "http://router-api";
    var apiKey = configuration["TelemetryApi:ApiKey"];

    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(30);
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/health/analytics", (IConfiguration configuration) => Results.Ok(new
{
    service = "analytics-web",
    status = "ok",
    telemetryApiBaseUrl = configuration["TelemetryApi:BaseUrl"] ?? "http://router-api"
}));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
