var builder = DistributedApplication.CreateBuilder(args);

var foundryEndpoint = builder.AddParameter("FoundryEndpoint");
var foundryApiKey = builder.AddParameter("FoundryApiKey", secret: true);
var adminApiKey = builder.AddParameter("AdminApiKey", secret: true);
var judgeDbPath = builder.AddParameter("JudgeDbPath", @"App_Data\copilotharness-judge.db");

// useContainers=true wires PostgreSQL + Redis (requires Docker).
// Default is false so the harness works with aspire run and no container runtime.
var useContainers = builder.Configuration["UseContainers"] == "true";

var routerApi = builder.AddProject<Projects.ElBruno_CopilotHarness_Router_Api>("router-api")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("Backend__Auth__AdminApiKey", adminApiKey);

var evaluationWorker = builder.AddProject<Projects.ElBruno_CopilotHarness_Evaluation_Worker>("evaluation-worker")
    .WaitFor(routerApi);

if (useContainers)
{
    // Production: PostgreSQL + Redis via Docker containers
    var postgres = builder.AddPostgres("postgres").WithDataVolume();
    var routerDatabase = postgres.AddDatabase("copilotharness");
    var redis = builder.AddRedis("redis").WithDataVolume();

    routerApi
        .WithEnvironment("Persistence__Provider", "PostgreSql")
        .WithReference(routerDatabase)
        .WithReference(redis);

    evaluationWorker.WithReference(routerDatabase);
}
// else: SQLite (default) — no containers, no Docker required

builder.AddProject<Projects.ElBruno_CopilotHarness_Judge_Web>("judge-web")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("JudgePersistence__DatabasePath", judgeDbPath);

builder.AddProject<Projects.ElBruno_CopilotHarness_Admin_Web>("admin-web")
    .WithReference(routerApi)
    .WaitFor(routerApi);

builder.Build().Run();
