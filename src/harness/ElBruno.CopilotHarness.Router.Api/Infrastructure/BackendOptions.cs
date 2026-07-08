using System.ComponentModel.DataAnnotations;

namespace ElBruno.CopilotHarness.Router.Api.Infrastructure;

public sealed class BackendOptions
{
    public const string SectionName = "Backend";

    public AuthOptions Auth { get; init; } = new();

    public RateLimitingOptions RateLimiting { get; init; } = new();
}

public sealed class AuthOptions
{
    public string? AdminApiKey { get; init; }
}

public sealed class RateLimitingOptions
{
    public bool Enabled { get; init; } = true;

    [Range(1, 10_000)]
    public int PermitLimit { get; init; } = 200;

    [Range(1, 3_600)]
    public int WindowSeconds { get; init; } = 60;
}
