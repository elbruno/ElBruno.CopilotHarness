using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations;

public partial class AddPhase8ContinuousEvaluation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShadowRequests",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ShadowId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                OriginalTraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PrimaryProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ShadowProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PromptHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PrimaryLatencyMs = table.Column<double>(type: "REAL", nullable: false),
                ShadowLatencyMs = table.Column<double>(type: "REAL", nullable: false),
                PrimaryStatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                ShadowStatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                OutcomeLabel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShadowRequests", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShadowRequests_ShadowId",
            table: "ShadowRequests",
            column: "ShadowId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShadowRequests_OriginalTraceId",
            table: "ShadowRequests",
            column: "OriginalTraceId");

        migrationBuilder.CreateIndex(
            name: "IX_ShadowRequests_CreatedAtUtc",
            table: "ShadowRequests",
            column: "CreatedAtUtc");

        migrationBuilder.CreateTable(
            name: "RuleConfidenceScores",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                RuleKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                TotalInvocations = table.Column<int>(type: "INTEGER", nullable: false),
                SuccessfulInvocations = table.Column<int>(type: "INTEGER", nullable: false),
                ConfidenceScore = table.Column<double>(type: "REAL", nullable: false),
                WindowLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                WindowStartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                WindowEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RuleConfidenceScores", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RuleConfidenceScores_RuleKey_WindowStartUtc",
            table: "RuleConfidenceScores",
            columns: ["RuleKey", "WindowStartUtc"]);

        migrationBuilder.CreateIndex(
            name: "IX_RuleConfidenceScores_RecordedAtUtc",
            table: "RuleConfidenceScores",
            column: "RecordedAtUtc");

        migrationBuilder.CreateTable(
            name: "BenchmarkRuns",
            columns: table => new
            {
                RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                ProfilesJson = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                TotalItems = table.Column<int>(type: "INTEGER", nullable: false),
                CompletedItems = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BenchmarkRuns", x => x.RunId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BenchmarkRuns_Status",
            table: "BenchmarkRuns",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_BenchmarkRuns_CreatedAtUtc",
            table: "BenchmarkRuns",
            column: "CreatedAtUtc");

        migrationBuilder.CreateTable(
            name: "BenchmarkResults",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ItemId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Profile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Deployment = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                PromptHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                LatencyMs = table.Column<double>(type: "REAL", nullable: false),
                PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                JudgeVerdict = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                JudgeScore = table.Column<double>(type: "REAL", nullable: false),
                MetricsJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BenchmarkResults", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BenchmarkResults_RunId",
            table: "BenchmarkResults",
            column: "RunId");

        migrationBuilder.CreateTable(
            name: "ApprovalRequests",
            columns: table => new
            {
                ApprovalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ChangeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ReviewedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApprovalRequests", x => x.ApprovalId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApprovalRequests_Status",
            table: "ApprovalRequests",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_ApprovalRequests_CreatedAtUtc",
            table: "ApprovalRequests",
            column: "CreatedAtUtc");

        migrationBuilder.CreateTable(
            name: "TeamProfiles",
            columns: table => new
            {
                TeamId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DefaultProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RulesJson = table.Column<string>(type: "TEXT", nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamProfiles", x => x.TeamId);
            });

        migrationBuilder.CreateTable(
            name: "ProjectProfiles",
            columns: table => new
            {
                ProjectId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                TeamId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DefaultProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RulesJson = table.Column<string>(type: "TEXT", nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectProfiles", x => x.ProjectId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectProfiles_TeamId",
            table: "ProjectProfiles",
            column: "TeamId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShadowRequests");
        migrationBuilder.DropTable(name: "RuleConfidenceScores");
        migrationBuilder.DropTable(name: "BenchmarkRuns");
        migrationBuilder.DropTable(name: "BenchmarkResults");
        migrationBuilder.DropTable(name: "ApprovalRequests");
        migrationBuilder.DropTable(name: "TeamProfiles");
        migrationBuilder.DropTable(name: "ProjectProfiles");
    }
}
