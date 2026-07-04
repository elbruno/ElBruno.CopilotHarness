using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CopilotHarness.Tool.Commands;

public sealed class DoctorCommand : Command
{
    private const string HealthUrl = "http://localhost:5101/health";

    protected override int Execute(CommandContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold yellow]Copilot Harness — Doctor[/]").RuleStyle("grey"));

        var checks = new List<(string Check, bool Pass, string Note)>();

        // Check 1: Aspire CLI installed
        checks.Add(CheckAspireCli());

        // Check 2: FoundryLocalProxy healthy
        checks.Add(CheckProxyHealth());

        // Check 3: Agent files present
        checks.Add(CheckAgentFiles());

        // Check 4: chatLanguageModels.json exists in VS Code user config
        checks.Add(CheckChatLanguageModels());

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Check[/]")
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Note[/]");

        foreach (var (check, pass, note) in checks)
        {
            var status = pass ? "[green]✓ Pass[/]" : "[red]✗ Fail[/]";
            table.AddRow(check, status, note);
        }

        AnsiConsole.Write(table);

        var allPass = checks.All(c => c.Pass);
        if (allPass)
            AnsiConsole.MarkupLine("\n[green]✓ All checks passed.[/]");
        else
            AnsiConsole.MarkupLine("\n[red]✗ One or more checks failed. See hints above.[/]");

        return allPass ? 0 : 1;
    }

    private static (string, bool, string) CheckAspireCli()
    {
        try
        {
            var psi = new ProcessStartInfo("aspire")
            {
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc is null) return ("Aspire CLI installed", false, "Install: dotnet workload install aspire");
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode == 0)
                return ("Aspire CLI installed", true, Markup.Escape(output));
            return ("Aspire CLI installed", false, "Install: dotnet workload install aspire");
        }
        catch
        {
            return ("Aspire CLI installed", false, "Install: dotnet workload install aspire");
        }
    }

    private static (string, bool, string) CheckProxyHealth()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = http.GetAsync(HealthUrl).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
                return ("FoundryLocalProxy healthy", true, HealthUrl);
            return ("FoundryLocalProxy healthy", false, $"HTTP {(int)response.StatusCode} — Run: harness start");
        }
        catch
        {
            return ("FoundryLocalProxy healthy", false, "Run: harness start");
        }
    }

    private static (string, bool, string) CheckAgentFiles()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".github", "agents", "harness-general.agent.md");
            if (File.Exists(candidate))
                return ("Agent files present", true, Markup.Escape(candidate));
            dir = dir.Parent;
        }
        return ("Agent files present", false, "Run: harness init");
    }

    private static (string, bool, string) CheckChatLanguageModels()
    {
        foreach (var vsCodeDir in InitCommand.GetVsCodeUserDirs())
        {
            var target = Path.Combine(vsCodeDir, "chatLanguageModels.json");
            if (File.Exists(target))
            {
                try
                {
                    var content = File.ReadAllText(target);
                    if (content.Contains("phi-4-mini"))
                        return ("chatLanguageModels.json configured", true, Markup.Escape(target));
                    return ("chatLanguageModels.json configured", false, "File exists but missing phi-4-mini — Run: harness init");
                }
                catch
                {
                    return ("chatLanguageModels.json configured", false, "Could not read file — Run: harness init");
                }
            }
        }
        return ("chatLanguageModels.json configured", false, "Run: harness init");
    }
}
