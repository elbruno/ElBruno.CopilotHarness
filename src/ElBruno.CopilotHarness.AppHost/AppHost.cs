var builder = DistributedApplication.CreateBuilder(args);

var foundryEndpoint = builder.AddParameter("FoundryEndpoint");
var foundryApiKey = builder.AddParameter("FoundryApiKey", secret: true);
var adminApiKey = builder.AddParameter("AdminApiKey", secret: true);
var judgeDbPath = builder.AddParameter("JudgeDbPath", @"App_Data\copilotharness-judge.db");

// UseContainers=true wires PostgreSQL + Redis (requires Docker).
// Default is false so the harness works with aspire run and no container runtime.
var useContainers = builder.Configuration["UseContainers"] == "true";

// Shared SQLite path used by Router.Api and Evaluation.Worker in no-Docker mode.
const string sharedSqlitePath = @"App_Data\copilotharness-admin.db";

var routerApi = builder.AddProject<Projects.ElBruno_CopilotHarness_Router_Api>("router-api")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("Telemetry__CapturePromptText", "true")
    .WithEnvironment("ResponseAnnotation__Enabled", "true")
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

    evaluationWorker
        .WithEnvironment("Persistence__Provider", "PostgreSql")
        .WithReference(routerDatabase);
}
else
{
    // Default: SQLite — no containers, no Docker required.
    // Both Router.Api and Evaluation.Worker share the same SQLite file.
    routerApi.WithEnvironment("Persistence__DatabasePath", sharedSqlitePath);
    evaluationWorker.WithEnvironment("Persistence__DatabasePath", sharedSqlitePath);
}

builder.AddProject<Projects.ElBruno_CopilotHarness_Judge_Web>("judge-web")
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("JudgePersistence__DatabasePath", judgeDbPath);

builder.AddProject<Projects.ElBruno_CopilotHarness_Admin_Web>("admin-web")
    .WithReference(routerApi)
    .WithEnvironment("AdminApi__ApiKey", adminApiKey)
    .WaitFor(routerApi);

builder.Build().Run();
