using System.Text.Json.Nodes;
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
