using Microsoft.AspNetCore.Authentication;

namespace ElBruno.CopilotHarness.Router.Api.Security;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "HarnessAdminBearer";

    public string ExpectedApiKey { get; set; } = string.Empty;
}
