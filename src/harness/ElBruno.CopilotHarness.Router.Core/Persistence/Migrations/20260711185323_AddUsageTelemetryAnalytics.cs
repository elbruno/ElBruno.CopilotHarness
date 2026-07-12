using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations;

public partial class AddUsageTelemetryAnalytics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UsageTelemetryEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IngestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Proxy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RequestModel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ResponseModel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                SpanId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                Operation = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                TotalTokens = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsageTelemetryEvents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UsageTelemetryEvents_IdempotencyKey",
            table: "UsageTelemetryEvents",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UsageTelemetryEvents_OccurredAtUtc",
            table: "UsageTelemetryEvents",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_UsageTelemetryEvents_Proxy_Provider_OccurredAtUtc",
            table: "UsageTelemetryEvents",
            columns: ["Proxy", "Provider", "OccurredAtUtc"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UsageTelemetryEvents");
    }
}
