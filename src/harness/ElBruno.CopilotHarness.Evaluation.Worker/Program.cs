using ElBruno.CopilotHarness.Evaluation.Worker;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// Resolve persistence — mirrors Router.Api convention.
// In Docker mode (UseContainers=true in AppHost), Aspire injects the PostgreSQL
// connection string as ConnectionStrings:copilotharness via WithReference.
// In SQLite mode (default), AppHost passes Persistence__DatabasePath.
var postgresConnectionString = builder.Configuration.GetConnectionString("copilotharness");

if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    // Docker mode: PostgreSQL
    builder.Services.AddDbContext<HarnessDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));
}
else
{
    // No-Docker mode: SQLite, using the same path as Router.Api
    var dbPath = builder.Configuration["Persistence:DatabasePath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, @"App_Data\copilotharness-admin.db");

    var resolvedPath = Path.IsPathRooted(dbPath)
        ? dbPath
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, dbPath));

    Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

    builder.Services.AddDbContext<HarnessDbContext>(options =>
        options.UseSqlite($"Data Source={resolvedPath}"));
}

// Phase 8 stores
builder.Services.AddScoped<BenchmarkStore>();
builder.Services.AddScoped<IBenchmarkStore>(sp => sp.GetRequiredService<BenchmarkStore>());
builder.Services.AddScoped<RuleConfidenceStore>();
builder.Services.AddScoped<IRuleConfidenceStore>(sp => sp.GetRequiredService<RuleConfidenceStore>());

// Periodic background jobs
builder.Services.AddHostedService<ContinuousBenchmarkJob>();
builder.Services.AddHostedService<RecommendationScheduler>();

var host = builder.Build();
host.Run();
