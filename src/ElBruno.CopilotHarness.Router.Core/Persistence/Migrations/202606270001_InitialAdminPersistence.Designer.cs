using System;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations;

[DbContext(typeof(HarnessDbContext))]
[Migration("202606270001_InitialAdminPersistence")]
partial class InitialAdminPersistence
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.9");

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.ModelProfileEntity", b =>
        {
            b.Property<string>("ProfileName")
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<string>("ApiVersion")
                .HasMaxLength(32)
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("TEXT");

            b.Property<string>("Deployment")
                .HasMaxLength(128)
                .HasColumnType("TEXT");

            b.Property<string>("DisplayName")
                .HasMaxLength(128)
                .HasColumnType("TEXT");

            b.Property<bool>("Enabled")
                .HasColumnType("INTEGER");

            b.Property<DateTimeOffset>("UpdatedAtUtc")
                .HasColumnType("TEXT");

            b.HasKey("ProfileName");

            b.ToTable("ModelProfiles", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.RoutingRuleSettingsEntity", b =>
        {
            b.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER");

            b.Property<string>("BigProfile")
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<int>("BigPromptCharacterThreshold")
                .HasColumnType("INTEGER");

            b.Property<string>("DefaultProfile")
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<bool>("PreferBigWhenSystemMessageExists")
                .HasColumnType("INTEGER");

            b.Property<bool>("PreferStreamingProfileWhenStreaming")
                .HasColumnType("INTEGER");

            b.Property<string>("StreamingProfile")
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.Property<DateTimeOffset>("UpdatedAtUtc")
                .HasColumnType("TEXT");

            b.HasKey("Id");

            b.ToTable("RoutingRuleSettings", (string)null);
        });

        modelBuilder.Entity("ElBruno.CopilotHarness.Router.Core.Persistence.SetupStateEntity", b =>
        {
            b.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER");

            b.Property<DateTimeOffset?>("CompletedAtUtc")
                .HasColumnType("TEXT");

            b.Property<bool>("IsCompleted")
                .HasColumnType("INTEGER");

            b.Property<string>("SelectedDefaultProfile")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            b.HasKey("Id");

            b.ToTable("SetupState", (string)null);
        });
#pragma warning restore 612, 618
    }
}
