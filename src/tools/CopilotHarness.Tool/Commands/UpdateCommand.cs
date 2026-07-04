using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CopilotHarness.Tool.Commands;

public sealed class UpdateCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold yellow]Copilot Harness — Update Agent Files[/]").RuleStyle("grey"));

        var targetAgentsDir = Path.Combine(Directory.GetCurrentDirectory(), ".github", "agents");
        Directory.CreateDirectory(targetAgentsDir);

        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames();

        var results = new List<(string File, string Status)>();

        foreach (var resource in resources)
        {
            if (!resource.Contains("Templates.agents") || !resource.EndsWith(".md"))
                continue;

            var agentFileName = ExtractFileName(resource, ".md");
            if (!agentFileName.StartsWith("harness-"))
                continue;

            var destPath = Path.Combine(targetAgentsDir, agentFileName);

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var newContent = reader.ReadToEnd();

            if (!File.Exists(destPath))
            {
                File.WriteAllText(destPath, newContent);
                results.Add((agentFileName, "[green]Added[/]"));
            }
            else
            {
                var existing = File.ReadAllText(destPath);
                if (existing == newContent)
                {
                    results.Add((agentFileName, "[grey]Up to date[/]"));
                }
                else
                {
                    File.WriteAllText(destPath, newContent);
                    results.Add((agentFileName, "[yellow]Updated[/]"));
                }
            }
        }

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No harness agent templates found in assembly resources.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]File[/]")
            .AddColumn("[bold]Status[/]");

        foreach (var (file, status) in results)
            table.AddRow($"[cyan]{file}[/]", status);

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[green]✓ Update complete.[/] {results.Count} file(s) processed.");

        return 0;
    }

    private static string ExtractFileName(string resourceName, string extension)
    {
        var withoutExt = resourceName[..^extension.Length];
        var parts = withoutExt.Split('.');
        var idx = Array.IndexOf(parts, "agents");
        if (idx >= 0 && idx + 1 < parts.Length)
        {
            var nameParts = parts[(idx + 1)..];
            return string.Join(".", nameParts) + extension;
        }
        return parts[^1] + extension;
    }
}
