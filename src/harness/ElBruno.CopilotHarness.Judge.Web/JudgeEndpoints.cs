namespace ElBruno.CopilotHarness.Judge.Web;

public static class JudgeEndpoints
{
    public static IEndpointRouteBuilder MapJudgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/judge");

        group.MapGet("/prompt-records", async (IPromptRecordStore store, CancellationToken cancellationToken) =>
        {
            var records = await store.ListAsync(cancellationToken);
            return records.Select(ToResponse).ToList();
        });

        group.MapPost("/prompt-records/import", async (
            ImportPromptRecordsRequest request,
            IPromptRecordStore store,
            CancellationToken cancellationToken) =>
        {
            if (request.Records.Count == 0)
            {
                return Results.BadRequest("At least one prompt record is required.");
            }

            var records = await store.ImportAsync(request.Records, cancellationToken);
            return Results.Ok(records.Select(ToResponse).ToList());
        });

        group.MapPost("/benchmarks/replay", async (
            ReplayPromptRecordsRequest request,
            IBenchmarkRunner runner,
            CancellationToken cancellationToken) =>
        {
            if (request.Models.Count == 0)
            {
                return Results.BadRequest("At least one benchmark model is required.");
            }

            var run = await runner.ReplayAsync(request, cancellationToken);
            return Results.Ok(ToSummary(run));
        });

        group.MapPost("/benchmarks/manual", async (
            ManualBenchmarkRequest request,
            IBenchmarkRunner runner,
            CancellationToken cancellationToken) =>
        {
            if (request.Models.Count == 0)
            {
                return Results.BadRequest("At least one benchmark model is required.");
            }

            var run = await runner.ManualAsync(request, cancellationToken);
            return Results.Ok(ToSummary(run));
        });

        group.MapGet("/benchmarks/runs", async (IBenchmarkRunner runner, CancellationToken cancellationToken) =>
        {
            var runs = await runner.ListSummariesAsync(cancellationToken);
            return Results.Ok(runs);
        });

        group.MapGet("/benchmarks/runs/{runId:guid}", async (Guid runId, IBenchmarkRunner runner, CancellationToken cancellationToken) =>
        {
            var summary = await runner.GetSummaryAsync(runId, cancellationToken);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        });

        group.MapGet("/reports/{runId:guid}", async (Guid runId, IBenchmarkRunner runner, CancellationToken cancellationToken) =>
        {
            var report = await runner.GetReportAsync(runId, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        return endpoints;
    }

    private static PromptRecordResponse ToResponse(PromptRecordEntity record) =>
        new(
            record.Id,
            record.Source,
            record.ClientId,
            record.Endpoint,
            record.Prompt,
            record.SystemMessage,
            record.RequestedModel,
            record.ReferenceAnswer,
            record.ImportedAtUtc);

    private static BenchmarkRunSummaryDto ToSummary(BenchmarkRunEntity run) =>
        new(
            run.Id,
            run.Name,
            run.Mode,
            run.Status,
            run.CreatedAtUtc,
            run.CompletedAtUtc,
            run.PromptRecordCount,
            run.ModelCount,
            run.FailureReason);
}