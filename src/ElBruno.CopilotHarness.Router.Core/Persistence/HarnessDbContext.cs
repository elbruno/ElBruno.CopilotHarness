using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class HarnessDbContext(DbContextOptions<HarnessDbContext> options) : DbContext(options)
{
    public DbSet<ModelProfileEntity> ModelProfiles => Set<ModelProfileEntity>();
    public DbSet<RoutingRuleSettingsEntity> RoutingRuleSettings => Set<RoutingRuleSettingsEntity>();
    public DbSet<RoutingExecutionTraceEntity> RoutingExecutionTraces => Set<RoutingExecutionTraceEntity>();
    public DbSet<SetupStateEntity> SetupState => Set<SetupStateEntity>();

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
    }
}
