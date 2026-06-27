using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations;

public partial class InitialAdminPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ModelProfiles",
            columns: table => new
            {
                ProfileName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Deployment = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ApiVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ModelProfiles", x => x.ProfileName);
            });

        migrationBuilder.CreateTable(
            name: "RoutingRuleSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                DefaultProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                BigPromptCharacterThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                BigProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                StreamingProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PreferBigWhenSystemMessageExists = table.Column<bool>(type: "INTEGER", nullable: false),
                PreferStreamingProfileWhenStreaming = table.Column<bool>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoutingRuleSettings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SetupState",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                SelectedDefaultProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SetupState", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ModelProfiles");

        migrationBuilder.DropTable(
            name: "RoutingRuleSettings");

        migrationBuilder.DropTable(
            name: "SetupState");
    }
}
