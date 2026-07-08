using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Judge.Web;

public interface IPromptRecordStore
{
    Task<IReadOnlyList<PromptRecordEntity>> ImportAsync(IReadOnlyList<PromptRecordImportRequest> requests, CancellationToken cancellationToken);
    Task<IReadOnlyList<PromptRecordEntity>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PromptRecordEntity>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
    Task<PromptRecordEntity?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class PromptRecordStore(JudgeDbContext dbContext) : IPromptRecordStore
{
    public async Task<IReadOnlyList<PromptRecordEntity>> ImportAsync(IReadOnlyList<PromptRecordImportRequest> requests, CancellationToken cancellationToken)
    {
        var records = requests.Select(request => new PromptRecordEntity
        {
            Source = request.Source.Trim(),
            ClientId = request.ClientId.Trim(),
            Endpoint = request.Endpoint.Trim(),
            Prompt = request.Prompt.Trim(),
            SystemMessage = Normalize(request.SystemMessage),
            RequestedModel = Normalize(request.RequestedModel),
            ReferenceAnswer = Normalize(request.ReferenceAnswer),
            MetadataJson = request.Metadata is null ? null : request.Metadata.ToJsonString()
        }).ToList();

        dbContext.PromptRecords.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
        return records;
    }

    public async Task<IReadOnlyList<PromptRecordEntity>> ListAsync(CancellationToken cancellationToken) =>
        (await dbContext.PromptRecords
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .OrderByDescending(record => record.ImportedAtUtc)
            .ToList();

    public async Task<IReadOnlyList<PromptRecordEntity>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.PromptRecords
            .AsNoTracking()
            .Where(record => ids.Contains(record.Id))
            .ToListAsync(cancellationToken);

    public async Task<PromptRecordEntity?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.PromptRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == id, cancellationToken);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IBenchmarkRunner
{
    Task<BenchmarkRunEntity> ReplayAsync(ReplayPromptRecordsRequest request, CancellationToken cancellationToken);
    Task<BenchmarkRunEntity> ManualAsync(ManualBenchmarkRequest request, CancellationToken cancellationToken);
    Task<BenchmarkRunSummaryDto?> GetSummaryAsync(Guid runId, CancellationToken cancellationToken);
    Task<BenchmarkRunReportDto?> GetReportAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BenchmarkRunSummaryDto>> ListSummariesAsync(CancellationToken cancellationToken);
}

public sealed class BenchmarkRunner(
    JudgeDbContext dbContext,
    IPromptRecordStore promptRecordStore,
    IJudgeModelClient judgeModelClient,
    IJudgeScoringEngine scoringEngine) : IBenchmarkRunner
{
    public Task<BenchmarkRunEntity> ReplayAsync(ReplayPromptRecordsRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.Name, "replay", request.PromptRecordIds, request.Models, cancellationToken);

    public async Task<BenchmarkRunEntity> ManualAsync(ManualBenchmarkRequest request, CancellationToken cancellationToken)
    {
        if (request.Models.Count == 0)
        {
            throw new InvalidOperationException("At least one model is required for a manual benchmark.");
        }

        var promptRecord = (await promptRecordStore.ImportAsync([
            new PromptRecordImportRequest(
                "manual",
                "manual",
                "/manual/benchmark",
                request.Prompt,
                request.SystemMessage,
                request.Models.FirstOrDefault()?.ProfileName,
                request.ReferenceAnswer,
                null)
        ], cancellationToken)).Single();

        return await ExecuteAsync(request.Name, "manual", [promptRecord.Id], request.Models, cancellationToken);
    }

    public async Task<BenchmarkRunSummaryDto?> GetSummaryAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.BenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == runId, cancellationToken);
        return run is null ? null : ToSummaryDto(run);
    }

