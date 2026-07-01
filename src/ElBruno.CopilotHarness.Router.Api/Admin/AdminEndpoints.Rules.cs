using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapRuleEndpoints(RouteGroupBuilder group)
    {
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

        group.MapGet("/rules/analyzer-prompt", async (
            IRequestRoutingService routingService,
            CancellationToken cancellationToken) =>
        {
            var routingOptions = await routingService.GetRoutingOptionsAsync(cancellationToken);
            var semanticRules = BasicModelRouter.GetSemanticRules(routingOptions);
            var processor = routingOptions.Profiles
                .FirstOrDefault(entry => entry.Value is { IsProcessor: true, Enabled: true });
            var hasProcessor = processor.Value is not null;
            var systemPrompt = semanticRules.Count == 0
                ? "No semantic rules are enabled yet. Add at least one semantic rule to see the local analyzer prompt."
                : SemanticRuleAnalyzer.BuildAnalyzerSystemPrompt(semanticRules);

            return Results.Ok(new RulesAnalyzerPromptResponse(
                hasProcessor,
                hasProcessor ? processor.Key : null,
                semanticRules.Count,
                systemPrompt));
        });

        group.MapPost("/rules/test", async (
            RuleTestRequest request,
            IRequestRoutingService routingService,
            IExecutionTraceStore traceStore,
            CancellationToken cancellationToken) =>
        {
            var jsonRequest = BuildEvaluationRequest(request.Prompt, request.SystemMessage, request.Stream, request.RequestedModel);
            var routingSelection = await routingService.SelectModelWithTraceAsync(jsonRequest, cancellationToken);
            var decision = routingSelection.Decision;
            var promptCharacters = (request.Prompt?.Length ?? 0) + (request.SystemMessage?.Length ?? 0);

            // Pull the rich facts the workflow recorded on the trace (same source the Live Routing feed uses).
            string? semanticRule = null;
            string? semanticReason = null;
            string? userRequest = null;
            var decisionSource = "deterministic";
            var confidence = 0d;
            var intent = string.Empty;
            var complexity = string.Empty;

            if (traceStore.TryGet(routingSelection.TraceId, out var trace))
            {
                semanticRule = GetContextValue(trace, "semantic.matchedRule");
                semanticReason = GetContextValue(trace, "semantic.reason");
                var rawUserMessage = GetContextValue(trace, "request.rawUserMessage");
                // Show the cleaned typed request (what the local model actually evaluated), not the wrapper.
                userRequest = string.IsNullOrWhiteSpace(rawUserMessage)
                    ? rawUserMessage
                    : BasicModelRouter.ExtractTypedUserMessage(rawUserMessage);
                intent = trace.Classification.Intent;
                complexity = trace.Classification.Complexity;
                confidence = trace.Classification.Confidence;
                decisionSource = !string.IsNullOrWhiteSpace(semanticRule)
                    ? GetContextValue(trace, "semantic.source") ?? "processor-model"
                    : GetContextValue(trace, "classifier.source")
                        ?? (string.IsNullOrWhiteSpace(trace.Classification.Source) ? "deterministic" : trace.Classification.Source);

                if (double.TryParse(GetContextValue(trace, "semantic.confidence"), out var semConfidence))
                {
                    confidence = semConfidence;
                }
            }

            var isSemantic = !string.IsNullOrWhiteSpace(semanticRule);
            var matchedRule = isSemantic ? semanticRule : ExtractMatchedRuleName(decision.Reason);

            // Only the local analyzer prompt is meaningful for semantic routing.
            string? analyzerPrompt = null;
            if (isSemantic)
            {
                var routingOptions = await routingService.GetRoutingOptionsAsync(cancellationToken);
                var semanticRules = BasicModelRouter.GetSemanticRules(routingOptions);
                if (semanticRules.Count > 0)
                {
                    analyzerPrompt = SemanticRuleAnalyzer.BuildAnalyzerSystemPrompt(semanticRules);
                }
            }

            return Results.Ok(new RuleTestResponse(
                matchedRule,
                decision.ProfileName,
                decision.Reason,
                promptCharacters,
                userRequest,
                isSemantic,
                decisionSource,
                confidence,
                intent,
                complexity,
                semanticReason,
                analyzerPrompt));
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
    }
}
