using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin");

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
            var response = await store.CompleteSetupWizardAsync(new CompleteSetupWizardRequest(
                    request.LocalDeployment,
                    request.SmallDeployment,
                    request.BigDeployment,
                    request.DefaultProfile,
                    request.GenerateFirstRules),
                cancellationToken);

            return Results.Ok(new SetupWizardResponse(response.IsCompleted, response.DefaultProfile, response.CompletedAtUtc));
        });

        group.MapPost("/setup/generate-first-rules", async (
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var currentRules = await store.GetBasicRulesAsync(cancellationToken);
            var updatedRules = await store.UpdateBasicRulesAsync(new UpdateBasicRulesRequest(
                    currentRules.DefaultProfile,
                    currentRules.BigPromptCharacterThreshold,
                    "big",
                    "small",
                    true,
                    true),
                cancellationToken);

            return Results.Ok(ToBasicRulesDto(updatedRules));
        });

        group.MapGet("/models", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var models = await store.GetModelRegistryAsync(cancellationToken);
            return models.Select(ToModelProfileDto).ToList();
        });

        group.MapPut("/models/{name}", async (
            string name,
            ModelProfileDto request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var updated = await store.UpsertModelRegistryEntryAsync(
                name,
                new UpsertModelRegistryEntryRequest(
                    request.Category,
                    request.Deployment,
                    request.ApiVersion,
                    request.Enabled),
                cancellationToken);

            return Results.Ok(ToModelProfileDto(updated));
        });

        group.MapGet("/rules/basic", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var rules = await store.GetBasicRulesAsync(cancellationToken);
            return ToBasicRulesDto(rules);
        });

        group.MapPut("/rules/basic", async (
            BasicRulesUpdateRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var rules = await store.UpdateBasicRulesAsync(new UpdateBasicRulesRequest(
                    request.DefaultProfile,
                    request.BigPromptCharacterThreshold,
                    request.BigProfile,
                    request.StreamingProfile,
                    request.PreferBigWhenSystemMessageExists,
                    request.PreferStreamingProfileWhenStreaming),
                cancellationToken);

            return Results.Ok(ToBasicRulesDto(rules));
        });

        group.MapGet("/rules", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var rules = await store.GetBasicRulesAsync(cancellationToken);
            return ToLegacyRuleDtos(rules);
        });

        group.MapPost("/rules", async (
            RuleUpsertRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var rules = await ApplyLegacyRuleChangeAsync(store, request, cancellationToken);
            var selected = ToLegacyRuleDtos(rules).FirstOrDefault(rule =>
                string.Equals(rule.Condition, request.Condition, StringComparison.OrdinalIgnoreCase));

            return selected is null ? Results.BadRequest() : Results.Ok(selected);
        });

        group.MapPut("/rules/{id:int}", async (
            int id,
            RuleUpsertRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var rules = await ApplyLegacyRuleChangeAsync(store, request, cancellationToken);
            var selected = ToLegacyRuleDtos(rules).FirstOrDefault(rule => rule.Id == id);

            return selected is null ? Results.NotFound() : Results.Ok(selected);
        });

        group.MapDelete("/rules/{id:int}", async (int id, IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var currentRules = await store.GetBasicRulesAsync(cancellationToken);
            var updatedRules = id switch
            {
                10 => currentRules with { PreferBigWhenSystemMessageExists = false },
                20 => currentRules with { PreferStreamingProfileWhenStreaming = false },
                _ => currentRules
            };

            await store.UpdateBasicRulesAsync(new UpdateBasicRulesRequest(
                updatedRules.DefaultProfile,
                updatedRules.BigPromptCharacterThreshold,
                updatedRules.BigProfile,
                updatedRules.StreamingProfile,
                updatedRules.PreferBigWhenSystemMessageExists,
                updatedRules.PreferStreamingProfileWhenStreaming), cancellationToken);

            return Results.NoContent();
        });

        group.MapPost("/playground/evaluate", async (
            PlaygroundRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var jsonRequest = new JsonObject
            {
                ["stream"] = request.Stream
            };

            if (!string.IsNullOrWhiteSpace(request.RequestedProfile))
            {
                jsonRequest["model"] = request.RequestedProfile.Trim();
            }

            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(request.SystemMessage))
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = request.SystemMessage.Trim()
                });
            }

            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = request.Prompt
            });
            jsonRequest["messages"] = messages;

            var routingOptions = await store.GetRoutingOptionsAsync(cancellationToken);
            var decision = BasicModelRouter.SelectModel(jsonRequest, routingOptions);
            jsonRequest["model"] = decision.Profile.Deployment;

            var promptCharacters = request.Prompt.Length + (request.SystemMessage?.Length ?? 0);
            return Results.Ok(new PlaygroundResponse(
                decision.ProfileName,
                decision.Profile.Deployment,
                decision.Reason,
                promptCharacters,
                jsonRequest));
        });

        group.MapGet("/system/validation", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var setup = await store.GetSetupWizardStateAsync(cancellationToken);
            var models = await store.GetModelRegistryAsync(cancellationToken);
            var rules = await store.GetBasicRulesAsync(cancellationToken);
            var validation = await store.ValidateSystemAsync(cancellationToken);

            var checks = new List<ValidationCheck>
            {
                new("setup-completed", setup.IsCompleted, setup.IsCompleted ? "Setup wizard completed." : "Run setup wizard first."),
                new("three-logical-profiles", models.Count >= 3, $"Found {models.Count} profile(s) in registry."),
                new("enabled-profile", models.Any(model => model.Enabled), "At least one model profile must be enabled."),
                new("deployment-configured", models.All(model => !string.IsNullOrWhiteSpace(model.Deployment)), "Each profile must define a deployment name."),
                new("rules-available", true, $"Default={rules.DefaultProfile}, Big={rules.BigProfile}, Streaming={rules.StreamingProfile}."),
                new("store-validation", validation.IsValid, validation.IsValid ? "Persistence validation passed." : string.Join(' ', validation.Errors))
            };

            checks.AddRange(validation.Warnings.Select(warning => new ValidationCheck("warning", true, warning)));

            return Results.Ok(new ValidationResponse(checks));
        });

        return endpoints;
    }

    private static ModelProfileDto ToModelProfileDto(ModelRegistryEntry model) =>
        new(model.ProfileName, model.DisplayName, model.Deployment, model.ApiVersion, model.Enabled);

    private static BasicRulesDto ToBasicRulesDto(BasicRulesConfiguration rules) =>
        new(
            rules.DefaultProfile,
            rules.BigPromptCharacterThreshold,
            rules.BigProfile,
            rules.StreamingProfile,
            rules.PreferBigWhenSystemMessageExists,
            rules.PreferStreamingProfileWhenStreaming,
            rules.UpdatedAtUtc);

    private static IReadOnlyList<RuleDto> ToLegacyRuleDtos(BasicRulesConfiguration rules) =>
    [
        new RuleDto(
            10,
            "Prefer big when system message exists",
            "Uses the big profile for requests with a system message.",
            "system-message-present",
            rules.BigProfile,
            10,
            rules.PreferBigWhenSystemMessageExists),
        new RuleDto(
            20,
            "Prefer small for streaming",
            "Uses the streaming profile for streaming responses.",
            "streaming-request",
            rules.StreamingProfile,
            20,
            rules.PreferStreamingProfileWhenStreaming),
        new RuleDto(
            99,
            "Fallback to default profile",
            "Default route when no specific rule applies.",
            "fallback",
            rules.DefaultProfile,
            99,
            true)
    ];

    private static async Task<BasicRulesConfiguration> ApplyLegacyRuleChangeAsync(
        IRoutingConfigurationStore store,
        RuleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var currentRules = await store.GetBasicRulesAsync(cancellationToken);
        var normalizedCondition = request.Condition.Trim().ToLowerInvariant();

        var updatedRules = normalizedCondition switch
        {
            "system-message-present" => currentRules with
            {
                BigProfile = request.TargetProfile.Trim(),
                PreferBigWhenSystemMessageExists = request.Enabled
            },
            "streaming-request" => currentRules with
            {
                StreamingProfile = request.TargetProfile.Trim(),
                PreferStreamingProfileWhenStreaming = request.Enabled
            },
            "fallback" => currentRules with
            {
                DefaultProfile = request.TargetProfile.Trim()
            },
            _ => currentRules
        };

        return await store.UpdateBasicRulesAsync(new UpdateBasicRulesRequest(
            updatedRules.DefaultProfile,
            updatedRules.BigPromptCharacterThreshold,
            updatedRules.BigProfile,
            updatedRules.StreamingProfile,
            updatedRules.PreferBigWhenSystemMessageExists,
            updatedRules.PreferStreamingProfileWhenStreaming), cancellationToken);
    }
}
