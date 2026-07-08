using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapProfileEndpoints(RouteGroupBuilder group)
    {
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
    }
}
