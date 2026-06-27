var builder = DistributedApplication.CreateBuilder(args);

var foundryEndpoint = builder.AddParameter("FoundryEndpoint");
var foundryApiKey = builder.AddParameter("FoundryApiKey", secret: true);
var adminDbPath = builder.AddParameter("AdminDbPath", @"App_Data\copilotharness-admin.db");

var routerApi = builder.AddProject<Projects.ElBruno_CopilotHarness_Router_Api>("router-api")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("Persistence__DatabasePath", adminDbPath);

builder.AddProject<Projects.ElBruno_CopilotHarness_Admin_Web>("admin-web")
    .WithReference(routerApi)
    .WaitFor(routerApi);

builder.Build().Run();
