using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints, bool requireAuthorization)
    {
        var group = endpoints.MapGroup("/admin");

        if (requireAuthorization)
        {
            group.RequireAuthorization("AdminOnly");
        }

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

        // ── Model registry (multi-provider connections) ───────────────────────

        group.MapGet("/models", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var models = await store.GetModelsAsync(cancellationToken);
            return models.Select(ToModelConnectionDto).ToList();
        });

        group.MapGet("/models/{id}", async (
            string id,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var model = await store.GetModelAsync(id, cancellationToken);
            return model is null ? Results.NotFound() : Results.Ok(ToModelConnectionDto(model));
        });

        group.MapPost("/models", async (
            ModelConnectionUpsertRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Model name is required.");
            }

            var created = await store.UpsertModelAsync(null, ToUpsertModelConnectionRequest(request), cancellationToken);
            return Results.Created($"/admin/models/{created.Id}", ToModelConnectionDto(created));
        });

        group.MapPut("/models/{id}", async (
            string id,
            ModelConnectionUpsertRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var existing = await store.GetModelAsync(id, cancellationToken);
            if (existing is null)
            {
                return Results.NotFound();
            }

            var updated = await store.UpsertModelAsync(id, ToUpsertModelConnectionRequest(request), cancellationToken);
            return Results.Ok(ToModelConnectionDto(updated));
        });

        group.MapDelete("/models/{id}", async (
            string id,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var deleted = await store.DeleteModelAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/models/{id}/test", async (
            string id,
            IRoutingConfigurationStore store,
            IChatCompletionsProviderFactory providerFactory,
            CancellationToken cancellationToken) =>
        {
            var profile = await store.ResolveModelConnectionAsync(id, cancellationToken);
            if (profile is null)
            {
                return Results.NotFound();
            }

            var result = await ProbeModelAsync(providerFactory, profile, cancellationToken);
            return Results.Ok(new ModelConnectionTestResponse(result.Success, result.Message, result.LatencyMs));
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
            var rules = await store.GetRulesAsync(cancellationToken);
            return rules.Select(ToRoutingRuleDto).ToList();
        });

        group.MapGet("/rules/default", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var defaultModel = await store.GetDefaultModelAsync(cancellationToken);
            return Results.Ok(new DefaultModelDto(defaultModel.ModelName, defaultModel.UpdatedAtUtc));
        });

        group.MapPut("/rules/default", async (
            SetDefaultModelRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ModelName))
            {
                return Results.BadRequest("ModelName is required.");
            }

            var updated = await store.SetDefaultModelAsync(request.ModelName.Trim(), cancellationToken);
            return Results.Ok(new DefaultModelDto(updated.ModelName, updated.UpdatedAtUtc));
        });

        group.MapPost("/rules/wizard", async (IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var rules = await store.GenerateStarterRulesAsync(cancellationToken);
            return Results.Ok(rules.Select(ToRoutingRuleDto).ToList());
        });

        group.MapPost("/rules/test", async (
            RuleTestRequest request,
            IRequestRoutingService routingService,
            CancellationToken cancellationToken) =>
        {
            var jsonRequest = BuildEvaluationRequest(request.Prompt, request.SystemMessage, request.Stream, request.RequestedModel);
            var routingSelection = await routingService.SelectModelWithTraceAsync(jsonRequest, cancellationToken);
            var decision = routingSelection.Decision;
            var matchedRule = ExtractMatchedRuleName(decision.Reason);
            var promptCharacters = (request.Prompt?.Length ?? 0) + (request.SystemMessage?.Length ?? 0);

            return Results.Ok(new RuleTestResponse(matchedRule, decision.ProfileName, decision.Reason, promptCharacters));
        });

        group.MapGet("/rules/{id:int}", async (
            int id,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            var rule = await store.GetRuleAsync(id, cancellationToken);
            return rule is null ? Results.NotFound() : Results.Ok(ToRoutingRuleDto(rule));
        });

        group.MapPost("/rules", async (
            RoutingRuleUpsertRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Rule name is required.");
            }

            if (!TryParseConditionType(request.ConditionType, out var conditionType))
            {
                return Results.BadRequest($"Unknown condition type '{request.ConditionType}'.");
            }

            var created = await store.CreateRuleAsync(
                new UpsertRoutingRuleRequest(
                    request.Name.Trim(),
                    request.Description ?? string.Empty,
                    conditionType,
                    request.ConditionValue ?? string.Empty,
                    request.TargetModel?.Trim() ?? string.Empty,
                    request.Priority,
                    request.Enabled),
                cancellationToken);

            return Results.Created($"/admin/rules/{created.Id}", ToRoutingRuleDto(created));
        });

        group.MapPut("/rules/{id:int}", async (
            int id,
            RoutingRuleUpsertRequest request,
            IRoutingConfigurationStore store,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseConditionType(request.ConditionType, out var conditionType))
            {
                return Results.BadRequest($"Unknown condition type '{request.ConditionType}'.");
            }

            var updated = await store.UpdateRuleAsync(
                id,
                new UpsertRoutingRuleRequest(
                    request.Name.Trim(),
                    request.Description ?? string.Empty,
                    conditionType,
                    request.ConditionValue ?? string.Empty,
                    request.TargetModel?.Trim() ?? string.Empty,
                    request.Priority,
                    request.Enabled),
                cancellationToken);

            return updated is null ? Results.NotFound() : Results.Ok(ToRoutingRuleDto(updated));
        });

        group.MapDelete("/rules/{id:int}", async (int id, IRoutingConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var deleted = await store.DeleteRuleAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/playground/evaluate", async (
            PlaygroundRequest request,
            IRequestRoutingService routingService,
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

            var routingSelection = await routingService.SelectModelWithTraceAsync(jsonRequest, cancellationToken);
            var decision = routingSelection.Decision;
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

        group.MapGet("/telemetry/clients", (IExecutionTraceStore traceStore, int? limit) =>
        {
            var normalizedLimit = Math.Clamp(limit ?? 200, 1, 500);
            var traces = traceStore.GetRecent(normalizedLimit);
            var now = DateTimeOffset.UtcNow;

            var clients = traces
                .GroupBy(trace => GetContextValue(trace, "request.client.id") ?? "unknown", StringComparer.OrdinalIgnoreCase)
                .Select(grouped =>
                {
                    var ordered = grouped.OrderByDescending(item => item.CreatedAtUtc).ToList();
                    var latest = ordered[0];
                    var clientId = grouped.Key;

                    return new ConnectedClientTelemetryDto(
                        ClientId: clientId,
                        DisplayName: GetClientDisplayName(clientId),
                        Source: GetContextValue(latest, "request.client.source") ?? "unknown",
                        Version: GetContextValue(latest, "request.client.version"),
                        LastSeenUtc: latest.CreatedAtUtc,
                        RequestsLastHour: grouped.Count(item => item.CreatedAtUtc >= now.AddHours(-1)),
                        LastProfile: latest.Decision.ProfileName,
                        LastDeployment: latest.Decision.Profile.Deployment);
                })
                .OrderByDescending(client => client.LastSeenUtc)
                .ToList();

            return Results.Ok(new ConnectedClientsResponse(now, clients));
        });

        group.MapGet("/telemetry/requests", (IExecutionTraceStore traceStore, int? limit) =>
        {
            var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
            var traces = traceStore.GetRecent(normalizedLimit);

            var requests = traces
                .OrderByDescending(trace => trace.CreatedAtUtc)
                .Take(normalizedLimit)
                .Select(trace =>
                {
                    var clientId = GetContextValue(trace, "request.client.id") ?? "unknown";
                    return new LiveRequestTelemetryDto(
                        TraceId: trace.TraceId,
                        CreatedAtUtc: trace.CreatedAtUtc,
                        Endpoint: GetContextValue(trace, "request.endpoint") ?? "unknown",
                        ClientId: clientId,
                        ClientDisplayName: GetClientDisplayName(clientId),
                        ClientVersion: GetContextValue(trace, "request.client.version"),
                        Profile: trace.Decision.ProfileName,
                        Deployment: trace.Decision.Profile.Deployment,
                        Reason: trace.Decision.Reason,
                        ClassificationIntent: trace.Classification.Intent,
                        ClassificationComplexity: trace.Classification.Complexity);
                })
                .ToList();

            return Results.Ok(new LiveRequestsResponse(DateTimeOffset.UtcNow, requests));
        });

        group.MapGet("/telemetry/feed", (
            IExecutionTraceStore traceStore,
            IOptions<TelemetryOptions> telemetryOptions,
            int? limit) =>
        {
            var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
            var traces = traceStore.GetRecent(normalizedLimit);

            var requests = traces
                .OrderByDescending(trace => trace.CreatedAtUtc)
                .Take(normalizedLimit)
                .Select(trace =>
                {
                    var clientId = GetContextValue(trace, "request.client.id") ?? "unknown";
                    var matchedRule = ExtractMatchedRuleName(trace.Decision.Reason);
                    int.TryParse(GetContextValue(trace, "request.promptCharacters"), out var promptChars);
                    return new RoutedRequestView(
                        TraceId: trace.TraceId,
                        CreatedAtUtc: trace.CreatedAtUtc,
                        ClientId: clientId,
                        ClientDisplayName: GetClientDisplayName(clientId),
                        Endpoint: GetContextValue(trace, "request.endpoint") ?? "unknown",
                        Stream: string.Equals(GetContextValue(trace, "request.stream"), "true", StringComparison.OrdinalIgnoreCase),
                        RequestedModel: GetContextValue(trace, "request.requestedModel"),
                        SelectedModel: trace.Decision.ProfileName,
                        Deployment: trace.Decision.Profile.Deployment,
                        MatchedRuleName: matchedRule,
                        Reason: trace.Decision.Reason,
                        Explanation: BuildExplanation(trace, matchedRule),
                        PromptPreview: GetContextValue(trace, PromptPrivacy.PromptPreviewFactKey),
                        PromptCharacters: promptChars,
                        ClassificationIntent: trace.Classification.Intent,
                        ClassificationComplexity: trace.Classification.Complexity);
                })
                .ToList();

            return Results.Ok(new RoutingFeedResponse(
                DateTimeOffset.UtcNow,
                telemetryOptions.Value.CapturePromptText,
                requests));
        });

        group.MapGet("/traces/{traceId}", (string traceId, IExecutionTraceStore traceStore) =>
        {
            if (!traceStore.TryGet(traceId, out var trace))
            {
                return Results.NotFound();
            }

            return Results.Ok(RoutingTraceResponseMapper.ToResponse(trace));
        });

        group.MapGet("/dashboard/snapshot", (IClientRequestActivityStore requestActivityStore) =>
        {
            var now = DateTimeOffset.UtcNow;
            var snapshot = requestActivityStore.GetSnapshot(now);
            return Results.Ok(new DashboardSnapshotResponse(
                snapshot.ConnectedClients.Select(ToConnectedClientDto).ToList(),
                snapshot.LiveRequests.Select(ToLiveRequestDto).ToList(),
                now));
        });

        group.MapGet("/operations/status", async (
            HealthCheckService healthCheckService,
            IOptions<PersistenceOptions> persistenceOptions,
            IHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            var report = await healthCheckService.CheckHealthAsync(cancellationToken);
            var healthChecks = report.Entries
                .Select(entry => new OperationalHealthDto(
                    entry.Key,
                    entry.Value.Status.ToString(),
                    entry.Value.Description ?? entry.Value.Exception?.Message ?? "No additional details."))
                .ToList();

            return Results.Ok(new OperationsStatusResponse(
                DateTimeOffset.UtcNow,
                new OperationalSignalDto(
                    "Authentication",
                    "Not configured",
                    "The Phase 6 auth surface is not enabled in this build.",
                    "Wire an identity provider before turning on admin authentication."),
                new OperationalSignalDto(
                    "Rate limiting",
                    "Disabled",
                    "No rate limiter is registered yet.",
                    "Add a request budget and expose counters from the gateway."),
                new OperationalSignalDto(
                    "Retry / backoff",
                    "Partial",
                    "Shared HttpClient resilience is enabled, but app-specific backoff is not tuned.",
                    "Refine the retry policy when production traffic rules are available."),
                new OperationalSignalDto(
                    "Background jobs",
                    "Not configured",
                    "No schedulers or workers are registered in the current app graph.",
                    "Add a queue-backed worker and surface queue depth here."),
                new InfrastructureStatusDto(
                    "SQLite",
                    "None",
                    persistenceOptions.Value.DatabasePath,
                    environment.EnvironmentName),
                healthChecks));
        });

        group.MapGet("/clients/connected", (IClientRequestActivityStore requestActivityStore) =>
        {
            var snapshot = requestActivityStore.GetSnapshot(DateTimeOffset.UtcNow);
            return Results.Ok(snapshot.ConnectedClients.Select(ToConnectedClientDto).ToList());
        });

        group.MapGet("/requests/live", (IClientRequestActivityStore requestActivityStore) =>
        {
            var snapshot = requestActivityStore.GetSnapshot(DateTimeOffset.UtcNow);
            return Results.Ok(snapshot.LiveRequests.Select(ToLiveRequestDto).ToList());
        });

        // ── Phase 8 – Admin.Web-compatible bridge endpoints ────────────────────

        group.MapGet("/recommendations/pending", async (
            IApprovalWorkflowStore approvalStore,
            CancellationToken cancellationToken) =>
        {
            var pending = await approvalStore.ListAsync("pending", 0, 100, cancellationToken);
            var dtos = pending.Select(ApprovalToRecommendationDto).ToList();
            return Results.Ok(new RecommendationsResponse(dtos));
        });

        group.MapPost("/recommendations/decision", async (
            ReviewRecommendationRequest request,
            IApprovalWorkflowStore approvalStore,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RecommendationId))
            {
                return Results.BadRequest("RecommendationId is required.");
            }

            var approved = string.Equals(request.Decision, "approve", StringComparison.OrdinalIgnoreCase);

            try
            {
                await approvalStore.ReviewAsync(request.RecommendationId,
                    new ReviewApprovalRequest(approved, "admin", request.Reason),
                    cancellationToken);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        group.MapGet("/profiles/teams", async (
            ITeamProjectProfileStore teamStore,
            CancellationToken cancellationToken) =>
        {
            var teams = await teamStore.ListTeamsAsync(cancellationToken);
            return Results.Ok(teams.Select(ToAdminTeamDto).ToList());
        });

        group.MapPost("/profiles/teams", async (
            AdminCreateTeamRequest request,
            ITeamProjectProfileStore teamStore,
            CancellationToken cancellationToken) =>
        {
            var slug = request.Name.Trim().ToLowerInvariant().Replace(' ', '-');
            var preferredModelsJson = System.Text.Json.JsonSerializer.Serialize(request.PreferredModels);
            var defaultProfile = request.PreferredModels.Count > 0 ? request.PreferredModels[0] : "small";

            await teamStore.UpsertTeamAsync(slug, new UpsertTeamProfileRequest(
                request.Name, defaultProfile, preferredModelsJson, true), cancellationToken);

            return Results.Created($"/admin/profiles/teams/{slug}", null);
        });

        group.MapDelete("/profiles/teams/{name}", async (
            string name,
            ITeamProjectProfileStore teamStore,
            CancellationToken cancellationToken) =>
        {
            var deleted = await teamStore.DeleteTeamAsync(name, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/profiles/projects", async (
            ITeamProjectProfileStore teamStore,
            CancellationToken cancellationToken) =>
        {
            var projects = await teamStore.ListProjectsAsync(null, cancellationToken);
            return Results.Ok(projects.Select(ToAdminProjectDto).ToList());
        });

        group.MapPost("/profiles/projects", async (
            AdminCreateProjectRequest request,
            ITeamProjectProfileStore teamStore,
            CancellationToken cancellationToken) =>
        {
            var slug = request.Name.Trim().ToLowerInvariant().Replace(' ', '-');
            var tagsJson = System.Text.Json.JsonSerializer.Serialize(request.Tags);
            var overrideProfile = string.IsNullOrWhiteSpace(request.OverrideProfile) ? "small" : request.OverrideProfile;

            await teamStore.UpsertProjectAsync(slug, new UpsertProjectProfileRequest(
                request.TeamProfile, request.Name, overrideProfile, tagsJson, true), cancellationToken);

            return Results.Created($"/admin/profiles/projects/{slug}", null);
        });

        group.MapDelete("/profiles/projects/{name}", async (
            string name,
            ITeamProjectProfileStore teamStore,
            CancellationToken cancellationToken) =>
        {
            var deleted = await teamStore.DeleteProjectAsync(name, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/benchmarks/status", async (
            IBenchmarkStore benchmarkStore,
            CancellationToken cancellationToken) =>
        {
            var recentRuns = await benchmarkStore.ListRunsAsync(0, 10, cancellationToken);

            var runDtos = recentRuns.Select(r => new AdminBenchmarkRunDto(
                r.RunId,
                r.Status,
                "scheduled",
                r.CreatedAtUtc,
                r.CompletedAtUtc,
                r.TotalItems,
                r.CompletedItems,
                0)).ToList();

            var last = recentRuns.FirstOrDefault(r => r.CompletedAtUtc.HasValue);
            return Results.Ok(new BenchmarkStatusResponse(
                recentRuns.Any(r => r.Status == "running") ? "Running" : "Idle",
                last?.CompletedAtUtc,
                null,
                runDtos,
                []));
        });

        group.MapGet("/rules/confidence", async (
            IRuleConfidenceStore confidenceStore,
            CancellationToken cancellationToken) =>
        {
            var scores = await confidenceStore.GetCurrentScoresAsync(cancellationToken);
            var dtos = scores.Select(s => new AdminRuleConfidenceDto(
                s.RuleKey,
                s.ConfidenceScore,
                s.ConfidenceScore >= 0.8 ? "stable" : s.ConfidenceScore >= 0.5 ? "declining" : "low",
                s.RecordedAtUtc)).ToList();
            return Results.Ok(new RulesConfidenceResponse(dtos));
        });

        return endpoints;
    }

    private static ModelConnectionDto ToModelConnectionDto(ModelConnectionRecord model) =>
        new(
            model.Id,
            model.Name,
            ProviderTypeToString(model.ProviderType),
            model.Endpoint,
            model.ModelName,
            model.ApiVersion,
            model.HasApiKey,
            model.Enabled,
            model.UpdatedAtUtc);

    private static UpsertModelConnectionRequest ToUpsertModelConnectionRequest(ModelConnectionUpsertRequest request) =>
        new(
            request.Name.Trim(),
            ParseProviderType(request.Type),
            request.Endpoint?.Trim() ?? string.Empty,
            request.ModelName?.Trim() ?? string.Empty,
            request.ApiVersion?.Trim() ?? string.Empty,
            request.ApiKey,
            request.Enabled);

    private static RoutingRuleDto ToRoutingRuleDto(RoutingRuleRecord rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Description,
            rule.ConditionType.ToString(),
            rule.ConditionValue,
            rule.TargetModel,
            rule.Priority,
            rule.Enabled,
            rule.UpdatedAtUtc);

    private static string ProviderTypeToString(ModelProviderType type) =>
        type switch
        {
            ModelProviderType.Ollama => "ollama",
            ModelProviderType.AzureOpenAI => "azure-openai",
            _ => type.ToString().ToLowerInvariant()
        };

    private static ModelProviderType ParseProviderType(string? type) =>
        (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ollama" => ModelProviderType.Ollama,
            "azure-openai" => ModelProviderType.AzureOpenAI,
            "azureopenai" => ModelProviderType.AzureOpenAI,
            "azure" => ModelProviderType.AzureOpenAI,
            "foundry" => ModelProviderType.AzureOpenAI,
            _ => ModelProviderType.AzureOpenAI
        };

    private static bool TryParseConditionType(string? value, out RoutingRuleConditionType conditionType) =>
        Enum.TryParse(value, ignoreCase: true, out conditionType);

    private static JsonObject BuildEvaluationRequest(string? prompt, string? systemMessage, bool stream, string? requestedModel)
    {
        var jsonRequest = new JsonObject
        {
            ["stream"] = stream
        };

        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            jsonRequest["model"] = requestedModel.Trim();
        }

        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemMessage.Trim()
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = prompt ?? string.Empty
        });
        jsonRequest["messages"] = messages;

        return jsonRequest;
    }

    private static string? ExtractMatchedRuleName(string reason)
    {
        const string marker = "Matched rule '";
        var start = reason.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = reason.IndexOf('\'', start);
        return end < 0 ? null : reason[start..end];
    }

    private static string BuildExplanation(RoutingExecutionTrace trace, string? matchedRule)
    {
        var model = trace.Decision.ProfileName;
        var classification = trace.Classification;
        var classificationSuffix = string.IsNullOrWhiteSpace(classification.Intent)
            ? string.Empty
            : $" Classified as {classification.Intent}/{classification.Complexity}.";

        if (!string.IsNullOrWhiteSpace(matchedRule))
        {
            return $"Routed to '{model}' because rule '{matchedRule}' matched.{classificationSuffix}";
        }

        var reason = trace.Decision.Reason;
        if (reason.Contains("Explicit", StringComparison.OrdinalIgnoreCase))
        {
            return $"Routed to '{model}' because the client explicitly requested it.{classificationSuffix}";
        }

        if (reason.Contains("Default", StringComparison.OrdinalIgnoreCase))
        {
            return $"Routed to '{model}' as the configured default model.{classificationSuffix}";
        }

        if (reason.Contains("Fallback", StringComparison.OrdinalIgnoreCase))
        {
            return $"Routed to '{model}' as a fallback (no rule or default matched).{classificationSuffix}";
        }

        return $"Routed to '{model}'. {reason}{classificationSuffix}".Trim();
    }

    private static async Task<ModelConnectionTestResult> ProbeModelAsync(
        IChatCompletionsProviderFactory providerFactory,
        ModelProfileOptions profile,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = profile.Deployment,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "ping" }
            },
            ["max_tokens"] = 1
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await providerFactory
                .GetProvider(profile)
                .SendChatCompletionsAsync(payload, profile, stream: false, cancellationToken);
            stopwatch.Stop();

            return response.IsSuccessStatusCode
                ? new ModelConnectionTestResult(true, $"Connection succeeded ({(int)response.StatusCode}).", stopwatch.Elapsed.TotalMilliseconds)
                : new ModelConnectionTestResult(false, $"Upstream returned {(int)response.StatusCode} {response.ReasonPhrase}.", stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ModelConnectionTestResult(false, $"Connection failed: {ex.Message}", stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static BasicRulesDto ToBasicRulesDto(BasicRulesConfiguration rules) =>
        new(
            rules.DefaultProfile,
            rules.BigPromptCharacterThreshold,
            rules.BigProfile,
            rules.StreamingProfile,
            rules.PreferBigWhenSystemMessageExists,
            rules.PreferStreamingProfileWhenStreaming,
            rules.UpdatedAtUtc);

    private static ConnectedClientDto ToConnectedClientDto(ConnectedClientSnapshot snapshot) =>
        new(
            snapshot.Client,
            snapshot.IsConnected,
            snapshot.ActiveRequests,
            snapshot.RequestsLastFiveMinutes,
            snapshot.LastSeenAtUtc);

    private static LiveRequestDto ToLiveRequestDto(LiveRequestSnapshot snapshot) =>
        new(
            snapshot.RequestId,
            snapshot.Endpoint,
            snapshot.Client,
            snapshot.Stream,
            snapshot.RequestedModel,
            snapshot.SelectedProfile,
            snapshot.SelectedDeployment,
            snapshot.TraceId,
            snapshot.StartedAtUtc,
            snapshot.ElapsedMs);

    private static string? GetContextValue(RoutingExecutionTrace trace, string key) =>
        trace.Context.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string GetClientDisplayName(string clientId) =>
        clientId.ToLowerInvariant() switch
        {
            "vscode" => "VS Code",
            "copilot-cli" => "Copilot CLI",
            "copilot-app" => "Copilot App",
            _ => "Unknown"
        };

    // ── Phase 8 mapping helpers ───────────────────────────────────────────────

    private static RuleRecommendationDto ApprovalToRecommendationDto(ApprovalRequestSummary a)
    {
        string ruleKey = a.ChangeType, currentValue = "", recommendedValue = "", rationale = a.Description;
        double confidence = 0.75;

        try
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(a.PayloadJson);
            ruleKey = TryGetString(payload, "ruleKey") ?? a.ChangeType;
            currentValue = TryGetString(payload, "currentValue") ?? "";
            recommendedValue = TryGetString(payload, "recommendedValue") ?? "";
            rationale = TryGetString(payload, "rationale") ?? a.Description;
            if (payload.TryGetProperty("confidence", out var conf)) confidence = conf.GetDouble();
        }
        catch { /* fall through to defaults */ }

        return new RuleRecommendationDto(a.ApprovalId, ruleKey, currentValue, recommendedValue, rationale, confidence, a.Status, a.CreatedAtUtc);
    }

    private static string? TryGetString(System.Text.Json.JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String
            ? prop.GetString()
            : null;

    private static AdminTeamProfileDto ToAdminTeamDto(TeamProfileSummary t)
    {
        IReadOnlyList<string> preferredModels;
        try
        {
            preferredModels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.RulesJson) ?? [t.DefaultProfile];
        }
        catch
        {
            preferredModels = [t.DefaultProfile];
        }

        return new AdminTeamProfileDto(t.TeamId, t.DisplayName, preferredModels, false);
    }

    private static AdminProjectProfileDto ToAdminProjectDto(ProjectProfileSummary p)
    {
        IReadOnlyList<string> tags;
        try
        {
            tags = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.RulesJson) ?? [];
        }
        catch
        {
            tags = [];
        }

        return new AdminProjectProfileDto(p.ProjectId, p.TeamId, tags, p.DefaultProfile);
    }
}
