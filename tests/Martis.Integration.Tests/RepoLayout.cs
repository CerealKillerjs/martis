namespace Martis.Integration.Tests;

/// <summary>
/// Locates the repository from the test output directory.
/// The CLI smoke test depends on the default SDK output path.
/// </summary>
internal static class RepoLayout
{
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// Configuration directory name (<c>Debug</c> or <c>Release</c>) of this test run.
    /// </summary>
    public static string Configuration =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
        ?? throw new InvalidOperationException("Cannot detect the build configuration from the test output path.");

    /// <summary>
    /// Path of the built headless executable assembly.
    /// </summary>
    public static string FindCliDll()
    {
        var path = Path.Combine(Root, "src", "Martis.Cli", "bin", Configuration, "net10.0", "Martis.Cli.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Martis.Cli was not built for configuration '{Configuration}'. Build MARTIS.sln before running tests.",
                path);
        }

        return path;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MARTIS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("MARTIS.sln was not found above the test output directory.");
    }
}
