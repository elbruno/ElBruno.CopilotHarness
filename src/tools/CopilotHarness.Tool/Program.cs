using Spectre.Console.Cli;
using CopilotHarness.Tool.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("harness");
    config.AddCommand<InitCommand>("init")
          .WithDescription("Copy Copilot Harness agent files and VS Code settings into the current repo.");
    config.AddCommand<StartCommand>("start")
          .WithDescription("Start the FoundryLocalProxy (downloads model on first run).");
    config.AddCommand<StatusCommand>("status")
          .WithDescription("Check if FoundryLocalProxy is running and healthy.");
});
return app.Run(args);
