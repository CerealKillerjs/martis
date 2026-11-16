using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// Locks the SDK pin and the shared build settings required by T01.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class BuildConfigurationTests
{
    [Fact]
    public void GlobalJsonPinsTheNet10Sdk()
    {
        var path = Path.Combine(RepoLayout.Root, "global.json");
        var sdk = JsonNode.Parse(File.ReadAllText(path))!["sdk"]!.AsObject();

        Assert.Equal("10.0.404", sdk["version"]!.GetValue<string>());
        Assert.Equal("latestPatch", sdk["rollForward"]!.GetValue<string>());
        Assert.False(sdk["allowPrerelease"]!.GetValue<bool>());
    }

    [Fact]
    public void DirectoryBuildPropsSetsTheRequiredCompilerSwitches()
    {
        var document = XDocument.Load(Path.Combine(RepoLayout.Root, "Directory.Build.props"));
        var properties = document
            .Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.Ordinal);

        Assert.Equal("net10.0", properties["TargetFramework"]);
        Assert.Equal("enable", properties["Nullable"]);
        Assert.Equal("enable", properties["ImplicitUsings"]);
        Assert.Equal("latest", properties["LangVersion"]);
        Assert.Equal("true", properties["TreatWarningsAsErrors"]);
        Assert.Equal("true", properties["Deterministic"]);
        Assert.Equal("latest", properties["AnalysisLevel"]);
        Assert.Equal("true", properties["InvariantGlobalization"]);
        Assert.Equal("0.1.0", properties["Version"]);

        var continuous = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ContinuousIntegrationBuild")
            .ToArray();
        Assert.Contains(continuous, element => element.Value.Trim() == "true");
        Assert.Contains(
            continuous,
            element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains("CI", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageVersionsAreExact()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BenchmarkDotNet"] = "0.15.8",
            ["coverlet.collector"] = "10.1.1",
            ["CsCheck"] = "5.0.1",
            ["Microsoft.Data.Sqlite"] = "11.0.0",
            ["Microsoft.NET.Test.Sdk"] = "18.12.0",
            ["xunit"] = "2.9.3",
            ["xunit.runner.visualstudio"] = "4.0.1",
        };

        var document = XDocument.Load(Path.Combine(RepoLayout.Root, "Directory.Packages.props"));
        var central = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ManagePackageVersionsCentrally");
        Assert.Equal("true", central.Value.Trim());

        var actual = document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        AssertSameMap(expected, actual);
        foreach (var version in actual.Values)
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        }
    }

    [Fact]
    public void CiRestoreIsLockedAndNuGetCacheHasALockFileKey()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoLayout.Root, ".github", "workflows", "ci.yml"));
        Assert.Contains("cache: true", workflow, StringComparison.Ordinal);
        Assert.Contains("**/packages.lock.json", workflow, StringComparison.Ordinal);
        Assert.Contains("--locked-mode", workflow, StringComparison.Ordinal);
        Assert.Contains("actions: write", workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --exit-code", workflow, StringComparison.Ordinal);
        Assert.Contains("git status --porcelain", workflow, StringComparison.Ordinal);

        var props = XDocument.Load(Path.Combine(RepoLayout.Root, "Directory.Build.props"));
        var lockFileSwitch = props
            .Descendants()
            .Single(element => element.Name.LocalName == "RestorePackagesWithLockFile");
        Assert.Equal("true", lockFileSwitch.Value.Trim());
        var fallback = props
            .Descendants()
            .Single(element => element.Name.LocalName == "DisableImplicitNuGetFallbackFolder");
        Assert.Equal("true", fallback.Value.Trim());

        var locked = props
            .Descendants()
            .Where(element => element.Name.LocalName == "RestoreLockedMode")
            .ToArray();
        Assert.Contains(locked, element => element.Value.Trim() == "true");
        Assert.Contains(
            locked,
            element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains("CI", StringComparison.Ordinal));

        var projects = Directory
            .EnumerateFiles(RepoLayout.Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToArray();
        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var lockPath = Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json");
            Assert.True(File.Exists(lockPath), $"Missing lock file next to {Path.GetFileName(project)}.");
            // SDK 10.0.404 recreates a missing lock during `dotnet restore --locked-mode`,
            // so presence on disk is not evidence that the file was committed.
            RequireLockTracked(RepoLayout.Root, lockPath);
            var json = JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject();
            Assert.True(json.ContainsKey("version"));
            Assert.True(json.ContainsKey("dependencies"));
        }

        var persistenceLock = File.ReadAllText(
            Path.Combine(RepoLayout.Root, "src", "Martis.Persistence", "packages.lock.json"));
        Assert.Contains("Microsoft.Data.Sqlite", persistenceLock, StringComparison.Ordinal);
        Assert.Contains("11.0.0", persistenceLock, StringComparison.Ordinal);
    }

    [Fact]
    public void UntrackedLockFileIsRejectedUntilItIsIndexed()
    {
        var root = Directory.CreateTempSubdirectory("martis-lock-").FullName;
        try
        {
            Git(root, "init", "-q");
            var projectDirectory = Path.Combine(root, "src", "Sample");
            Directory.CreateDirectory(projectDirectory);
            var project = Path.Combine(projectDirectory, "Sample.csproj");
            var lockPath = Path.Combine(projectDirectory, "packages.lock.json");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(lockPath, """{ "version": 1, "dependencies": {} }""");

            var missing = Assert.Throws<InvalidOperationException>(() => RequireLockTracked(root, lockPath));
            Assert.Contains("is not tracked", missing.Message, StringComparison.Ordinal);
            Assert.Contains("packages.lock.json", missing.Message, StringComparison.Ordinal);

            Git(root, "add", "--", Path.GetRelativePath(root, lockPath).Replace('\\', '/'));
            RequireLockTracked(root, lockPath);
        }
        finally
        {
            DeleteWorkTree(root);
        }
    }

    [Fact]
    public void DirectoryThatIsNotAGitRepositoryFailsClearly()
    {
        var root = Directory.CreateTempSubdirectory("martis-not-git-").FullName;
        try
        {
            var project = Path.Combine(root, "Sample.csproj");
            var lockPath = Path.Combine(root, "packages.lock.json");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(lockPath, """{ "version": 1, "dependencies": {} }""");

            var error = Assert.Throws<InvalidOperationException>(() => RequireLockTracked(root, lockPath));
            Assert.Contains("not a git repository", error.Message, StringComparison.Ordinal);
            Assert.Contains(root, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteWorkTree(root);
        }
    }

    private static void DeleteWorkTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }

    private static void RequireLockTracked(string repositoryRoot, string lockPath)
    {
        var relative = Path.GetRelativePath(repositoryRoot, lockPath).Replace('\\', '/');
        var (code, stdout, stderr) = Git(repositoryRoot, "ls-files", "--error-unmatch", "--", relative);
        if (code == 0)
        {
            return;
        }

        var detail = string.Join(
            " ",
            new[] { stderr, stdout }.Where(static text => !string.IsNullOrWhiteSpace(text))).Trim();
        if (code == 128)
        {
            throw new InvalidOperationException(
                $"Cannot verify tracked lock files because '{repositoryRoot}' is not a git repository. {detail}".Trim());
        }

        throw new InvalidOperationException(
            $"'{relative}' is not tracked in the git index at '{repositoryRoot}'. git ls-files --error-unmatch exited {code}. {detail}".Trim());
    }

    private static (int ExitCode, string Stdout, string Stderr) Git(string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = StartGit(start);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("git did not exit while checking packages.lock.json.");
        }

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static Process StartGit(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start)
                ?? throw new InvalidOperationException("Could not start git to verify packages.lock.json.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new InvalidOperationException("git is required to verify packages.lock.json is tracked.", error);
        }
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || parts.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertSameMap(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        var expectedText = string.Join(", ", expected.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"));
        var actualText = string.Join(", ", actual.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"));
        Assert.Equal(expectedText, actualText);
    }
}
