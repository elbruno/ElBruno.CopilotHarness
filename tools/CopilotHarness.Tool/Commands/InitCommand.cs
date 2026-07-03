using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CopilotHarness.Tool.Commands;

public sealed class InitCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold yellow]Copilot Harness Init[/]").RuleStyle("grey"));

        var targetAgentsDir = Path.Combine(Directory.GetCurrentDirectory(), ".github", "agents");
        var settingsTarget = Path.Combine(Directory.GetCurrentDirectory(), "copilot-harness-settings.json");

        // Warn if agent files already exist
        if (Directory.Exists(targetAgentsDir) && Directory.GetFiles(targetAgentsDir, "harness-*.md").Length > 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠  Harness agent files already exist in .github/agents/ — they will be overwritten.[/]");
        }

        Directory.CreateDirectory(targetAgentsDir);

        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames();

        var agentsWritten = new List<string>();

        foreach (var resource in resources)
        {
            // Templates/agents/*.md  →  .github/agents/
            if (resource.Contains("Templates.agents") && resource.EndsWith(".md"))
            {
                var fileName = resource.Split('.')[^2] + ".md";
                // Reconstruct filename: last two segments before extension
                var parts = resource.Split('.');
                // e.g. CopilotHarness.Tool.Templates.agents.harness-general.agent.md
                // We want "harness-general.agent.md"
                var agentFileName = ExtractFileName(resource, ".md");
                var destPath = Path.Combine(targetAgentsDir, agentFileName);

                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var file = File.Create(destPath);
                stream.CopyTo(file);
                agentsWritten.Add(agentFileName);
            }
            // Templates/vscode-settings.json  →  copilot-harness-settings.json
            else if (resource.Contains("Templates") && resource.EndsWith("vscode-settings.json"))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var file = File.Create(settingsTarget);
                stream.CopyTo(file);
            }
        }

        // Summary table
        var table = new Table().Border(TableBorder.Rounded).AddColumn("File").AddColumn("Location");
        foreach (var agent in agentsWritten)
            table.AddRow($"[green]{agent}[/]", $".github/agents/{agent}");
        table.AddRow("[green]copilot-harness-settings.json[/]", "./copilot-harness-settings.json");

        AnsiConsole.Write(table);

        AnsiConsole.Write(new Panel(
            "[bold]Next steps:[/]\n\n" +
            "1. Merge [cyan]copilot-harness-settings.json[/] into your VS Code [cyan]settings.json[/]\n" +
            "   ([grey]Ctrl+Shift+P → Open User Settings JSON[/])\n\n" +
            "2. Start the local proxy:\n" +
            "   [yellow]harness start[/]\n\n" +
            "3. In VS Code Copilot Chat, try:\n" +
            "   [yellow]@harness-general What can you help me with?[/]")
            .Header("[bold green]✓ Harness files installed[/]")
            .BorderColor(Color.Green));

        return 0;
    }

    private static string ExtractFileName(string resourceName, string extension)
    {
        // Remove assembly prefix and extension, then reconstruct filename
        // e.g. "CopilotHarness.Tool.Templates.agents.harness-general.agent.md"
        // → "harness-general.agent.md"
        var withoutExt = resourceName[..^extension.Length]; // remove ".md"
        var parts = withoutExt.Split('.');
        // Find the index of "agents" segment
        var idx = Array.IndexOf(parts, "agents");
        if (idx >= 0 && idx + 1 < parts.Length)
        {
            var nameParts = parts[(idx + 1)..];
            return string.Join(".", nameParts) + extension;
        }
        // Fallback: use last two dot-segments + extension
        return parts[^1] + extension;
    }
}
