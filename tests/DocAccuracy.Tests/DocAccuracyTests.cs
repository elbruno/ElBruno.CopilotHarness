using System.Text;
using Xunit;

namespace DocAccuracy.Tests;

public class DocAccuracyTests
{
    /// <summary>
    /// Walks up from the test assembly's directory until it finds the repo root
    /// (identified by ElBruno.CopilotHarness.slnx).
    /// </summary>
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("ElBruno.CopilotHarness.slnx").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (ElBruno.CopilotHarness.slnx not found).");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: StartCommand resolves proxies/ not samples/
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void StartCommand_References_ProxiesPath_Not_SamplesPath()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "tools", "CopilotHarness.Tool", "Commands", "StartCommand.cs");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("proxies"),
            $"StartCommand.cs should reference the 'proxies' path segment but does not. File: {filePath}");

        // The old literal path segment — not just the word "samples" in a comment
        // but the specific path string "samples/FoundryLocalProxy" or its Windows variant
        Assert.False(content.Contains("samples/FoundryLocalProxy"),
            $"StartCommand.cs still contains the deprecated path 'samples/FoundryLocalProxy'. File: {filePath}");
        Assert.False(content.Contains(@"samples\FoundryLocalProxy"),
            $"StartCommand.cs still contains the deprecated path 'samples\\FoundryLocalProxy'. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: No deprecated VS Code config keys in docs/
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Docs_DoNotContain_DeprecatedVsCodeConfigKeys()
    {
        var root = GetRepoRoot();
        var docsDir = Path.Combine(root, "docs");

        var deprecated = new[]
        {
            "github.copilot.chat.customModels",
            "github.copilot.advanced.chatModels"
        };

        var violations = new List<string>();

        foreach (var mdFile in Directory.EnumerateFiles(docsDir, "*.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(mdFile);
            foreach (var key in deprecated)
            {
                if (content.Contains(key))
                    violations.Add($"{Path.GetRelativePath(root, mdFile)} contains '{key}'");
            }
        }

        Assert.True(violations.Count == 0,
            "Deprecated VS Code config key(s) found in docs/:\n" + string.Join("\n", violations));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: No stale samples/FoundryLocalProxy path in docs, tools, or proxies
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void NoMarkdownFiles_Contain_StaleProxyPath()
    {
        var root = GetRepoRoot();
        var searchDirs = new[] { "docs", "tools", "proxies" };
        const string stale = "samples/FoundryLocalProxy";

        var violations = new List<string>();

        foreach (var dir in searchDirs)
        {
            var fullDir = Path.Combine(root, dir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var mdFile in Directory.EnumerateFiles(fullDir, "*.md", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(mdFile);
                if (content.Contains(stale))
                    violations.Add(Path.GetRelativePath(root, mdFile));
            }
        }

        Assert.True(violations.Count == 0,
            $"File(s) still reference the stale path '{stale}':\n" + string.Join("\n", violations));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: proxies/README.md covers all test app pages
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void ProxiesReadme_Covers_AllTestAppPages()
    {
        var root = GetRepoRoot();
        var readmePath = Path.Combine(root, "proxies", "README.md");

        Assert.True(File.Exists(readmePath), $"File not found: {readmePath}");

        var content = File.ReadAllText(readmePath);

        var requiredPages = new[] { "/chat", "/compare", "/models", "/history", "/setup" };
        var missing = requiredPages.Where(p => !content.Contains(p)).ToList();

        Assert.True(missing.Count == 0,
            $"proxies/README.md is missing coverage of these test app page(s): {string.Join(", ", missing)}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: vscode-settings.json template uses new format
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void VsCodeSettingsTemplate_UsesNewFormat()
    {
        var root = GetRepoRoot();
        var templatePath = Path.Combine(root, "tools", "CopilotHarness.Tool", "Templates", "vscode-settings.json");

        Assert.True(File.Exists(templatePath), $"File not found: {templatePath}");

        var content = File.ReadAllText(templatePath);

        Assert.True(content.Contains("\"vendor\""),
            $"vscode-settings.json template should contain '\"vendor\"' (new format) but does not. File: {templatePath}");

        Assert.False(content.Contains("\"github.copilot.chat.customModels\""),
            $"vscode-settings.json template still contains deprecated key '\"github.copilot.chat.customModels\"'. File: {templatePath}");
    }
}
