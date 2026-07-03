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
            AnsiConsole.MarkupLine("[red]✗ Could not locate samples/FoundryLocalProxy/.[/]");
            AnsiConsole.MarkupLine("[grey]Clone the ElBruno.CopilotHarness repo and run from its root, or pass:[/]");
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
                            return;
                        }
                    }
                    catch { /* not yet ready */ }
                    Task.Delay(1000).GetAwaiter().GetResult();
                }
                ctx.Status("[yellow]Proxy did not respond in time — it may still be starting.[/]");
            });

        AnsiConsole.MarkupLine($"\n[green]✓ Proxy started (PID {process.Id}).[/] Health: [cyan]{HealthUrl}[/]");
        AnsiConsole.MarkupLine("[grey]Run [yellow]harness status[/] to check at any time.[/]");

        return 0;
    }

    private static string? ResolveProxyPath(string? explicitPath)
    {
        if (explicitPath is not null)
            return Directory.Exists(explicitPath) ? explicitPath : null;

        // Walk up from cwd looking for samples/FoundryLocalProxy
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "FoundryLocalProxy");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
