using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapModelEndpoints(RouteGroupBuilder group)
    {
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
    }
}
