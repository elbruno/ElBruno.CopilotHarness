using ProxiesTestApp.Services;

var builder = WebApplication.CreateBuilder(args);

// OTEL → Aspire dashboard (traces, metrics, structured logs).
// Works standalone too — OTLP is skipped when the Aspire env var is absent.
builder.AddProxiesServiceDefaults();

// Blazor Server with interactive components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// One named HttpClient per proxy — URLs come from appsettings (standalone)
// or are overridden by Aspire WithReference env-var injection.
foreach (var proxy in ProxyConfig.All)
{
    builder.Services.AddHttpClient(proxy.Name, client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration[$"ProxyUrls:{proxy.ConfigKey}"] ?? proxy.DefaultUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
    });
}

builder.Services.AddScoped<ProxyClient>();
// Singleton: stores request history across all Blazor connections / pages.
builder.Services.AddSingleton<RequestHistoryService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<ProxiesTestApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
