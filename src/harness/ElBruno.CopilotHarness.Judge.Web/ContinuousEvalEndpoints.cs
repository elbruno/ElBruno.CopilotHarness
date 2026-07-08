using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Judge.Web;

public static class ContinuousEvalEndpoints
{
    private static readonly string[] ValidReviewStatuses = ["Approved", "Rejected"];

    public static IEndpointRouteBuilder MapContinuousEvalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/judge/continuous-eval");

        group.MapGet("/recommendations", async (
            JudgeDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var recs = (await dbContext.Recommendations
                .AsNoTracking()
                .ToListAsync(cancellationToken))
                .OrderByDescending(r => r.GeneratedAtUtc)
                .ToList();

            return Results.Ok(recs.Select(ToDto).ToList());
        });

        group.MapPost("/recommendations/analyze", async (
            IRecommendationAgent agent,
            CancellationToken cancellationToken) =>
        {
            var generated = await agent.AnalyzeAsync(cancellationToken);
            return Results.Ok(generated.Select(ToDto).ToList());
        });

        group.MapPut("/recommendations/{id:guid}/review", async (
            Guid id,
            ReviewRecommendationRequest request,
            JudgeDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!ValidReviewStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            {
                return Results.BadRequest($"Status must be one of: {string.Join(", ", ValidReviewStatuses)}.");
            }

            var rec = await dbContext.Recommendations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (rec is null)
            {
                return Results.NotFound();
            }

            rec.Status = request.Status;
            rec.ReviewedAtUtc = DateTimeOffset.UtcNow;
            rec.ReviewNotes = string.IsNullOrWhiteSpace(request.ReviewNotes) ? null : request.ReviewNotes.Trim();
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToDto(rec));
        });

        group.MapGet("/schedule", (ContinuousBenchmarkScheduler scheduler) =>
            Results.Ok(new ContinuousBenchmarkScheduleDto(
                scheduler.Options.Enabled,
                scheduler.Options.IntervalMinutes,
                scheduler.Options.BatchSize,
                scheduler.LastRunAtUtc,
                scheduler.LastRunStatus)));

        return endpoints;
    }

    private static RecommendationDto ToDto(RecommendationEntity entity) =>
        new(
            entity.Id,
            entity.Type,
            entity.Summary,
            entity.Rationale,
            entity.Confidence,
            entity.SuggestedAction,
            entity.SuggestedProfileName,
            entity.Status,
            entity.GeneratedAtUtc,
            entity.ReviewedAtUtc,
            entity.ReviewNotes);
}
