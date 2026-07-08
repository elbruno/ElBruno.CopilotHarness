using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElBruno.CopilotHarness.Router.Core.Persistence.Migrations;

public partial class AddRoutingExecutionTraces : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RoutingExecutionTraces",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TraceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                WorkflowEngine = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoutingExecutionTraces", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RoutingExecutionTraces_CreatedAtUtc",
            table: "RoutingExecutionTraces",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_RoutingExecutionTraces_TraceId",
            table: "RoutingExecutionTraces",
            column: "TraceId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RoutingExecutionTraces");
    }
}
