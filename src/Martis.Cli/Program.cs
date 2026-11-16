using System.Reflection;
using Martis.Persistence.SQLite;
using Martis.Simulation.Engine;

namespace Martis.Cli;

/// <summary>
/// Headless entry point of the MARTIS engine.
/// T01 answers <c>--version</c> and does not run a simulation.
/// </summary>
public static class Program
{
    /// <summary>
    /// Product version of the engine, without a source-revision suffix.
    /// The value comes from <c>Version</c> in <c>Directory.Build.props</c>.
    /// </summary>
    public static readonly string EngineVersion = ReadEngineVersion();

    /// <summary>
    /// Prints the engine version when the only argument is <c>--version</c>.
    /// </summary>
    /// <param name="args">Command-line arguments supplied by the host.</param>
    /// <returns><c>0</c> when the version was printed; <c>1</c> otherwise.</returns>
    public static int Main(string[] args)
    {
        // Keep the allowed project references in the assembly manifest.
        // The compiler drops an assembly reference that no instruction uses.
        _ = EngineAssembly.Name;
        _ = SqliteProbe.PersistenceAssemblyName;

        if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
        {
            Console.WriteLine($"MARTIS {EngineVersion}");
            return 0;
        }

        Console.Error.WriteLine("Usage: martis --version");
        return 1;
    }

    /// <summary>
    /// Reads the informational version and drops a <c>+</c> source-revision suffix.
    /// </summary>
    private static string ReadEngineVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
