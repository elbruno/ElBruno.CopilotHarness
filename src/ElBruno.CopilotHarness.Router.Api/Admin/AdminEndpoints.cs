using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints, bool requireAuthorization)
    {
        var group = endpoints.MapGroup("/admin");

        if (requireAuthorization)
        {
            group.RequireAuthorization("AdminOnly");
        }

        MapSetupEndpoints(group);
        MapSettingsEndpoints(group);
        MapModelEndpoints(group);
        MapRuleEndpoints(group);
        MapPlaygroundEndpoints(group);
        MapSystemEndpoints(group);
        MapTelemetryEndpoints(group);
        MapTraceEndpoints(group);
        MapDashboardEndpoints(group);
        MapProfileEndpoints(group);
        MapBenchmarkEndpoints(group);

        return endpoints;
    }
}