using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsagePricingCatalogAndEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsagePricingCards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InputUsdPer1MToken = table.Column<double>(type: "REAL", nullable: false),
                    OutputUsdPer1MToken = table.Column<double>(type: "REAL", nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SourceMetadataJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    IsOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsagePricingCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsagePricingCards_Provider_Model_EffectiveFromUtc",
                table: "UsagePricingCards",
                columns: new[] { "Provider", "Model", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsagePricingCards_Provider_Model_Operation_EffectiveFromUtc_IsOverride",
                table: "UsagePricingCards",
                columns: new[] { "Provider", "Model", "Operation", "EffectiveFromUtc", "IsOverride" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsagePricingCards");
        }
    }
}
