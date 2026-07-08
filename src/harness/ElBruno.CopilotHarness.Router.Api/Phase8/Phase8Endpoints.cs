using ElBruno.CopilotHarness.Router.Core.Persistence;

namespace ElBruno.CopilotHarness.Router.Api.Phase8;

public static class Phase8Endpoints
{
    public static IEndpointRouteBuilder MapPhase8Endpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization)
    {
        var group = endpoints.MapGroup("/admin/phase8");

        if (requireAuthorization)
            group.RequireAuthorization("AdminOnly");

        MapShadowRouting(group);
        MapRuleConfidence(group);
        MapBenchmarks(group);
        MapApprovals(group);
        MapTeamProfiles(group);
        MapProjectProfiles(group);

        return endpoints;
    }

    // ─── Shadow Routing ───────────────────────────────────────────────────────

    private static void MapShadowRouting(RouteGroupBuilder group)
    {
        group.MapGet("/shadow/config", async (IShadowRoutingStore store, CancellationToken ct) =>
        {
            var config = await store.GetConfigAsync(ct);
            return Results.Ok(new ShadowConfigDto(config.Enabled, config.ShadowProfile, config.SamplingRate));
        })
        .WithName("GetShadowConfig")
        .WithSummary("Get current shadow routing configuration.");

        group.MapPut("/shadow/config", async (
            ShadowConfigDto dto,
            IShadowRoutingStore store,
            CancellationToken ct) =>
        {
            var saved = await store.SaveConfigAsync(
                new ShadowRoutingConfig(dto.Enabled, dto.ShadowProfile, dto.SamplingRate), ct);
            return Results.Ok(new ShadowConfigDto(saved.Enabled, saved.ShadowProfile, saved.SamplingRate));
        })
        .WithName("UpdateShadowConfig")
        .WithSummary("Update shadow routing configuration.");

        group.MapGet("/shadow/results", async (
            IShadowRoutingStore store,
            int count = 50,
            CancellationToken ct = default) =>
        {
            var results = await store.GetRecentResultsAsync(count, ct);
            return Results.Ok(results.Select(ToShadowResultDto).ToList());
        })
        .WithName("GetShadowResults")
        .WithSummary("Get recent shadow routing results.");
    }

    // ─── Rule Confidence ──────────────────────────────────────────────────────

    private static void MapRuleConfidence(RouteGroupBuilder group)
    {
        group.MapGet("/rules/confidence", async (
            IRuleConfidenceStore store,
            CancellationToken ct) =>
        {
            var scores = await store.GetCurrentScoresAsync(ct);
            return Results.Ok(scores.Select(ToConfidenceDto).ToList());
        })
        .WithName("GetRuleConfidenceScores")
        .WithSummary("Get rule confidence scores over the last 7 days.");

        group.MapGet("/rules/confidence/{ruleKey}", async (
            string ruleKey,
            IRuleConfidenceStore store,
            CancellationToken ct) =>
        {
            var score = await store.GetScoreAsync(ruleKey, ct);
            return score is null ? Results.NotFound() : Results.Ok(ToConfidenceDto(score));
        })
        .WithName("GetRuleConfidenceScore")
        .WithSummary("Get confidence score for a specific rule key.");

        group.MapPost("/rules/confidence/{ruleKey}/record", async (
            string ruleKey,
            bool successful,
            IRuleConfidenceStore store,
            CancellationToken ct) =>
        {
            await store.RecordInvocationAsync(ruleKey, successful, ct);
            return Results.Ok();
        })
        .WithName("RecordRuleInvocation")
        .WithSummary("Record a rule invocation outcome for confidence scoring.");
    }

    // ─── Benchmarks ───────────────────────────────────────────────────────────

    private static void MapBenchmarks(RouteGroupBuilder group)
    {
        group.MapGet("/benchmark/runs", async (
            IBenchmarkStore store,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var runs = await store.ListRunsAsync(page, pageSize, ct);
            return Results.Ok(runs.Select(ToBenchmarkRunDto).ToList());
        })
        .WithName("ListBenchmarkRuns")
        .WithSummary("List benchmark runs with pagination.");

        group.MapPost("/benchmark/runs", async (
            CreateBenchmarkRunDto dto,
            IBenchmarkStore store,
            CancellationToken ct) =>
        {
            var items = dto.Items
                .Select(i => new BenchmarkPromptItem(i.ItemId, i.Prompt, i.SystemMessage))
                .ToList();

            var run = await store.CreateRunAsync(
                new CreateBenchmarkRunRequest(dto.Name, dto.Description, dto.Profiles, items), ct);

            return Results.Created($"/admin/phase8/benchmark/runs/{run.RunId}", ToBenchmarkRunDto(run));
        })
        .WithName("CreateBenchmarkRun")
        .WithSummary("Create a new benchmark run.");

        group.MapGet("/benchmark/runs/{runId}", async (
            string runId,
            IBenchmarkStore store,
            CancellationToken ct) =>
        {
            var run = await store.GetRunAsync(runId, ct);
            return run is null ? Results.NotFound() : Results.Ok(ToBenchmarkRunDto(run));
        })
        .WithName("GetBenchmarkRun")
        .WithSummary("Get a benchmark run by ID.");

        group.MapPut("/benchmark/runs/{runId}/status", async (
            string runId,
            string status,
            IBenchmarkStore store,
            CancellationToken ct) =>
        {
            try
            {
                var run = await store.UpdateRunStatusAsync(runId, status, ct);
                return Results.Ok(ToBenchmarkRunDto(run));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("UpdateBenchmarkRunStatus")
        .WithSummary("Update the status of a benchmark run.");

        group.MapPost("/benchmark/runs/{runId}/results", async (
            string runId,
            RecordBenchmarkResultDto dto,
            IBenchmarkStore store,
            CancellationToken ct) =>
        {
            var result = await store.RecordResultAsync(runId, new RecordBenchmarkResultRequest(
                dto.ItemId, dto.Profile, dto.Deployment, dto.PromptHash,
                dto.LatencyMs, dto.PromptTokens, dto.CompletionTokens,
                dto.StatusCode, dto.JudgeVerdict, dto.JudgeScore, dto.MetricsJson), ct);

            return Results.Created(
                $"/admin/phase8/benchmark/runs/{runId}/results/{result.Id}",
                ToBenchmarkResultDto(result));
        })
        .WithName("RecordBenchmarkResult")
        .WithSummary("Record a result item inside a benchmark run.");

        group.MapGet("/benchmark/runs/{runId}/results", async (
            string runId,
            IBenchmarkStore store,
            CancellationToken ct) =>
        {
            var results = await store.GetResultsAsync(runId, ct);
            return Results.Ok(results.Select(ToBenchmarkResultDto).ToList());
        })
        .WithName("GetBenchmarkResults")
        .WithSummary("Get all results for a benchmark run.");
    }

    // ─── Human Approval Workflow ──────────────────────────────────────────────

    private static void MapApprovals(RouteGroupBuilder group)
    {
        group.MapGet("/approvals", async (
            IApprovalWorkflowStore store,
            string? status = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var approvals = await store.ListAsync(status, page, pageSize, ct);
            return Results.Ok(approvals.Select(ToApprovalDto).ToList());
        })
        .WithName("ListApprovalRequests")
        .WithSummary("List approval requests, optionally filtered by status.");

        group.MapPost("/approvals", async (
            CreateApprovalDto dto,
            IApprovalWorkflowStore store,
            CancellationToken ct) =>
        {
            var approval = await store.CreateAsync(new CreateApprovalRequest(
                dto.ChangeType, dto.Title, dto.Description, dto.PayloadJson, dto.ExpiresAtUtc), ct);

            return Results.Created(
                $"/admin/phase8/approvals/{approval.ApprovalId}",
                ToApprovalDto(approval));
        })
        .WithName("CreateApprovalRequest")
        .WithSummary("Create a new human approval request.");

        group.MapGet("/approvals/{approvalId}", async (
            string approvalId,
            IApprovalWorkflowStore store,
            CancellationToken ct) =>
        {
            var approval = await store.GetAsync(approvalId, ct);
            return approval is null ? Results.NotFound() : Results.Ok(ToApprovalDto(approval));
        })
        .WithName("GetApprovalRequest")
        .WithSummary("Get an approval request by ID.");

        group.MapPut("/approvals/{approvalId}/review", async (
            string approvalId,
            ReviewApprovalDto dto,
            IApprovalWorkflowStore store,
            CancellationToken ct) =>
        {
            try
            {
                var approval = await store.ReviewAsync(approvalId,
                    new ReviewApprovalRequest(dto.Approved, dto.ReviewedBy, dto.ReviewNotes), ct);
                return Results.Ok(ToApprovalDto(approval));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ReviewApprovalRequest")
        .WithSummary("Approve or reject a pending approval request.");

        group.MapPost("/approvals/expire-overdue", async (
            IApprovalWorkflowStore store,
            CancellationToken ct) =>
        {
            var count = await store.ExpireOverdueAsync(ct);
            return Results.Ok(new { expiredCount = count });
        })
        .WithName("ExpireOverdueApprovals")
        .WithSummary("Expire all overdue pending approval requests.");
    }

    // ─── Team Profiles ────────────────────────────────────────────────────────

    private static void MapTeamProfiles(RouteGroupBuilder group)
    {
        group.MapGet("/teams", async (ITeamProjectProfileStore store, CancellationToken ct) =>
        {
            var teams = await store.ListTeamsAsync(ct);
            return Results.Ok(teams.Select(ToTeamDto).ToList());
        })
        .WithName("ListTeamProfiles")
        .WithSummary("List all team routing profiles.");

        group.MapGet("/teams/{teamId}", async (
            string teamId,
            ITeamProjectProfileStore store,
            CancellationToken ct) =>
        {
            var team = await store.GetTeamAsync(teamId, ct);
            return team is null ? Results.NotFound() : Results.Ok(ToTeamDto(team));
        })
        .WithName("GetTeamProfile")
        .WithSummary("Get a team routing profile by ID.");

        group.MapPut("/teams/{teamId}", async (
            string teamId,
            UpsertTeamProfileDto dto,
            ITeamProjectProfileStore store,
            CancellationToken ct) =>
        {
            var team = await store.UpsertTeamAsync(teamId,
                new UpsertTeamProfileRequest(dto.DisplayName, dto.DefaultProfile, dto.RulesJson, dto.Enabled), ct);
            return Results.Ok(ToTeamDto(team));
        })
        .WithName("UpsertTeamProfile")
        .WithSummary("Create or update a team routing profile.");

        group.MapDelete("/teams/{teamId}", async (
            string teamId,
            ITeamProjectProfileStore store,
            CancellationToken ct) =>
        {
            var deleted = await store.DeleteTeamAsync(teamId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteTeamProfile")
        .WithSummary("Delete a team routing profile.");
    }

    // ─── Project Profiles ─────────────────────────────────────────────────────

    private static void MapProjectProfiles(RouteGroupBuilder group)
    {
        group.MapGet("/projects", async (
            ITeamProjectProfileStore store,
            string? teamId = null,
            CancellationToken ct = default) =>
        {
            var projects = await store.ListProjectsAsync(teamId, ct);
            return Results.Ok(projects.Select(ToProjectDto).ToList());
        })
        .WithName("ListProjectProfiles")
        .WithSummary("List project routing profiles, optionally scoped to a team.");

        group.MapGet("/projects/{projectId}", async (
            string projectId,
            ITeamProjectProfileStore store,
            CancellationToken ct) =>
        {
            var project = await store.GetProjectAsync(projectId, ct);
            return project is null ? Results.NotFound() : Results.Ok(ToProjectDto(project));
        })
        .WithName("GetProjectProfile")
        .WithSummary("Get a project routing profile by ID.");

        group.MapPut("/projects/{projectId}", async (
            string projectId,
            UpsertProjectProfileDto dto,
            ITeamProjectProfileStore store,
            CancellationToken ct) =>
        {
            var project = await store.UpsertProjectAsync(projectId,
                new UpsertProjectProfileRequest(
                    dto.TeamId, dto.DisplayName, dto.DefaultProfile, dto.RulesJson, dto.Enabled), ct);
            return Results.Ok(ToProjectDto(project));
        })
        .WithName("UpsertProjectProfile")
        .WithSummary("Create or update a project routing profile.");

        group.MapDelete("/projects/{projectId}", async (
            string projectId,
            ITeamProjectProfileStore store,
            CancellationToken ct) =>
        {
            var deleted = await store.DeleteProjectAsync(projectId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProjectProfile")
        .WithSummary("Delete a project routing profile.");
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static ShadowResultDto ToShadowResultDto(ShadowRequestRecord r) =>
        new(r.ShadowId, r.OriginalTraceId, r.PrimaryProfile, r.ShadowProfile,
            r.PrimaryLatencyMs, r.ShadowLatencyMs, r.PrimaryStatusCode, r.ShadowStatusCode,
            r.OutcomeLabel, r.CreatedAtUtc, r.CompletedAtUtc);

    private static RuleConfidenceDto ToConfidenceDto(RuleConfidenceSummary s) =>
        new(s.RuleKey, s.TotalInvocations, s.SuccessfulInvocations, s.ConfidenceScore,
            s.WindowLabel, s.WindowStartUtc, s.WindowEndUtc, s.RecordedAtUtc);

    private static BenchmarkRunDto ToBenchmarkRunDto(BenchmarkRunSummary r) =>
        new(r.RunId, r.Name, r.Description, r.Profiles, r.Status,
            r.TotalItems, r.CompletedItems, r.CreatedAtUtc, r.StartedAtUtc, r.CompletedAtUtc);

    private static BenchmarkResultDto ToBenchmarkResultDto(BenchmarkResultRecord r) =>
        new(r.Id, r.RunId, r.ItemId, r.Profile, r.Deployment,
            r.LatencyMs, r.PromptTokens, r.CompletionTokens,
            r.StatusCode, r.JudgeVerdict, r.JudgeScore, r.CreatedAtUtc);

    private static ApprovalRequestDto ToApprovalDto(ApprovalRequestSummary a) =>
        new(a.ApprovalId, a.ChangeType, a.Title, a.Description, a.PayloadJson,
            a.Status, a.ReviewedBy, a.ReviewNotes,
            a.CreatedAtUtc, a.ReviewedAtUtc, a.ExpiresAtUtc);

    private static TeamProfileDto ToTeamDto(TeamProfileSummary t) =>
        new(t.TeamId, t.DisplayName, t.DefaultProfile, t.RulesJson,
            t.Enabled, t.CreatedAtUtc, t.UpdatedAtUtc);

    private static ProjectProfileDto ToProjectDto(ProjectProfileSummary p) =>
        new(p.ProjectId, p.TeamId, p.DisplayName, p.DefaultProfile,
            p.RulesJson, p.Enabled, p.CreatedAtUtc, p.UpdatedAtUtc);
}
