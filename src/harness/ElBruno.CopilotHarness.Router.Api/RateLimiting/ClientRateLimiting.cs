namespace ElBruno.CopilotHarness.Router.Api.RateLimiting;

public static class ClientRateLimiting
{
    public static string GetPartitionKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return $"user:{context.User.Identity.Name.Trim().ToLowerInvariant()}";
        }

        var clientHeader = context.Request.Headers["x-copilot-client"].FirstOrDefault()
                           ?? context.Request.Headers["x-client-name"].FirstOrDefault()
                           ?? context.Request.Headers["x-client-id"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(clientHeader))
        {
            return $"client:{clientHeader.Trim().ToLowerInvariant()}";
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            return $"ip:{remoteIp}";
        }

        return "anonymous";
    }
}
