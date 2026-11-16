using Martis.Domain.Common;
using Martis.Domain.Environment;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Stable text for an <see cref="EnvironmentScope"/>. The regional form is
/// <c>regional:</c> plus the site, which is how <c>metadata.scope_id</c> is written.
/// </summary>
internal static class ScopeText
{
    public static string? Format(EnvironmentScope? scope)
    {
        return scope switch
        {
            null => null,
            EnvironmentScope.PlanetaryMean => "planetary_mean",
            EnvironmentScope.Regional regional => "regional:" + regional.SiteId.Value,
            EnvironmentScope.Lab => "lab",
            EnvironmentScope.Game => "game",
            _ => throw new InvalidOperationException("The environment scope has no snapshot text."),
        };
    }

    public static EnvironmentScope? Parse(string? text)
    {
        if (text is null || string.Equals(text, "unset", StringComparison.Ordinal))
        {
            return null;
        }

        const string prefix = "regional:";
        if (string.Equals(text, "planetary_mean", StringComparison.Ordinal))
        {
            return new EnvironmentScope.PlanetaryMean();
        }

        if (string.Equals(text, "lab", StringComparison.Ordinal))
        {
            return new EnvironmentScope.Lab();
        }

        if (string.Equals(text, "game", StringComparison.Ordinal))
        {
            return new EnvironmentScope.Game();
        }

        if (text.StartsWith(prefix, StringComparison.Ordinal) && text.Length > prefix.Length)
        {
            return new EnvironmentScope.Regional(new SiteId(text[prefix.Length..]));
        }

        throw new InvalidOperationException($"The snapshot scope '{text}' is not recognised.");
    }
}
