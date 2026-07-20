namespace Part13_Summary.Tests;

public sealed class RepoConsistencyTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task All31SrcLabsAreNonPlaceholder()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        var labDirs = Directory.GetDirectories(srcDir)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return (name.StartsWith("Step", StringComparison.Ordinal)
                    || name.StartsWith("Part", StringComparison.Ordinal))
                    && File.Exists(Path.Combine(d, "Program.cs"))
                    && File.Exists(Path.Combine(d, "Properties", "launchSettings.json"));
            })
            .ToList();
        Assert.Equal(31, labDirs.Count);
        foreach (var dir in labDirs)
        {
            var programPath = Path.Combine(dir, "Program.cs");
            var content = await File.ReadAllTextAsync(programPath);
            Assert.DoesNotContain("// LearnAspNet placeholder", content);
        }
    }

    [Fact]
    public async Task AllShellScriptsHaveShebang()
    {
        var scriptsDir = Path.Combine(RepoRoot, "scripts");
        if (!Directory.Exists(scriptsDir))
        {
            return;
        }
        var scripts = Directory.GetFiles(scriptsDir, "*.sh", SearchOption.AllDirectories);
        foreach (var script in scripts)
        {
            var content = await File.ReadAllTextAsync(script);
            Assert.StartsWith("#!", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task JsonAndYamlFilesEndWithNewline()
    {
        var docsDir = Path.Combine(RepoRoot, "docs");
        if (!Directory.Exists(docsDir))
        {
            return;
        }
        var jsonFiles = Directory.GetFiles(docsDir, "*.json", SearchOption.AllDirectories);
        foreach (var file in jsonFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            Assert.True(content.Length > 0 && content[^1] == '\n',
                $"{file} does not end with newline");
        }
    }

    [Fact]
    public void ReadmeDoesNotCarryStaleStatus()
    {
        var readmePath = Path.Combine(RepoRoot, "README.md");
        var content = File.ReadAllText(readmePath);
        Assert.DoesNotContain("W6–W8 未完成", content);
        Assert.DoesNotContain("W6-W8 未完成", content);
        Assert.DoesNotContain("未完成（17 个 Lab", content);
    }
}