    public async Task<BenchmarkRunReportDto?> GetReportAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.BenchmarkRuns
            .AsNoTracking()
            .Include(entity => entity.Results)
            .FirstOrDefaultAsync(entity => entity.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var promptRecordIds = run.Results.Select(result => result.PromptRecordId).Distinct().ToList();
        var promptRecords = await dbContext.PromptRecords
            .AsNoTracking()
            .Where(record => promptRecordIds.Contains(record.Id))
            .ToListAsync(cancellationToken);

        var promptLookup = promptRecords.ToDictionary(record => record.Id);
        var results = run.Results.OrderBy(result => result.PromptRecordId).ThenBy(result => result.ProfileName).ToList();

        var promptReports = promptRecordIds
            .Select(promptRecordId =>
            {
                var promptRecord = promptLookup[promptRecordId];
                var promptResults = results
                    .Where(result => result.PromptRecordId == promptRecordId)
                    .Select(ToResultDto)
                    .ToList();

                return new BenchmarkPromptReportDto(
                    promptRecordId,
                    promptRecord.Prompt,
                    promptRecord.ReferenceAnswer,
                    promptResults);
            })
            .ToList();

        var modelReports = results
            .GroupBy(result => new { result.ProfileName, result.Deployment })
            .Select(group =>
            {
                var ordered = group.ToList();
                return new BenchmarkModelReportDto(
                    group.Key.ProfileName,
                    group.Key.Deployment,
                    ordered.Count,
                    ordered.Count(item => item.IsWinner),
                    ordered.Average(item => item.OverallScore),
                    ordered.Average(item => item.LatencyMs ?? 0),
                    ordered.Average(item => item.TokenScore));
            })
            .OrderByDescending(report => report.AverageOverallScore)
            .ToList();

        return new BenchmarkRunReportDto(ToSummaryDto(run), promptReports, modelReports, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<BenchmarkRunSummaryDto>> ListSummariesAsync(CancellationToken cancellationToken)
    {
        var runs = await dbContext.BenchmarkRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return runs
            .OrderByDescending(entity => entity.CreatedAtUtc)
            .Select(ToSummaryDto)
            .ToList();
    }

    private async Task<BenchmarkRunEntity> ExecuteAsync(
        string name,
        string mode,
        IReadOnlyList<Guid> promptRecordIds,
        IReadOnlyList<BenchmarkModelRequest> models,
        CancellationToken cancellationToken)
    {
        var promptRecords = await promptRecordStore.GetByIdsAsync(promptRecordIds, cancellationToken);
        if (promptRecords.Count == 0)
        {
            throw new InvalidOperationException("No prompt records were found for the benchmark run.");
        }

        if (models.Count == 0)
        {
            throw new InvalidOperationException("At least one model is required for a benchmark run.");
        }

        var run = new BenchmarkRunEntity
        {
            Name = name.Trim(),
            Mode = mode,
            Status = "Running",
            PromptRecordCount = promptRecords.Count,
            ModelCount = models.Count
        };

        dbContext.BenchmarkRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            foreach (var promptRecord in promptRecords)
            {
                foreach (var model in models)
                {
                    var execution = new JudgeModelExecutionRequest(promptRecord, model);
                    var response = await judgeModelClient.GenerateAsync(execution, cancellationToken);
                    var scores = scoringEngine.Score(promptRecord, response);

                    var result = new BenchmarkResultEntity
                    {
                        BenchmarkRunId = run.Id,
                        PromptRecordId = promptRecord.Id,
                        ProfileName = model.ProfileName.Trim(),
                        Deployment = model.Deployment.Trim(),
                        InputTokens = response.InputTokens,
                        OutputTokens = response.OutputTokens,
                        LatencyMs = response.LatencyMs,
                        ResponseText = response.ResponseText.Trim(),
                        CorrectnessScore = scores.Correctness,
                        CompletenessScore = scores.Completeness,
                        SecurityScore = scores.Security,
                        BestPracticesScore = scores.BestPractices,
                        CostScore = scores.Cost,
                        LatencyScore = scores.Latency,
                        TokenScore = scores.Tokens,
                        OverallScore = scores.Overall,
                        EvaluatedAtUtc = DateTimeOffset.UtcNow
                    };

                    run.Results.Add(result);
                    dbContext.BenchmarkResults.Add(result);
                }

                var promptResults = run.Results.Where(result => result.PromptRecordId == promptRecord.Id).ToList();
                var winner = promptResults
                    .OrderByDescending(result => result.OverallScore)
                    .ThenBy(result => result.LatencyMs ?? double.MaxValue)
                    .First();

                foreach (var promptResult in promptResults)
                {
                    promptResult.IsWinner = promptResult.Id == winner.Id;
                    promptResult.WinnerReason = promptResult.IsWinner ? "Highest overall score." : "Lower than winning model.";
                }
            }
            run.Status = "Completed";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.FailureReason = ex.Message;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static BenchmarkRunSummaryDto ToSummaryDto(BenchmarkRunEntity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Mode,
            entity.Status,
            entity.CreatedAtUtc,
            entity.CompletedAtUtc,
            entity.PromptRecordCount,
            entity.ModelCount,
            entity.FailureReason);

    private static BenchmarkResultDto ToResultDto(BenchmarkResultEntity entity) =>
        new(
            entity.Id,
            entity.PromptRecordId,
            entity.ProfileName,
            entity.Deployment,
            entity.IsWinner,
            entity.InputTokens,
            entity.OutputTokens,
            entity.LatencyMs,
            entity.ResponseText,
            new JudgeScoresDto(
                entity.CorrectnessScore,
                entity.CompletenessScore,
                entity.SecurityScore,
                entity.BestPracticesScore,
                entity.CostScore,
                entity.LatencyScore,
                entity.TokenScore,
                entity.OverallScore));
}
