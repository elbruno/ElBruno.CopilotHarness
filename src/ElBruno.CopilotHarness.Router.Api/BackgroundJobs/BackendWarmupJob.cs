using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Api.BackgroundJobs;

public sealed class BackendWarmupJob(ILogger<BackendWarmupJob> logger)
{
    public async ValueTask RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dbContext = services.GetService<HarnessDbContext>();
        if (dbContext is not null)
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            logger.LogInformation("Backend database warmup completed. CanConnect={CanConnect}", canConnect);
        }

        var cache = services.GetService<IDistributedCache>();
        if (cache is not null)
        {
            const string key = "backend:warmup";
            await cache.SetStringAsync(
                key,
                "ok",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
                cancellationToken);
            await cache.RemoveAsync(key, cancellationToken);
            logger.LogInformation("Backend cache warmup completed.");
        }
    }
}
