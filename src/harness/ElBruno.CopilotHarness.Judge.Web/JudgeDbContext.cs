using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Judge.Web;

public sealed class JudgeDbContext(DbContextOptions<JudgeDbContext> options) : DbContext(options)
{
    public DbSet<PromptRecordEntity> PromptRecords => Set<PromptRecordEntity>();
    public DbSet<BenchmarkRunEntity> BenchmarkRuns => Set<BenchmarkRunEntity>();
    public DbSet<BenchmarkResultEntity> BenchmarkResults => Set<BenchmarkResultEntity>();
    public DbSet<RecommendationEntity> Recommendations => Set<RecommendationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var promptRecords = modelBuilder.Entity<PromptRecordEntity>();
        promptRecords.ToTable("PromptRecords");
        promptRecords.HasKey(entity => entity.Id);
        promptRecords.Property(entity => entity.Id).ValueGeneratedNever();
        promptRecords.Property(entity => entity.Source).HasMaxLength(128);
        promptRecords.Property(entity => entity.ClientId).HasMaxLength(64);
        promptRecords.Property(entity => entity.Endpoint).HasMaxLength(128);
        promptRecords.Property(entity => entity.Prompt).HasMaxLength(8192);
        promptRecords.Property(entity => entity.SystemMessage).HasMaxLength(8192);
        promptRecords.Property(entity => entity.RequestedModel).HasMaxLength(128);
        promptRecords.Property(entity => entity.ReferenceAnswer).HasMaxLength(8192);
        promptRecords.Property(entity => entity.MetadataJson).HasMaxLength(32768);
        promptRecords.HasIndex(entity => entity.ImportedAtUtc);

        var benchmarkRuns = modelBuilder.Entity<BenchmarkRunEntity>();
        benchmarkRuns.ToTable("BenchmarkRuns");
        benchmarkRuns.HasKey(entity => entity.Id);
        benchmarkRuns.Property(entity => entity.Id).ValueGeneratedNever();
        benchmarkRuns.Property(entity => entity.Name).HasMaxLength(128);
        benchmarkRuns.Property(entity => entity.Mode).HasMaxLength(32);
        benchmarkRuns.Property(entity => entity.Status).HasMaxLength(32);
        benchmarkRuns.Property(entity => entity.FailureReason).HasMaxLength(2048);
        benchmarkRuns.Property(entity => entity.CreatedAtUtc);
        benchmarkRuns.HasMany(entity => entity.Results)
            .WithOne()
            .HasForeignKey(entity => entity.BenchmarkRunId)
            .OnDelete(DeleteBehavior.Cascade);
        benchmarkRuns.HasIndex(entity => entity.CreatedAtUtc);

        var benchmarkResults = modelBuilder.Entity<BenchmarkResultEntity>();
        benchmarkResults.ToTable("BenchmarkResults");
        benchmarkResults.HasKey(entity => entity.Id);
        benchmarkResults.Property(entity => entity.Id).ValueGeneratedNever();
        benchmarkResults.Property(entity => entity.ProfileName).HasMaxLength(128);
        benchmarkResults.Property(entity => entity.Deployment).HasMaxLength(128);
        benchmarkResults.Property(entity => entity.ResponseText).HasMaxLength(8192);
        benchmarkResults.Property(entity => entity.WinnerReason).HasMaxLength(512);
        benchmarkResults.HasIndex(entity => entity.BenchmarkRunId);
        benchmarkResults.HasIndex(entity => entity.PromptRecordId);
        benchmarkResults.HasIndex(entity => entity.IsWinner);

        var recommendations = modelBuilder.Entity<RecommendationEntity>();
        recommendations.ToTable("Recommendations");
        recommendations.HasKey(e => e.Id);
        recommendations.Property(e => e.Id).ValueGeneratedNever();
        recommendations.Property(e => e.Type).HasMaxLength(64);
        recommendations.Property(e => e.Summary).HasMaxLength(256);
        recommendations.Property(e => e.Rationale).HasMaxLength(2048);
        recommendations.Property(e => e.SuggestedAction).HasMaxLength(512);
        recommendations.Property(e => e.SuggestedProfileName).HasMaxLength(128);
        recommendations.Property(e => e.Status).HasMaxLength(32);
        recommendations.Property(e => e.ReviewNotes).HasMaxLength(1024);
        recommendations.HasIndex(e => e.Status);
        recommendations.HasIndex(e => e.GeneratedAtUtc);
    }
}

public sealed class PromptRecordEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? SystemMessage { get; set; }
    public string? RequestedModel { get; set; }
    public string? ReferenceAnswer { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BenchmarkRunEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int PromptRecordCount { get; set; }
    public int ModelCount { get; set; }
    public List<BenchmarkResultEntity> Results { get; set; } = [];
}

public sealed class BenchmarkResultEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BenchmarkRunId { get; set; }
    public Guid PromptRecordId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public bool IsWinner { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? LatencyMs { get; set; }
    public string ResponseText { get; set; } = string.Empty;
    public string? WinnerReason { get; set; }
    public double CorrectnessScore { get; set; }
    public double CompletenessScore { get; set; }
    public double SecurityScore { get; set; }
    public double BestPracticesScore { get; set; }
    public double CostScore { get; set; }
    public double LatencyScore { get; set; }
    public double TokenScore { get; set; }
    public double OverallScore { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecommendationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string SuggestedAction { get; set; } = string.Empty;
    public string? SuggestedProfileName { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }
}

public sealed class JudgeDatabaseInitializer(JudgeDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
