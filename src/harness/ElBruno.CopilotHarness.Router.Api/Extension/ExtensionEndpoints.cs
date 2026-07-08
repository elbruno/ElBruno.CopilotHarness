using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Api;
using ElBruno.CopilotHarness.Router.Api.Admin;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Extension;

public static class ExtensionEndpoints
{
    public static IEndpointRouteBuilder MapExtensionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/status", async (
            IExecutionTraceStore traceStore,
            HealthCheckService healthCheckService,
            CancellationToken cancellationToken) =>
        {
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var traces = traceStore.GetRecent(1);
            var latestTrace = traces.FirstOrDefault();
            var healthReport = await healthCheckService.CheckHealthAsync(cancellationToken);
            var healthChecks = healthReport.Entries
                .Select(entry => new OperationalHealthDto(
                    entry.Key,
                    entry.Value.Status.ToString(),
                    entry.Value.Description ?? entry.Value.Exception?.Message ?? "No additional details."))
                .ToList();

            var dashboardLinks = BuildDashboardLinks();
            var summary = latestTrace is null
                ? "No routed requests have been recorded yet."
                : $"Latest routing decision selected {latestTrace.Decision.ProfileName} ({latestTrace.Decision.Profile.Deployment}).";

            return Results.Ok(new ExtensionStatusSurfaceDto(
                generatedAtUtc,
                healthReport.Status.ToString(),
                summary,
                latestTrace?.TraceId,
                latestTrace?.Decision.ProfileName,
                latestTrace?.Decision.Profile.Deployment,
                dashboardLinks,
                healthChecks));
        });

        endpoints.MapGet("/v1/explain-routing/{traceId}", (string traceId, IExecutionTraceStore traceStore) =>
        {
            if (!traceStore.TryGet(traceId, out var trace))
            {
                return Results.NotFound();
            }

            return Results.Ok(RoutingTraceResponseMapper.ToResponse(trace));
        });

        endpoints.MapGet("/v1/extension/capabilities", (IOptions<RoutingOptions> routingOptions) =>
        {
            var dashboardLinks = BuildDashboardLinks();
            var explainLink = dashboardLinks[1];

            return Results.Ok(new ExtensionCapabilitiesResponse(
                DateTimeOffset.UtcNow,
                new ExtensionStatusSurfaceDto(
                    DateTimeOffset.UtcNow,
                    "Ready",
                    "Status and routing explanation surfaces are available.",
                    null,
                    routingOptions.Value.DefaultProfile,
                    routingOptions.Value.Profiles.TryGetValue(routingOptions.Value.DefaultProfile, out var profile) ? profile.Deployment : null,
                    dashboardLinks,
                    []),
                new ExtensionExplainRoutingSurfaceDto(
                    "Explain routing",
                    "Inspect the routing decision for a trace id.",
                    "/v1/explain-routing/{traceId}",
                    "traceId",
                    explainLink),
                new ExtensionChatParticipantSurfaceDto(
                    "@harness",
                    "Harness",
                    "Ask Harness to explain routing or open the dashboard.",
                    ["explain routing", "show status", "open dashboard"]),
                dashboardLinks,
                [
                    new LanguageModelToolMetadataDto(
                        "harness.status",
                        "Returns the current router status surface.",
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject(),
                            ["additionalProperties"] = false
                        }),
                    new LanguageModelToolMetadataDto(
                        "harness.explain-routing",
                        "Explains a routing decision for a trace id.",
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["traceId"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["description"] = "The routing trace id."
                                }
                            },
                            ["required"] = new JsonArray { JsonValue.Create("traceId")! },
                            ["additionalProperties"] = false
                        }),
                    new LanguageModelToolMetadataDto(
                        "harness.dashboard-links",
                        "Returns the dashboard link metadata surface.",
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject(),
                            ["additionalProperties"] = false
                        })
                ]));
        });

        return endpoints;
    }

    private static IReadOnlyList<DashboardLinkDto> BuildDashboardLinks() =>
    [
        new DashboardLinkDto("status", "Status panel", "View the router health and latest routing summary.", "/v1/status", false),
        new DashboardLinkDto("explain-routing", "Explain routing", "Open a trace and inspect the routing decision.", "/v1/explain-routing/{traceId}", false),
        new DashboardLinkDto("admin-dashboard", "Admin dashboard", "Open the admin telemetry snapshot.", "/admin/dashboard/snapshot", true),
        new DashboardLinkDto("admin-operations", "Admin operations", "Open the operational readiness view.", "/admin/operations/status", true),
    ];
}
