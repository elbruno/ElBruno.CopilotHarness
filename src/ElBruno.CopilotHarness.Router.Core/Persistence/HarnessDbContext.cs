using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class HarnessDbContext(DbContextOptions<HarnessDbContext> options) : DbContext(options)
{
    public DbSet<ModelProfileEntity> ModelProfiles => Set<ModelProfileEntity>();
    public DbSet<RoutingRuleSettingsEntity> RoutingRuleSettings => Set<RoutingRuleSettingsEntity>();
    public DbSet<RoutingExecutionTraceEntity> RoutingExecutionTraces => Set<RoutingExecutionTraceEntity>();
    public DbSet<SetupStateEntity> SetupState => Set<SetupStateEntity>();

    // Phase 8 - Continuous Evaluation
    public DbSet<ShadowRequestEntity> ShadowRequests => Set<ShadowRequestEntity>();
    public DbSet<RuleConfidenceScoreEntity> RuleConfidenceScores => Set<RuleConfidenceScoreEntity>();
    public DbSet<BenchmarkRunEntity> BenchmarkRuns => Set<BenchmarkRunEntity>();
    public DbSet<BenchmarkResultEntity> BenchmarkResults => Set<BenchmarkResultEntity>();
    public DbSet<ApprovalRequestEntity> ApprovalRequests => Set<ApprovalRequestEntity>();
    public DbSet<TeamProfileEntity> TeamProfiles => Set<TeamProfileEntity>();
    public DbSet<ProjectProfileEntity> ProjectProfiles => Set<ProjectProfileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var profiles = modelBuilder.Entity<ModelProfileEntity>();
        profiles.ToTable("ModelProfiles");
        profiles.HasKey(entity => entity.ProfileName);
        profiles.Property(entity => entity.ProfileName).HasMaxLength(64);
        profiles.Property(entity => entity.DisplayName).HasMaxLength(128);
        profiles.Property(entity => entity.Deployment).HasMaxLength(128);
        profiles.Property(entity => entity.ApiVersion).HasMaxLength(32);

        var rules = modelBuilder.Entity<RoutingRuleSettingsEntity>();
        rules.ToTable("RoutingRuleSettings");
        rules.HasKey(entity => entity.Id);
        rules.Property(entity => entity.DefaultProfile).HasMaxLength(64);
        rules.Property(entity => entity.BigProfile).HasMaxLength(64);
        rules.Property(entity => entity.StreamingProfile).HasMaxLength(64);

        var setupState = modelBuilder.Entity<SetupStateEntity>();
        setupState.ToTable("SetupState");
        setupState.HasKey(entity => entity.Id);
        setupState.Property(entity => entity.SelectedDefaultProfile).HasMaxLength(64);

        var traces = modelBuilder.Entity<RoutingExecutionTraceEntity>();
        traces.ToTable("RoutingExecutionTraces");
        traces.HasKey(entity => entity.Id);
        traces.Property(entity => entity.Id).ValueGeneratedOnAdd();
        traces.Property(entity => entity.TraceId).HasMaxLength(64);
        traces.Property(entity => entity.WorkflowEngine).HasMaxLength(128);
        traces.Property(entity => entity.PayloadJson).HasMaxLength(32768);
        traces.HasIndex(entity => entity.CreatedAtUtc);
        traces.HasIndex(entity => entity.TraceId).IsUnique();

        // Phase 8 - Continuous Evaluation
        var shadow = modelBuilder.Entity<ShadowRequestEntity>();
        shadow.ToTable("ShadowRequests");
        shadow.HasKey(e => e.Id);
        shadow.Property(e => e.Id).ValueGeneratedOnAdd();
        shadow.Property(e => e.ShadowId).HasMaxLength(64);
        shadow.Property(e => e.OriginalTraceId).HasMaxLength(64);
        shadow.Property(e => e.PrimaryProfile).HasMaxLength(64);
        shadow.Property(e => e.ShadowProfile).HasMaxLength(64);
        shadow.Property(e => e.PromptHash).HasMaxLength(64);
        shadow.Property(e => e.OutcomeLabel).HasMaxLength(32);
        shadow.HasIndex(e => e.ShadowId).IsUnique();
        shadow.HasIndex(e => e.OriginalTraceId);
        shadow.HasIndex(e => e.CreatedAtUtc);

        var confidence = modelBuilder.Entity<RuleConfidenceScoreEntity>();
        confidence.ToTable("RuleConfidenceScores");
        confidence.HasKey(e => e.Id);
        confidence.Property(e => e.Id).ValueGeneratedOnAdd();
        confidence.Property(e => e.RuleKey).HasMaxLength(128);
        confidence.Property(e => e.WindowLabel).HasMaxLength(64);
        confidence.HasIndex(e => new { e.RuleKey, e.WindowStartUtc });
        confidence.HasIndex(e => e.RecordedAtUtc);

        var benchmarkRun = modelBuilder.Entity<BenchmarkRunEntity>();
        benchmarkRun.ToTable("BenchmarkRuns");
        benchmarkRun.HasKey(e => e.RunId);
        benchmarkRun.Property(e => e.RunId).HasMaxLength(64).ValueGeneratedNever();
        benchmarkRun.Property(e => e.Name).HasMaxLength(128);
        benchmarkRun.Property(e => e.Description).HasMaxLength(512);
        benchmarkRun.Property(e => e.Status).HasMaxLength(32);
        benchmarkRun.HasIndex(e => e.Status);
        benchmarkRun.HasIndex(e => e.CreatedAtUtc);

        var benchmarkResult = modelBuilder.Entity<BenchmarkResultEntity>();
        benchmarkResult.ToTable("BenchmarkResults");
        benchmarkResult.HasKey(e => e.Id);
        benchmarkResult.Property(e => e.Id).ValueGeneratedOnAdd();
        benchmarkResult.Property(e => e.RunId).HasMaxLength(64);
        benchmarkResult.Property(e => e.ItemId).HasMaxLength(64);
        benchmarkResult.Property(e => e.Profile).HasMaxLength(64);
        benchmarkResult.Property(e => e.Deployment).HasMaxLength(128);
        benchmarkResult.Property(e => e.PromptHash).HasMaxLength(64);
        benchmarkResult.Property(e => e.JudgeVerdict).HasMaxLength(32);
        benchmarkResult.HasIndex(e => e.RunId);

        var approval = modelBuilder.Entity<ApprovalRequestEntity>();
        approval.ToTable("ApprovalRequests");
        approval.HasKey(e => e.ApprovalId);
        approval.Property(e => e.ApprovalId).HasMaxLength(64).ValueGeneratedNever();
        approval.Property(e => e.ChangeType).HasMaxLength(64);
        approval.Property(e => e.Title).HasMaxLength(256);
        approval.Property(e => e.Description).HasMaxLength(1024);
        approval.Property(e => e.Status).HasMaxLength(32);
        approval.Property(e => e.ReviewedBy).HasMaxLength(128);
        approval.Property(e => e.ReviewNotes).HasMaxLength(1024);
        approval.HasIndex(e => e.Status);
        approval.HasIndex(e => e.CreatedAtUtc);

        var team = modelBuilder.Entity<TeamProfileEntity>();
        team.ToTable("TeamProfiles");
        team.HasKey(e => e.TeamId);
        team.Property(e => e.TeamId).HasMaxLength(64).ValueGeneratedNever();
        team.Property(e => e.DisplayName).HasMaxLength(128);
        team.Property(e => e.DefaultProfile).HasMaxLength(64);

        var project = modelBuilder.Entity<ProjectProfileEntity>();
        project.ToTable("ProjectProfiles");
        project.HasKey(e => e.ProjectId);
        project.Property(e => e.ProjectId).HasMaxLength(64).ValueGeneratedNever();
        project.Property(e => e.TeamId).HasMaxLength(64);
        project.Property(e => e.DisplayName).HasMaxLength(128);
        project.Property(e => e.DefaultProfile).HasMaxLength(64);
        project.HasIndex(e => e.TeamId);
    }
}
