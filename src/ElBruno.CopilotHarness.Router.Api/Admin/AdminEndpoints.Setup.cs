using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapSetupEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/setup/state", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var setupState = await store.GetSetupWizardStateAsync(cancellationToken);
            return new SetupWizardResponse(setupState.IsCompleted, setupState.DefaultProfile, setupState.CompletedAtUtc);
        });

        group.MapPost("/setup/wizard", async (
            SetupWizardRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var response = await store.CompleteSetupWizardAsync(
                new CompleteSetupWizardRequest(request.DefaultModel, request.GenerateFirstRules),
                cancellationToken);

            return Results.Ok(new SetupWizardResponse(response.IsCompleted, response.DefaultProfile, response.CompletedAtUtc));
        });

        group.MapPost("/setup/generate-first-rules", async (
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var rules = await store.GenerateStarterRulesAsync(cancellationToken);
            return Results.Ok(rules.Select(ToRoutingRuleDto).ToList());
        });
    }
}
