using Xunit;

namespace ElBruno.CopilotHarness.Proxies.AppHost.Tests;

public sealed class AppHostWiringTests
{
    [Fact]
    public void ProxiesAppHost_ReferencesAnalyticsWebProject()
    {
        var projectType = typeof(Projects.ElBruno_CopilotHarness_Analytics_Web);

        Assert.Equal("ElBruno_CopilotHarness_Analytics_Web", projectType.Name);
    }
}
