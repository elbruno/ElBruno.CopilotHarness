using System.Security.Cryptography;
using System.Text;

namespace ElBruno.CopilotHarness.Router.Api.Security;

public static class ApiKeyAuthenticationValidator
{
    public static bool IsValidBearerToken(string? authorizationHeader, string expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) || string.IsNullOrWhiteSpace(expectedApiKey))
        {
            return false;
        }

        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length != expectedApiKey.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expectedApiKey));
    }
}
