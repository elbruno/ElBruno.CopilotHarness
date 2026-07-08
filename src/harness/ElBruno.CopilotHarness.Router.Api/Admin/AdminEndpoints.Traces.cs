using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapTraceEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/traces/{traceId}", (string traceId, IExecutionTraceStore traceStore) =>
        {
            if (!traceStore.TryGet(traceId, out var trace))
            {
                return Results.NotFound();
            }

            return Results.Ok(RoutingTraceResponseMapper.ToResponse(trace));
        });

        group.MapDelete("/traces/{traceId}", (string traceId, IExecutionTraceStore traceStore) =>
        {
            var deleted = traceStore.Remove(traceId);
            return Results.Ok(new DeleteTraceResponse(deleted));
        });

        group.MapPost("/traces/delete", (BulkDeleteTracesRequest request, IExecutionTraceStore traceStore) =>
        {
            var traceIds = request?.TraceIds ?? [];
            var deletedCount = traceStore.RemoveMany(traceIds);
            return Results.Ok(new BulkDeleteResponse(deletedCount));
        });

        group.MapDelete("/traces", (IExecutionTraceStore traceStore) =>
        {
            traceStore.Clear();
            return Results.Ok(new ClearTracesResponse(true));
        });
    }
}
