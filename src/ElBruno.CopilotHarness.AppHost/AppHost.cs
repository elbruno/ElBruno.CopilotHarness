var builder = DistributedApplication.CreateBuilder(args);

var foundryEndpoint = builder.AddParameter("FoundryEndpoint");
var foundryApiKey = builder.AddParameter("FoundryApiKey", secret: true);

builder.AddProject<Projects.ElBruno_CopilotHarness_Router_Api>("router-api")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey);

builder.Build().Run();
