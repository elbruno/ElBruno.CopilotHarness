using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class ShadowRoutingStore(HarnessDbContext db, IDistributedCache? cache = null) : IShadowRoutingStore
{
    private const string ConfigCacheKey = "shadow:config";
    private static readonly ShadowRoutingConfig DefaultConfig = new(false, "big", 0.1);

    public async Task<ShadowRoutingConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        if (cache is not null)
        {
            var cached = await cache.GetStringAsync(ConfigCacheKey, cancellationToken);
            if (cached is not null)
                return JsonSerializer.Deserialize<ShadowRoutingConfig>(cached) ?? DefaultConfig;
        }

        // Use a simple key-value approach stored in ShadowRequests metadata table.
        // For simplicity, we read a sentinel row with ShadowId == "__config__".
        var sentinel = await db.ShadowRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShadowId == "__config__", cancellationToken);

        if (sentinel is null)
            return DefaultConfig;

        return JsonSerializer.Deserialize<ShadowRoutingConfig>(sentinel.PromptHash) ?? DefaultConfig;
    }

    public async Task<ShadowRoutingConfig> SaveConfigAsync(ShadowRoutingConfig config, CancellationToken cancellationToken)
    {
        var configJson = JsonSerializer.Serialize(config);

        var sentinel = await db.ShadowRequests
            .FirstOrDefaultAsync(s => s.ShadowId == "__config__", cancellationToken);

        if (sentinel is null)
        {
            sentinel = new ShadowRequestEntity
            {
                ShadowId = "__config__",
                OriginalTraceId = "__config__",
                PromptHash = configJson
            };
            db.ShadowRequests.Add(sentinel);
        }
        else
        {
            sentinel.PromptHash = configJson;
            sentinel.CompletedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (cache is not null)
        {
            await cache.SetStringAsync(ConfigCacheKey, configJson,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
                cancellationToken);
        }

        return config;
    }

    public async Task<string> RecordShadowRequestAsync(
        string originalTraceId,
        string primaryProfile,
        string shadowProfile,
        string promptHash,
        CancellationToken cancellationToken)
    {
        var shadowId = Guid.NewGuid().ToString("N");
        var entity = new ShadowRequestEntity
        {
            ShadowId = shadowId,
            OriginalTraceId = originalTraceId,
            PrimaryProfile = primaryProfile,
            ShadowProfile = shadowProfile,
            PromptHash = promptHash,
            OutcomeLabel = "pending"
        };

        db.ShadowRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return shadowId;
    }

    public async Task UpdateShadowOutcomeAsync(
        string shadowId,
        double primaryLatencyMs,
        double shadowLatencyMs,
        int primaryStatusCode,
        int shadowStatusCode,
        string outcomeLabel,
        CancellationToken cancellationToken)
    {
        var entity = await db.ShadowRequests
            .FirstOrDefaultAsync(s => s.ShadowId == shadowId, cancellationToken);

        if (entity is null)
            return;

        entity.PrimaryLatencyMs = primaryLatencyMs;
        entity.ShadowLatencyMs = shadowLatencyMs;
        entity.PrimaryStatusCode = primaryStatusCode;
        entity.ShadowStatusCode = shadowStatusCode;
        entity.OutcomeLabel = outcomeLabel;
        entity.CompletedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShadowRequestRecord>> GetRecentResultsAsync(int count, CancellationToken cancellationToken)
    {
        // Fetch without ORDER BY to avoid SQLite DateTimeOffset translation issues; sort in memory.
        var entities = await db.ShadowRequests
            .AsNoTracking()
            .Where(s => s.ShadowId != "__config__")
            .ToListAsync(cancellationToken);

        return entities
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(count)
            .Select(e => new ShadowRequestRecord(
                e.ShadowId,
                e.OriginalTraceId,
                e.PrimaryProfile,
                e.ShadowProfile,
                e.PromptHash,
                e.PrimaryLatencyMs,
                e.ShadowLatencyMs,
                e.PrimaryStatusCode,
                e.ShadowStatusCode,
                e.OutcomeLabel,
                e.CreatedAtUtc,
                e.CompletedAtUtc)).ToList();
    }
}

public sealed class RuleConfidenceStore(HarnessDbContext db) : IRuleConfidenceStore
{
    private static string CurrentWindowLabel() =>
        $"{DateTimeOffset.UtcNow:yyyy-MM-dd-HH}";

    public async Task RecordInvocationAsync(
        string ruleKey,
        bool successful,
        CancellationToken cancellationToken)
    {
        var windowLabel = CurrentWindowLabel();
        var windowStart = DateTimeOffset.UtcNow.Date.AddHours(DateTimeOffset.UtcNow.Hour);
        var windowEnd = windowStart.AddHours(1);

        var existing = await db.RuleConfidenceScores
            .FirstOrDefaultAsync(s => s.RuleKey == ruleKey && s.WindowLabel == windowLabel, cancellationToken);

        if (existing is null)
        {
            existing = new RuleConfidenceScoreEntity
            {
                RuleKey = ruleKey,
                WindowLabel = windowLabel,
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd,
                TotalInvocations = 0,
                SuccessfulInvocations = 0
            };
            db.RuleConfidenceScores.Add(existing);
        }

        existing.TotalInvocations++;
        if (successful)
            existing.SuccessfulInvocations++;

        existing.ConfidenceScore = existing.TotalInvocations > 0
            ? (double)existing.SuccessfulInvocations / existing.TotalInvocations
            : 0.0;

        existing.RecordedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleConfidenceSummary>> GetCurrentScoresAsync(CancellationToken cancellationToken)
    {
        // Fetch all recent records without date filter (SQLite DateTimeOffset LINQ issues).
        // Sort and filter in memory.
        var cutoffLabel = DateTimeOffset.UtcNow.AddDays(-7).ToString("yyyy-MM-dd-HH");

        var scores = await db.RuleConfidenceScores
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Aggregate per rule key across recent windows
        return scores
            .Where(s => string.Compare(s.WindowLabel, cutoffLabel, StringComparison.OrdinalIgnoreCase) >= 0)
            .GroupBy(s => s.RuleKey)
            .Select(g =>
            {
                var total = g.Sum(s => s.TotalInvocations);
                var successful = g.Sum(s => s.SuccessfulInvocations);
                var latest = g.OrderByDescending(s => s.RecordedAtUtc).First();
                return new RuleConfidenceSummary(
                    g.Key,
                    total,
                    successful,
                    total > 0 ? (double)successful / total : 0.0,
                    "7d",
                    g.Min(s => s.WindowStartUtc),
                    g.Max(s => s.WindowEndUtc),
                    latest.RecordedAtUtc);
            })
            .ToList();
    }

    public async Task<RuleConfidenceSummary?> GetScoreAsync(string ruleKey, CancellationToken cancellationToken)
    {
        var scores = await db.RuleConfidenceScores
            .AsNoTracking()
            .Where(s => s.RuleKey == ruleKey)
            .ToListAsync(cancellationToken);

        if (scores.Count == 0)
            return null;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var recent = scores.Where(s => s.RecordedAtUtc >= cutoff).ToList();
        if (recent.Count == 0) recent = scores;

        var total = recent.Sum(s => s.TotalInvocations);
        var successful = recent.Sum(s => s.SuccessfulInvocations);
        var latest = recent.OrderByDescending(s => s.RecordedAtUtc).First();

        return new RuleConfidenceSummary(
            ruleKey,
            total,
            successful,
            total > 0 ? (double)successful / total : 0.0,
            "7d",
            recent.Min(s => s.WindowStartUtc),
            recent.Max(s => s.WindowEndUtc),
            latest.RecordedAtUtc);
    }
}

public sealed class BenchmarkStore(HarnessDbContext db) : IBenchmarkStore
{
    public async Task<BenchmarkRunSummary> CreateRunAsync(
        CreateBenchmarkRunRequest request,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var profilesJson = System.Text.Json.JsonSerializer.Serialize(request.Profiles);

        var entity = new BenchmarkRunEntity
        {
            RunId = runId,
            Name = request.Name,
            Description = request.Description,
            ProfilesJson = profilesJson,
            Status = "pending",
            TotalItems = request.Items.Count * request.Profiles.Count,
            CompletedItems = 0
        };

        db.BenchmarkRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<IReadOnlyList<BenchmarkRunSummary>> ListRunsAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        // Sort in memory to avoid SQLite DateTimeOffset ORDER BY limitation.
        var entities = await db.BenchmarkRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSummary).ToList();
    }

    public async Task<BenchmarkRunSummary?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        var entity = await db.BenchmarkRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);

        return entity is null ? null : ToSummary(entity);
    }

    public async Task<BenchmarkRunSummary> UpdateRunStatusAsync(
        string runId, string status, CancellationToken cancellationToken)
    {
        var entity = await db.BenchmarkRuns
            .FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Benchmark run '{runId}' not found.");

        entity.Status = status;
        if (status == "running" && entity.StartedAtUtc is null)
            entity.StartedAtUtc = DateTimeOffset.UtcNow;
        if (status is "completed" or "failed")
            entity.CompletedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<BenchmarkResultRecord> RecordResultAsync(
        string runId,
        RecordBenchmarkResultRequest request,
        CancellationToken cancellationToken)
    {
        var entity = new BenchmarkResultEntity
        {
            RunId = runId,
            ItemId = request.ItemId,
            Profile = request.Profile,
            Deployment = request.Deployment,
            PromptHash = request.PromptHash,
            LatencyMs = request.LatencyMs,
            PromptTokens = request.PromptTokens,
            CompletionTokens = request.CompletionTokens,
            StatusCode = request.StatusCode,
            JudgeVerdict = request.JudgeVerdict,
            JudgeScore = request.JudgeScore,
            MetricsJson = request.MetricsJson
        };

        db.BenchmarkResults.Add(entity);

        var run = await db.BenchmarkRuns
            .FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);

        if (run is not null)
        {
            run.CompletedItems++;
            if (run.CompletedItems >= run.TotalItems)
            {
                run.Status = "completed";
                run.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<IReadOnlyList<BenchmarkResultRecord>> GetResultsAsync(
        string runId, CancellationToken cancellationToken)
    {
        var entities = await db.BenchmarkResults
            .AsNoTracking()
            .Where(r => r.RunId == runId)
            .ToListAsync(cancellationToken);

        return entities.OrderBy(r => r.Id).Select(ToRecord).ToList();
    }

    private static BenchmarkRunSummary ToSummary(BenchmarkRunEntity e)
    {
        var profiles = JsonSerializer.Deserialize<List<string>>(e.ProfilesJson) ?? [];
        return new BenchmarkRunSummary(
            e.RunId, e.Name, e.Description, profiles,
            e.Status, e.TotalItems, e.CompletedItems,
            e.CreatedAtUtc, e.StartedAtUtc, e.CompletedAtUtc);
    }

    private static BenchmarkResultRecord ToRecord(BenchmarkResultEntity e) =>
        new(e.Id, e.RunId, e.ItemId, e.Profile, e.Deployment,
            e.LatencyMs, e.PromptTokens, e.CompletionTokens,
            e.StatusCode, e.JudgeVerdict, e.JudgeScore, e.CreatedAtUtc);
}

public sealed class ApprovalWorkflowStore(HarnessDbContext db) : IApprovalWorkflowStore
{
    public async Task<ApprovalRequestSummary> CreateAsync(
        CreateApprovalRequest request, CancellationToken cancellationToken)
    {
        var entity = new ApprovalRequestEntity
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            ChangeType = request.ChangeType,
            Title = request.Title,
            Description = request.Description,
            PayloadJson = request.PayloadJson,
            Status = "pending",
            ExpiresAtUtc = request.ExpiresAtUtc ?? DateTimeOffset.UtcNow.AddDays(7)
        };

        db.ApprovalRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<IReadOnlyList<ApprovalRequestSummary>> ListAsync(
        string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.ApprovalRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        // Fetch without ORDER BY then sort in memory (SQLite DateTimeOffset limitation).
        var entities = await query.ToListAsync(cancellationToken);

        return entities
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSummary).ToList();
    }

    public async Task<ApprovalRequestSummary?> GetAsync(
        string approvalId, CancellationToken cancellationToken)
    {
        var entity = await db.ApprovalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ApprovalId == approvalId, cancellationToken);

        return entity is null ? null : ToSummary(entity);
    }

    public async Task<ApprovalRequestSummary> ReviewAsync(
        string approvalId, ReviewApprovalRequest review, CancellationToken cancellationToken)
    {
        var entity = await db.ApprovalRequests
            .FirstOrDefaultAsync(a => a.ApprovalId == approvalId, cancellationToken)
            ?? throw new KeyNotFoundException($"Approval request '{approvalId}' not found.");

        if (entity.Status != "pending")
            throw new InvalidOperationException($"Approval '{approvalId}' is already in status '{entity.Status}'.");

        entity.Status = review.Approved ? "approved" : "rejected";
        entity.ReviewedBy = review.ReviewedBy;
        entity.ReviewNotes = review.ReviewNotes;
        entity.ReviewedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(entity);
    }

    public async Task<int> ExpireOverdueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var overdue = await db.ApprovalRequests
            .Where(a => a.Status == "pending" && a.ExpiresAtUtc < now)
            .ToListAsync(cancellationToken);

        foreach (var entity in overdue)
        {
            entity.Status = "expired";
            entity.ReviewedAtUtc = now;
            entity.ReviewNotes = "Auto-expired by system.";
        }

        await db.SaveChangesAsync(cancellationToken);
        return overdue.Count;
    }

    private static ApprovalRequestSummary ToSummary(ApprovalRequestEntity e) =>
        new(e.ApprovalId, e.ChangeType, e.Title, e.Description, e.PayloadJson,
            e.Status, e.ReviewedBy, e.ReviewNotes,
            e.CreatedAtUtc, e.ReviewedAtUtc, e.ExpiresAtUtc);
}

