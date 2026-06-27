using System.Threading.RateLimiting;

namespace ElBruno.CopilotHarness.Router.Api.RateLimiting;

public static class BackendRateLimiter
{
    public static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter(int permitLimit, TimeSpan window)
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var partitionKey = ClientRateLimiting.GetPartitionKey(context);
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });
    }
}
