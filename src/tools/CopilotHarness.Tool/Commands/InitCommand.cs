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

        // Warn if agent files already exist
        if (Directory.Exists(targetAgentsDir) && Directory.GetFiles(targetAgentsDir, "harness-*.md").Length > 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠  Harness agent files already exist in .github/agents/ — they will be overwritten.[/]");
        }

        Directory.CreateDirectory(targetAgentsDir);

        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames();

        var agentsWritten = new List<string>();
        string? vsCodeSettingsContent = null;

        foreach (var resource in resources)
        {
            // Templates/agents/*.md  →  .github/agents/
            if (resource.Contains("Templates.agents") && resource.EndsWith(".md"))
            {
                var agentFileName = ExtractFileName(resource, ".md");
                var destPath = Path.Combine(targetAgentsDir, agentFileName);

                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var file = File.Create(destPath);
                stream.CopyTo(file);
                agentsWritten.Add(agentFileName);
            }
            // Templates/vscode-settings.json — read into memory for VS Code placement
            else if (resource.Contains("Templates") && resource.EndsWith("vscode-settings.json"))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                vsCodeSettingsContent = reader.ReadToEnd();
            }
        }

        // Attempt to write chatLanguageModels.json to VS Code user config
        var (vsCodeWritten, vsCodePath) = TryWriteChatLanguageModels(vsCodeSettingsContent);

        // Summary table
        var table = new Table().Border(TableBorder.Rounded).AddColumn("File").AddColumn("Location");
        foreach (var agent in agentsWritten)
            table.AddRow($"[green]{agent}[/]", $".github/agents/{agent}");

        if (vsCodeWritten && vsCodePath is not null)
            table.AddRow("[green]chatLanguageModels.json[/]", Markup.Escape(vsCodePath));
        else
            table.AddRow("[yellow]copilot-harness-settings.json[/]", "./copilot-harness-settings.json (fallback)");

        AnsiConsole.Write(table);

        // Build Next Steps text based on whether VS Code config was auto-written
        string nextSteps;
        if (vsCodeWritten && vsCodePath is not null)
        {
            nextSteps =
                "[bold]Next steps:[/]\n\n" +
                $"1. [green]chatLanguageModels.json[/] was auto-written to:\n" +
                $"   [cyan]{Markup.Escape(vsCodePath)}[/]\n" +
                "   Restart VS Code if it was already open.\n\n" +
                "2. Start the local proxy:\n" +
                "   [yellow]harness start[/]\n\n" +
                "3. In VS Code Copilot Chat, try:\n" +
                "   [yellow]@harness-general What can you help me with?[/]";
        }
        else
        {
            nextSteps =
                "[bold]Next steps:[/]\n\n" +
                "1. VS Code config folder not found — [yellow]copilot-harness-settings.json[/] was written to the current directory.\n" +
                "   Manually rename it to [cyan]chatLanguageModels.json[/] and place it in your VS Code user config folder:\n" +
                "   - Windows: [grey]%APPDATA%\\Code\\User\\[/]\n" +
                "   - macOS:   [grey]~/Library/Application Support/Code/User/[/]\n" +
                "   - Linux:   [grey]~/.config/Code/User/[/]\n\n" +
                "2. Start the local proxy:\n" +
                "   [yellow]harness start[/]\n\n" +
                "3. In VS Code Copilot Chat, try:\n" +
                "   [yellow]@harness-general What can you help me with?[/]";
        }

        AnsiConsole.Write(new Panel(nextSteps)
            .Header("[bold green]✓ Harness files installed[/]")
            .BorderColor(Color.Green));

        // Aspire AppHost detection
        var appHost = FindAspireAppHost(Directory.GetCurrentDirectory());
        if (appHost is not null)
        {
            AnsiConsole.MarkupLine(
                $"[blue]ℹ  Aspire AppHost detected: [cyan]{Markup.Escape(appHost)}[/]\n" +
                "   Consider wiring FoundryLocalProxy into your Aspire orchestration.[/]");
        }

        return 0;
    }

    internal static string? FindAspireAppHost(string startDir)
    {
        foreach (var csproj in Directory.EnumerateFiles(startDir, "*.csproj", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csproj);
            if (content.Contains("Aspire.AppHost") || content.Contains("Aspire.Hosting"))
                return csproj;
        }
        return null;
    }

    private static (bool written, string? path) TryWriteChatLanguageModels(string? content)
    {
        if (content is null) return (false, null);

        foreach (var dir in GetVsCodeUserDirs())
        {
            if (!Directory.Exists(dir)) continue;

            var target = Path.Combine(dir, "chatLanguageModels.json");
            try
            {
                File.WriteAllText(target, content);
                AnsiConsole.MarkupLine($"[green]✓ chatLanguageModels.json written to {Markup.Escape(target)}[/]");
                return (true, target);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠  Could not write to {Markup.Escape(target)}: {Markup.Escape(ex.Message)}[/]");
            }
        }

        // Fallback: write copilot-harness-settings.json in cwd
        AnsiConsole.MarkupLine("[yellow]⚠  VS Code user config folder not found — writing copilot-harness-settings.json in current directory.[/]");
        var fallback = Path.Combine(Directory.GetCurrentDirectory(), "copilot-harness-settings.json");
        File.WriteAllText(fallback, content);
        return (false, fallback);
    }

    internal static IEnumerable<string> GetVsCodeUserDirs()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(appData, "Code", "User");
            yield return Path.Combine(appData, "Code - Insiders", "User");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, "Library", "Application Support", "Code", "User");
            yield return Path.Combine(home, "Library", "Application Support", "Code - Insiders", "User");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".config", "Code", "User");
            yield return Path.Combine(home, ".config", "Code - Insiders", "User");
        }
    }

    private static string ExtractFileName(string resourceName, string extension)
    {
        // e.g. "CopilotHarness.Tool.Templates.agents.harness-general.agent.md"
        // → "harness-general.agent.md"
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