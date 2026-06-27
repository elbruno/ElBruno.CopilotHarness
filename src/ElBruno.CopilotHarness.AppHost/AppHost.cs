var builder = DistributedApplication.CreateBuilder(args);

var foundryEndpoint = builder.AddParameter("FoundryEndpoint");
var foundryApiKey = builder.AddParameter("FoundryApiKey", secret: true);
var adminApiKey = builder.AddParameter("AdminApiKey", secret: true);
var judgeDbPath = builder.AddParameter("JudgeDbPath", @"App_Data\copilotharness-judge.db");

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var routerDatabase = postgres.AddDatabase("copilotharness");
var redis = builder.AddRedis("redis").WithDataVolume();

var routerApi = builder.AddProject<Projects.ElBruno_CopilotHarness_Router_Api>("router-api")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("Backend__Auth__AdminApiKey", adminApiKey)
    .WithEnvironment("Persistence__Provider", "PostgreSql")
    .WithReference(routerDatabase)
    .WithReference(redis);

builder.AddProject<Projects.ElBruno_CopilotHarness_Judge_Web>("judge-web")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("JudgePersistence__DatabasePath", judgeDbPath);

builder.AddProject<Projects.ElBruno_CopilotHarness_Admin_Web>("admin-web")
    .WithReference(routerApi)
    .WaitFor(routerApi);

builder.Build().Run();
