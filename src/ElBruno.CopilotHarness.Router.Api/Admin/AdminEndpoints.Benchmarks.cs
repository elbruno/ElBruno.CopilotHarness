using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.CopilotHarness.Router.Core;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapBenchmarkEndpoints(RouteGroupBuilder group)
    {
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
    }
}
