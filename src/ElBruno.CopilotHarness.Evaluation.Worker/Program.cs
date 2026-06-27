using ElBruno.CopilotHarness.Evaluation.Worker;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// SQLite db — same path convention as Router.Api (data/harness.db relative to content root)
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "harness.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<HarnessDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

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