public sealed class TeamProjectProfileStore(HarnessDbContext db) : ITeamProjectProfileStore
{
    public async Task<IReadOnlyList<TeamProfileSummary>> ListTeamsAsync(CancellationToken cancellationToken)
    {
        var entities = await db.TeamProfiles
            .AsNoTracking()
            .OrderBy(t => t.DisplayName)
            .ToListAsync(cancellationToken);
        return entities.Select(ToTeamSummary).ToList();
    }

    public async Task<TeamProfileSummary?> GetTeamAsync(string teamId, CancellationToken cancellationToken)
    {
        var entity = await db.TeamProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);
        return entity is null ? null : ToTeamSummary(entity);
    }

    public async Task<TeamProfileSummary> UpsertTeamAsync(
        string teamId, UpsertTeamProfileRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.TeamProfiles
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);

        if (entity is null)
        {
            entity = new TeamProfileEntity { TeamId = teamId };
            db.TeamProfiles.Add(entity);
        }

        entity.DisplayName = request.DisplayName;
        entity.DefaultProfile = request.DefaultProfile;
        entity.RulesJson = request.RulesJson;
        entity.Enabled = request.Enabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToTeamSummary(entity);
    }

    public async Task<bool> DeleteTeamAsync(string teamId, CancellationToken cancellationToken)
    {
        var entity = await db.TeamProfiles
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);

        if (entity is null)
            return false;

        db.TeamProfiles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ProjectProfileSummary>> ListProjectsAsync(
        string? teamId, CancellationToken cancellationToken)
    {
        var query = db.ProjectProfiles.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(teamId))
            query = query.Where(p => p.TeamId == teamId);

        var entities = await query.OrderBy(p => p.DisplayName).ToListAsync(cancellationToken);
        return entities.Select(ToProjectSummary).ToList();
    }

    public async Task<ProjectProfileSummary?> GetProjectAsync(
        string projectId, CancellationToken cancellationToken)
    {
        var entity = await db.ProjectProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
        return entity is null ? null : ToProjectSummary(entity);
    }

    public async Task<ProjectProfileSummary> UpsertProjectAsync(
        string projectId, UpsertProjectProfileRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.ProjectProfiles
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);

        if (entity is null)
        {
            entity = new ProjectProfileEntity { ProjectId = projectId };
            db.ProjectProfiles.Add(entity);
        }

        entity.TeamId = request.TeamId;
        entity.DisplayName = request.DisplayName;
        entity.DefaultProfile = request.DefaultProfile;
        entity.RulesJson = request.RulesJson;
        entity.Enabled = request.Enabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToProjectSummary(entity);
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var entity = await db.ProjectProfiles
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);

        if (entity is null)
            return false;

        db.ProjectProfiles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static TeamProfileSummary ToTeamSummary(TeamProfileEntity e) =>
        new(e.TeamId, e.DisplayName, e.DefaultProfile, e.RulesJson,
            e.Enabled, e.CreatedAtUtc, e.UpdatedAtUtc);

    private static ProjectProfileSummary ToProjectSummary(ProjectProfileEntity e) =>
        new(e.ProjectId, e.TeamId, e.DisplayName, e.DefaultProfile,
            e.RulesJson, e.Enabled, e.CreatedAtUtc, e.UpdatedAtUtc);
}
