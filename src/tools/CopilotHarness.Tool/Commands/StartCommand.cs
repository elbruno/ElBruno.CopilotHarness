using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CopilotHarness.Tool.Commands;

public sealed class StartSettings : CommandSettings
{
    [CommandOption("--proxy-path")]
    public string? ProxyPath { get; init; }
}

public sealed class StartCommand : Command<StartSettings>
{
    private const string HealthUrl = "http://localhost:5101/health";

    protected override int Execute(CommandContext context, StartSettings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[bold yellow]Copilot Harness — Start Proxy[/]").RuleStyle("grey"));

        var proxyProjectPath = ResolveProxyPath(settings.ProxyPath);

        if (proxyProjectPath is null)
        {
            AnsiConsole.MarkupLine("[red]✗ Could not locate FoundryLocalProxy.[/]");
            AnsiConsole.MarkupLine("[grey]Install ElBruno.LLMProxies at C:\\src\\ElBruno.LLMProxies, run from that repo root, or pass:[/]");
            AnsiConsole.MarkupLine("[yellow]  harness start --proxy-path <path-to-FoundryLocalProxy>[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[grey]Proxy project: {proxyProjectPath}[/]");
        AnsiConsole.MarkupLine("[cyan]Launching FoundryLocalProxy… (Ctrl+C to stop)[/]\n");

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{proxyProjectPath}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        var process = Process.Start(psi);
        if (process is null)
        {
            AnsiConsole.MarkupLine("[red]✗ Failed to start dotnet process.[/]");
            return 1;
        }

        var proxyHealthy = false;

        // Wait up to 30 seconds for the proxy to become healthy
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[cyan]Waiting for proxy to be ready…[/]", ctx =>
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var response = http.GetAsync(HealthUrl).GetAwaiter().GetResult();
                        if (response.IsSuccessStatusCode)
                        {
                            ctx.Status("[green]Proxy is healthy![/]");
                            proxyHealthy = true;
                            return;
                        }
                    }
                    catch { /* not yet ready */ }
                    Task.Delay(1000).GetAwaiter().GetResult();
                }
                ctx.Status("[yellow]Proxy did not respond in time — it may still be starting.[/]");
            });

        AnsiConsole.MarkupLine($"\n[green]✓ Proxy started (PID {process.Id}).[/] Health: [cyan]{HealthUrl}[/]");

        if (proxyHealthy)
        {
            AnsiConsole.Write(new Panel(
                "[green]✓[/] FoundryLocalProxy running  →  [cyan]http://localhost:5101[/]\n" +
                "[green]✓[/] phi-4-mini loaded (check [cyan]/v1/models[/] for status)\n\n" +
                "[bold]Next steps:[/]\n" +
                "  1. Open VS Code → Copilot Chat → type [yellow]@harness-general[/]\n" +
                "  2. Try: [yellow]@harness-general start the web API[/]\n" +
                "  3. Run [yellow]harness doctor[/] to validate the full setup")
                .Header("[bold green]Ready[/]")
                .BorderColor(Color.Green));
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Run [yellow]harness status[/] to check at any time.[/]");
        }

        return 0;
    }

    private static string? ResolveProxyPath(string? explicitPath)
    {
        if (explicitPath is not null)
            return Directory.Exists(explicitPath) ? explicitPath : null;

        // Walk up from cwd looking for local or adjacent LLMProxies layouts.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var llmProxiesCandidate = Path.Combine(dir.FullName, "ElBruno.LLMProxies", "src", "proxies", "FoundryLocalProxy");
            if (Directory.Exists(llmProxiesCandidate))
                return llmProxiesCandidate;

            var candidate = Path.Combine(dir.FullName, "src", "proxies", "FoundryLocalProxy");
            if (Directory.Exists(candidate))
                return candidate;

            var legacyCandidate = Path.Combine(dir.FullName, "proxies", "FoundryLocalProxy");
            if (Directory.Exists(legacyCandidate))
                return legacyCandidate;

            var siblingRepoCandidate = Path.Combine(dir.FullName, "..", "ElBruno.LLMProxies", "src", "proxies", "FoundryLocalProxy");
            if (Directory.Exists(siblingRepoCandidate))
                return Path.GetFullPath(siblingRepoCandidate);

            dir = dir.Parent;
        }

        return null;
    }
}