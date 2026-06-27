using System;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations;

[DbContext(typeof(HarnessDbContext))]
[Migration("202606270003_AddPhase8ContinuousEvaluation")]
partial class AddPhase8ContinuousEvaluation
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.9");

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.ModelProfileEntity", b =>
        {
            b.Property<string>("ProfileName").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("ApiVersion").HasMaxLength(32).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<string>("Deployment").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("DisplayName").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<bool>("Enabled").HasColumnType("INTEGER");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("TEXT");
            b.HasKey("ProfileName");
            b.ToTable("ModelProfiles", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.RoutingExecutionTraceEntity", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<string>("PayloadJson").HasMaxLength(32768).HasColumnType("TEXT");
            b.Property<string>("TraceId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("WorkflowEngine").HasMaxLength(128).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("CreatedAtUtc");
            b.HasIndex("TraceId").IsUnique();
            b.ToTable("RoutingExecutionTraces", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.RoutingRuleSettingsEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("BigProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<int>("BigPromptCharacterThreshold").HasColumnType("INTEGER");
            b.Property<string>("DefaultProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<bool>("PreferBigWhenSystemMessageExists").HasColumnType("INTEGER");
            b.Property<bool>("PreferStreamingProfileWhenStreaming").HasColumnType("INTEGER");
            b.Property<string>("StreamingProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("RoutingRuleSettings", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.SetupStateEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("TEXT");
            b.Property<bool>("IsCompleted").HasColumnType("INTEGER");
            b.Property<string>("SelectedDefaultProfile").IsRequired().HasMaxLength(64).HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("SetupState", (string)null);
        });

        // Phase 8 entities
        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.ShadowRequestEntity", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("ShadowId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("OriginalTraceId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("PrimaryProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("ShadowProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("PromptHash").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<double>("PrimaryLatencyMs").HasColumnType("REAL");
            b.Property<double>("ShadowLatencyMs").HasColumnType("REAL");
            b.Property<int>("PrimaryStatusCode").HasColumnType("INTEGER");
            b.Property<int>("ShadowStatusCode").HasColumnType("INTEGER");
            b.Property<string>("OutcomeLabel").HasMaxLength(32).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("ShadowId").IsUnique();
            b.HasIndex("OriginalTraceId");
            b.HasIndex("CreatedAtUtc");
            b.ToTable("ShadowRequests", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.RuleConfidenceScoreEntity", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("RuleKey").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<int>("TotalInvocations").HasColumnType("INTEGER");
            b.Property<int>("SuccessfulInvocations").HasColumnType("INTEGER");
            b.Property<double>("ConfidenceScore").HasColumnType("REAL");
            b.Property<string>("WindowLabel").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("WindowStartUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("WindowEndUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("RecordedAtUtc").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("RecordedAtUtc");
            b.ToTable("RuleConfidenceScores", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.BenchmarkRunEntity", b =>
        {
            b.Property<string>("RunId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("Name").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("Description").HasMaxLength(512).HasColumnType("TEXT");
            b.Property<string>("ProfilesJson").HasColumnType("TEXT");
            b.Property<string>("Status").HasMaxLength(32).HasColumnType("TEXT");
            b.Property<int>("TotalItems").HasColumnType("INTEGER");
            b.Property<int>("CompletedItems").HasColumnType("INTEGER");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("StartedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("TEXT");
            b.HasKey("RunId");
            b.HasIndex("Status");
            b.HasIndex("CreatedAtUtc");
            b.ToTable("BenchmarkRuns", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.BenchmarkResultEntity", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("RunId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("ItemId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("Profile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("Deployment").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("PromptHash").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<double>("LatencyMs").HasColumnType("REAL");
            b.Property<int>("PromptTokens").HasColumnType("INTEGER");
            b.Property<int>("CompletionTokens").HasColumnType("INTEGER");
            b.Property<int>("StatusCode").HasColumnType("INTEGER");
            b.Property<string>("JudgeVerdict").HasMaxLength(32).HasColumnType("TEXT");
            b.Property<double>("JudgeScore").HasColumnType("REAL");
            b.Property<string>("MetricsJson").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("RunId");
            b.ToTable("BenchmarkResults", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.ApprovalRequestEntity", b =>
        {
            b.Property<string>("ApprovalId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("ChangeType").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("Title").HasMaxLength(256).HasColumnType("TEXT");
            b.Property<string>("Description").HasMaxLength(1024).HasColumnType("TEXT");
            b.Property<string>("PayloadJson").HasColumnType("TEXT");
            b.Property<string>("Status").HasMaxLength(32).HasColumnType("TEXT");
            b.Property<string>("ReviewedBy").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("ReviewNotes").HasMaxLength(1024).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("ReviewedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("ExpiresAtUtc").HasColumnType("TEXT");
            b.HasKey("ApprovalId");
            b.HasIndex("Status");
            b.HasIndex("CreatedAtUtc");
            b.ToTable("ApprovalRequests", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.TeamProfileEntity", b =>
        {
            b.Property<string>("TeamId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("DisplayName").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("DefaultProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("RulesJson").HasColumnType("TEXT");
            b.Property<bool>("Enabled").HasColumnType("INTEGER");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("TEXT");
            b.HasKey("TeamId");
            b.ToTable("TeamProfiles", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.ProjectProfileEntity", b =>
        {
            b.Property<string>("ProjectId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("TeamId").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("DisplayName").HasMaxLength(128).HasColumnType("TEXT");
            b.Property<string>("DefaultProfile").HasMaxLength(64).HasColumnType("TEXT");
            b.Property<string>("RulesJson").HasColumnType("TEXT");
            b.Property<bool>("Enabled").HasColumnType("INTEGER");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnType("TEXT");
            b.HasKey("ProjectId");
            b.HasIndex("TeamId");
            b.ToTable("ProjectProfiles", (string)null);
        });
#pragma warning restore 612, 618
    }
}
