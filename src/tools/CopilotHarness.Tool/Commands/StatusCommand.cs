using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CopilotHarness.Tool.Commands;

public sealed class StatusCommand : Command
{
    private const string HealthUrl = "http://localhost:5101/health";

    protected override int Execute(CommandContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold yellow]Copilot Harness — Proxy Status[/]").RuleStyle("grey"));

        string? json = null;

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[cyan]Checking proxy health…[/]", ctx =>
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                try
                {
                    var response = http.GetAsync(HealthUrl).GetAwaiter().GetResult();
                    json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    ctx.Status("[green]Response received[/]");
                }
                catch (Exception ex)
                {
                    json = null;
                    ctx.Status($"[red]Error: {ex.Message}[/]");
                }
            });

        if (json is null)
        {
            AnsiConsole.MarkupLine("\n[red]✗ FoundryLocalProxy is not reachable at[/] [cyan]" + HealthUrl + "[/]");
            AnsiConsole.MarkupLine("[grey]Start it with: [yellow]harness start[/][/]");
            return 1;
        }

        try
        {
            var doc = JsonNode.Parse(json);
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]Property[/]")
                .AddColumn("[bold]Value[/]");

            void AddRow(string key, string? value) =>
                table.AddRow($"[cyan]{key}[/]", value is null ? "[grey]n/a[/]" : Markup.Escape(value));

            AddRow("Proxy name",     doc?["proxy"]?.GetValue<string>());
            AddRow("Status",         doc?["status"]?.GetValue<string>());

            var models = doc?["loadedModels"];
            if (models is JsonArray arr && arr.Count > 0)
            {
                foreach (var m in arr)
                    AddRow("Loaded model", m?.GetValue<string>());
            }
            else
            {
                AddRow("Loaded models", "(none)");
            }

            AddRow("Internal REST",  doc?["internalRestServer"]?.GetValue<string>());
            AddRow("Default model",  doc?["model"]?.GetValue<string>());

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("\n[green]✓ Proxy is healthy.[/]");
        }
        catch
        {
            // Not structured JSON — show raw
            AnsiConsole.MarkupLine($"\n[green]✓ Proxy responded:[/] {Markup.Escape(json)}");
        }

        return 0;
    }
}
