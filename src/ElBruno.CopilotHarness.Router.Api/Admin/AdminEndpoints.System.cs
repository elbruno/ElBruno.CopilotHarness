using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapSystemEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/system/validation", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var setup = await store.GetSetupWizardStateAsync(cancellationToken);
            var models = await store.GetModelsAsync(cancellationToken);
            var rules = await store.GetRulesAsync(cancellationToken);
            var defaultModel = await store.GetDefaultModelAsync(cancellationToken);
            var validation = await store.ValidateSystemAsync(cancellationToken);

            var checks = new List<ValidationCheck>
            {
                new("setup-completed", setup.IsCompleted, setup.IsCompleted ? "Setup wizard completed." : "Run setup wizard first."),
                new("models-configured", models.Count > 0, $"Found {models.Count} model connection(s) in the registry."),
                new("enabled-model", models.Any(model => model.Enabled), "At least one model connection must be enabled."),
                new("model-name-configured", models.All(model => !string.IsNullOrWhiteSpace(model.ModelName)), "Each model connection must define a model/deployment name."),
                new("default-model", !string.IsNullOrWhiteSpace(defaultModel.ModelName), $"Default model: {defaultModel.ModelName}."),
                new("rules-available", true, $"{rules.Count} routing rule(s) configured."),
                new("store-validation", validation.IsValid, validation.IsValid ? "Persistence validation passed." : string.Join(' ', validation.Errors))
            };

            checks.AddRange(validation.Warnings.Select(warning => new ValidationCheck("warning", true, warning)));

            return Results.Ok(new ValidationResponse(checks));
        });
    }
}
