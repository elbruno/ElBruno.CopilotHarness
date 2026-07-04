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
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Commands", "StartCommand.cs");

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
        var searchDirs = new[] { "docs", Path.Combine("src", "tools"), Path.Combine("src", "proxies") };
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
        var readmePath = Path.Combine(root, "src", "proxies", "README.md");

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
        var templatePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Templates", "vscode-settings.json");

        Assert.True(File.Exists(templatePath), $"File not found: {templatePath}");

        var content = File.ReadAllText(templatePath);

        Assert.True(content.Contains("\"vendor\""),
            $"vscode-settings.json template should contain '\"vendor\"' (new format) but does not. File: {templatePath}");

        Assert.False(content.Contains("\"github.copilot.chat.customModels\""),
            $"vscode-settings.json template still contains deprecated key '\"github.copilot.chat.customModels\"'. File: {templatePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: README documents the Smart Agents / Option 2 section
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Readme_Contains_AgentsPatternSection()
    {
        var root = GetRepoRoot();
        var readmePath = Path.Combine(root, "README.md");

        Assert.True(File.Exists(readmePath), $"File not found: {readmePath}");

        var content = File.ReadAllText(readmePath);

        Assert.True(content.Contains("@harness-general"),
            "README.md should contain '@harness-general' (agents section) but does not.");

        Assert.True(content.Contains("phi-4-mini"),
            "README.md should contain 'phi-4-mini' in the context of the agents section but does not.");

        Assert.True(content.Contains("Option 2") || content.Contains("Smart Agents"),
            "README.md should contain 'Option 2' or 'Smart Agents' (section heading) but does not.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: README documents both the BYOK and agents entry points
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Readme_Contains_Both_BYOK_And_Agents_EntryPoints()
    {
        var root = GetRepoRoot();
        var readmePath = Path.Combine(root, "README.md");

        Assert.True(File.Exists(readmePath), $"File not found: {readmePath}");

        var content = File.ReadAllText(readmePath);

        Assert.True(content.Contains("BYOK") || content.Contains("Fast start"),
            "README.md should contain 'BYOK' or 'Fast start' (Option 1 entry point) but does not.");

        Assert.True(content.Contains("harness-general"),
            "README.md should contain 'harness-general' (Option 2 / agents entry point) but does not.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: No markdown file links to the old samples/ path
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void NoMarkdownFile_Links_To_Samples_Path()
    {
        var root = GetRepoRoot();

        var violations = new List<string>();

        // Collect all .md files from docs/, proxies/, tools/ and root README.md
        var searchDirs = new[] { "docs", Path.Combine("src", "proxies"), Path.Combine("src", "tools") };
        var mdFiles = searchDirs
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.md", SearchOption.AllDirectories))
            .ToList();

        var rootReadme = Path.Combine(root, "README.md");
        if (File.Exists(rootReadme))
            mdFiles.Add(rootReadme);

        var badPatterns = new[] { "(../samples/", "(samples/", "samples/FoundryLocalProxy" };

        foreach (var mdFile in mdFiles)
        {
            var content = File.ReadAllText(mdFile);
            foreach (var pattern in badPatterns)
            {
                if (content.Contains(pattern))
                {
                    violations.Add($"{Path.GetRelativePath(root, mdFile)} contains '{pattern}'");
                    break;
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Markdown file(s) contain references to the old samples/ path:\n" + string.Join("\n", violations));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: All docs/ relative links in README.md exist on disk
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AllDocLinks_In_README_Exist_On_Disk()
    {
        var root = GetRepoRoot();
        var readmePath = Path.Combine(root, "README.md");

        Assert.True(File.Exists(readmePath), $"File not found: {readmePath}");

        var content = File.ReadAllText(readmePath);

        // Extract all [text](path) links where path starts with docs/
        var missing = new List<string>();
        var regex = new System.Text.RegularExpressions.Regex(@"\]\((docs/[^)#\s]+)");
        foreach (System.Text.RegularExpressions.Match match in regex.Matches(content))
        {
            var relativePath = match.Groups[1].Value;
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                missing.Add(relativePath);
        }

        Assert.True(missing.Count == 0,
            "README.md links to docs/ file(s) that do not exist on disk:\n" + string.Join("\n", missing));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: Presentation has at least three slides
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Presentation_HasAtLeastThreeSlides()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "presentation", "harness-layers.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        // Both class="slide" and class="slide active" contain this substring
        var count = CountOccurrences(content, "class=\"slide\"");

        Assert.True(count >= 3,
            $"Expected at least 3 slides in harness-layers.html but found {count}.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 11: Presentation mentions harness-general
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Presentation_MentionsHarnessGeneral()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "presentation", "harness-layers.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("harness-general"),
            $"harness-layers.html should mention 'harness-general' but does not. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 12: Presentation mentions FoundryLocalProxy
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Presentation_MentionsFoundryLocalProxy()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "presentation", "harness-layers.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("FoundryLocalProxy"),
            $"harness-layers.html should mention 'FoundryLocalProxy' but does not. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 13: Presentation has no unclosed <div> tags
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Presentation_HasNoUnclosedDivTags()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "presentation", "harness-layers.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        var openCount  = CountOccurrences(content, "<div");
        var closeCount = CountOccurrences(content, "</div>");

        Assert.True(openCount == closeCount,
            $"harness-layers.html has unbalanced div tags: {openCount} opening <div> vs {closeCount} closing </div>.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase A — Tool DX
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tool_Has_DoctorCommand()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Commands", "DoctorCommand.cs");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("doctor", StringComparison.OrdinalIgnoreCase) || content.Contains("Doctor"),
            $"DoctorCommand.cs should contain 'doctor' or 'Doctor' but does not. File: {filePath}");
    }

    [Fact]
    public void Tool_Has_UpdateCommand()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Commands", "UpdateCommand.cs");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("Update") || content.Contains("update"),
            $"UpdateCommand.cs should contain 'Update' or 'update' but does not. File: {filePath}");
    }

    [Fact]
    public void Tool_README_Mentions_Doctor_And_Update()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "README.md");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("harness doctor"),
            $"Tool README.md should contain 'harness doctor' but does not. File: {filePath}");

        Assert.True(content.Contains("harness update"),
            $"Tool README.md should contain 'harness update' but does not. File: {filePath}");
    }

    [Fact]
    public void Tool_InitCommand_Detects_VSCode_UserDir()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Commands", "InitCommand.cs");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("Code"),
            $"InitCommand.cs should contain 'Code' (VS Code path detection) but does not. File: {filePath}");

        Assert.True(content.Contains("chatLanguageModels.json"),
            $"InitCommand.cs should contain 'chatLanguageModels.json' but does not. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase B — Agent templates
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllFourNewAgentTemplates_ExistIn_GithubAgents()
    {
        var root = GetRepoRoot();
        var agentsDir = Path.Combine(root, ".github", "agents");

        var expected = new[]
        {
            "harness-db.agent.md",
            "harness-test.agent.md",
            "harness-docs.agent.md",
            "harness-deploy.agent.md"
        };

        var missing = expected.Where(f => !File.Exists(Path.Combine(agentsDir, f))).ToList();

        Assert.True(missing.Count == 0,
            $"Missing agent file(s) in .github/agents/:\n{string.Join("\n", missing)}");
    }

    [Fact]
    public void AllFourNewAgentTemplates_ExistIn_ToolTemplates()
    {
        var root = GetRepoRoot();
        var templatesDir = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Templates", "agents");

        var expected = new[]
        {
            "harness-db.agent.md",
            "harness-test.agent.md",
            "harness-docs.agent.md",
            "harness-deploy.agent.md"
        };

        var missing = expected.Where(f => !File.Exists(Path.Combine(templatesDir, f))).ToList();

        Assert.True(missing.Count == 0,
            $"Missing agent template(s) in src/tools/CopilotHarness.Tool/Templates/agents/:\n{string.Join("\n", missing)}");
    }

    [Fact]
    public void HarnessGeneral_RoutesTo_AllFourNewAgents()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, ".github", "agents", "harness-general.agent.md");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("harness-db"),
            $"harness-general.agent.md should contain 'harness-db' but does not. File: {filePath}");

        Assert.True(content.Contains("harness-test"),
            $"harness-general.agent.md should contain 'harness-test' but does not. File: {filePath}");

        Assert.True(content.Contains("harness-docs"),
            $"harness-general.agent.md should contain 'harness-docs' but does not. File: {filePath}");

        Assert.True(content.Contains("harness-deploy"),
            $"harness-general.agent.md should contain 'harness-deploy' but does not. File: {filePath}");
    }

    [Fact]
    public void AllNewAgents_HavePhiFourMiniModel()
    {
        var root = GetRepoRoot();
        var agentsDir = Path.Combine(root, ".github", "agents");

        var agentFiles = new[]
        {
            "harness-db.agent.md",
            "harness-test.agent.md",
            "harness-docs.agent.md",
            "harness-deploy.agent.md"
        };

        var violations = new List<string>();
        foreach (var file in agentFiles)
        {
            var fullPath = Path.Combine(agentsDir, file);
            if (!File.Exists(fullPath))
            {
                violations.Add($"{file}: file not found");
                continue;
            }
            var content = File.ReadAllText(fullPath);
            if (!content.Contains("phi-4-mini"))
                violations.Add($"{file}: missing 'phi-4-mini'");
        }

        Assert.True(violations.Count == 0,
            $"Agent file(s) missing 'phi-4-mini' model:\n{string.Join("\n", violations)}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase C — Presentation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Presentation_HasSpeakerNotes()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "presentation", "harness-layers.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("notes"),
            $"harness-layers.html should contain 'notes' (speaker notes class/attribute) but does not. File: {filePath}");

        Assert.True(content.Contains("data-notes"),
            $"harness-layers.html should contain 'data-notes' attribute but does not. File: {filePath}");
    }

    [Fact]
    public void Presentation_HasClipboardFunctionality()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "presentation", "harness-layers.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("clipboard") || content.Contains("copy-btn"),
            $"harness-layers.html should contain 'clipboard' or 'copy-btn' but does not. File: {filePath}");
    }

    [Fact]
    public void Index_Links_To_Presentation()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "index.html");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("harness-layers.html"),
            $"docs/index.html should link to 'harness-layers.html' but does not. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase D — Aspire detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InitCommand_HasAspireAppHostDetection()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "Commands", "InitCommand.cs");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("AppHost"),
            $"InitCommand.cs should contain 'AppHost' (Aspire detection) but does not. File: {filePath}");

        Assert.True(content.Contains("FindAspireAppHost") || content.Contains("appHost"),
            $"InitCommand.cs should contain 'FindAspireAppHost' or 'appHost' but does not. File: {filePath}");
    }

    [Fact]
    public void ToolReadme_MentionsAspireDetection()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "tools", "CopilotHarness.Tool", "README.md");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        Assert.True(content.Contains("AppHost") || content.Contains("Aspire"),
            $"Tool README.md should contain 'AppHost' or 'Aspire' (Aspire detection note) but does not. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Proxies.Common — shared library exists and is wired correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProxiesCommon_ProjectFile_Exists()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "proxies", "Proxies.Common", "Proxies.Common.csproj");

        Assert.True(File.Exists(filePath),
            $"Proxies.Common project file not found — was it deleted or moved? Expected: {filePath}");
    }

    [Fact]
    public void ProxiesCommon_CopilotMessageExtractor_Exists()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "proxies", "Proxies.Common", "CopilotMessageExtractor.cs");

        Assert.True(File.Exists(filePath),
            $"CopilotMessageExtractor.cs not found in Proxies.Common. Expected: {filePath}");
    }

    [Fact]
    public void ProxiesCommon_CopilotMessageExtractor_HasCorrectNamespace()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "src", "proxies", "Proxies.Common", "CopilotMessageExtractor.cs");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);
        Assert.True(content.Contains("namespace Proxies.Common"),
            $"CopilotMessageExtractor.cs should declare 'namespace Proxies.Common'. File: {filePath}");
    }

    [Fact]
    public void DuplicateCopilotMessageExtractor_DoesNotExist_InProxyProjects()
    {
        var root = GetRepoRoot();
        var proxyDirs = new[]
        {
            Path.Combine(root, "src", "proxies", "OllamaProxy"),
            Path.Combine(root, "src", "proxies", "FoundryProxy"),
            Path.Combine(root, "src", "proxies", "FoundryLocalProxy"),
        };

        var violations = proxyDirs
            .Select(dir => Path.Combine(dir, "CopilotMessageExtractor.cs"))
            .Where(File.Exists)
            .ToList();

        Assert.True(violations.Count == 0,
            $"CopilotMessageExtractor.cs should only exist in Proxies.Common, but was found in:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void AllThreeProxyCsproj_Reference_ProxiesCommon()
    {
        var root = GetRepoRoot();
        var proxyProjects = new[]
        {
            Path.Combine(root, "src", "proxies", "OllamaProxy",       "OllamaProxy.csproj"),
            Path.Combine(root, "src", "proxies", "FoundryProxy",      "FoundryProxy.csproj"),
            Path.Combine(root, "src", "proxies", "FoundryLocalProxy", "FoundryLocalProxy.csproj"),
        };

        var missing = proxyProjects
            .Where(p => !File.ReadAllText(p).Contains("Proxies.Common"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(missing.Count == 0,
            $"These proxy .csproj files do not reference Proxies.Common:\n{string.Join("\n", missing)}");
    }

    [Fact]
    public void ProxiesSolution_Includes_ProxiesCommon()
    {
        var root = GetRepoRoot();
        var slnx = Path.Combine(root, "src", "proxies", "Proxies.slnx");

        Assert.True(File.Exists(slnx), $"Proxies.slnx not found: {slnx}");

        var content = File.ReadAllText(slnx);
        Assert.True(content.Contains("Proxies.Common"),
            $"Proxies.slnx should include Proxies.Common project but does not. File: {slnx}");
    }

    [Fact]
    public void BlogPost_01_Exists_WithImages()
    {
        var root = GetRepoRoot();
        var blogPost = Path.Combine(root, "docs", "blog", "01-CopilotHarness.md");
        var imagesDir = Path.Combine(root, "docs", "blog", "01-CopilotHarness-images");

        Assert.True(File.Exists(blogPost),
            $"Blog post 01-CopilotHarness.md not found. Expected: {blogPost}");

        Assert.True(Directory.Exists(imagesDir),
            $"Blog images folder not found. Expected: {imagesDir}");

        var images = Directory.GetFiles(imagesDir);
        Assert.True(images.Length > 0,
            $"Blog images folder exists but is empty: {imagesDir}");
    }

    [Fact]
    public void BlogPost_01_MentionsProxiesCommon()
    {
        var root = GetRepoRoot();
        var filePath = Path.Combine(root, "docs", "blog", "01-CopilotHarness.md");

        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);
        Assert.True(content.Contains("Proxies.Common") || content.Contains("CopilotMessageExtractor"),
            $"Blog post should mention Proxies.Common or CopilotMessageExtractor. File: {filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────
    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
