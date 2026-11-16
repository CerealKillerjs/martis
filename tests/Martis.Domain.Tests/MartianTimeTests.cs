using Martis.Domain.Common;

namespace Martis.Domain.Tests;

public sealed class MartianTimeTests
{
    [Fact]
    public void OneSolIsTheSingleCopyOfTheSecondCount()
    {
        Assert.Equal(88775, MartianTime.SecondsPerSol);

        var root = RepoRoot.Find();
        var files = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("88775", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["src/Martis.Domain/Common/MartianTime.cs"], files);
    }
}
