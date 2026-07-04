using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Proxies.ServiceDefaults;

namespace Microsoft.Extensions.Hosting;

// =============================================================================
//  ProxiesServiceDefaultsExtensions
//
//  Single call — builder.AddProxiesServiceDefaults() — gives every proxy and
//  the Blazor test app:
//
//    • OpenTelemetry traces  (ASP.NET Core + HttpClient + custom LLM spans)
//    • OpenTelemetry metrics (ASP.NET Core + HttpClient + .NET Runtime)
//    • OpenTelemetry logs    (structured, formatted)
//    • OTLP export           (only when Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT)
//    • Health check service  (Aspire reads /health for the Resources tab)
//
//  DELIBERATELY NOT included:
//    • Service discovery — proxies use fixed ports; Aspire injects URLs as env vars
//    • Resilience handler — each proxy configures its own 5-min LLM timeout
//      (AddStandardResilienceHandler with its 30-s default would kill long calls)
// =============================================================================

public static class ProxiesServiceDefaultsExtensions
{
    public static TBuilder AddProxiesServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureProxiesOpenTelemetry();

        // Register the health-check service so Aspire can probe /health.
        // Note: each proxy already maps its own /health endpoint with custom JSON —
        // we only add the underlying service here, not the framework /healthz path.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    static TBuilder ConfigureProxiesOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // Structured logs → Aspire dashboard "Console" and "Structured" tabs.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true; // values substituted into {placeholders}
            logging.IncludeScopes           = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()  // request counters + durations
                    .AddHttpClientInstrumentation()  // upstream call durations (→ Ollama/Azure/SDK)
                    .AddRuntimeInstrumentation();    // GC, thread pool, heap — Aspire Metrics tab
            })
            .WithTracing(tracing =>
            {
                tracing
                    // ApplicationName = the proxy's assembly name (OllamaProxy, FoundryProxy, …).
                    // This registers the proxy's own ActivitySource so any custom spans it
                    // creates are captured.
                    .AddSource(builder.Environment.ApplicationName)
                    // Shared LLM span source — all three proxies emit spans into this source.
                    // The llm.proxy tag distinguishes them in the Aspire Traces view.
                    .AddSource(LlmActivity.SourceName)
                    .AddAspNetCoreInstrumentation(opt =>
                    {
                        // Exclude health probes and root pings — they add noise.
                        opt.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health")
                            && ctx.Request.Path != "/";
                    })
                    .AddHttpClientInstrumentation(); // one span per upstream HTTP call
            });

        // OTLP export — activated only when Aspire injects the endpoint env var.
        // Running standalone (dotnet run) → env var absent → telemetry stays local.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }
}
