using System;
using System.IO;
using Godot;

namespace Martis.Game;

/// <summary>
/// Finds the repository folder that holds <c>data/</c> (scenarios, organisms, missions).
/// <c>MARTIS_DATA_ROOT</c> wins; otherwise the folders above the Godot project are searched.
/// </summary>
public static class DataRoot
{
    public static string Find()
    {
        var configured = System.Environment.GetEnvironmentVariable("MARTIS_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && IsRoot(configured))
        {
            return configured;
        }

        foreach (var start in new[] { ProjectSettings.GlobalizePath("res://"), OS.GetExecutablePath(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (IsRoot(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("No se encontró la carpeta data/ de MARTIS. Define MARTIS_DATA_ROOT.");
    }

    private static bool IsRoot(string path)
        => File.Exists(Path.Combine(path, "data", "missions", "first_colony.v1.json"))
            && Directory.Exists(Path.Combine(path, "data", "scenarios"));
}
