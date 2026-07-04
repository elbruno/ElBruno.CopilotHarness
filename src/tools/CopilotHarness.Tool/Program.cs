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
    config.AddCommand<DoctorCommand>("doctor")
          .WithDescription("Validate the full Copilot Harness setup (Aspire, proxy, agents, VS Code config).");
    config.AddCommand<UpdateCommand>("update")
          .WithDescription("Update harness agent files from the latest embedded templates.");
});
return app.Run(args);