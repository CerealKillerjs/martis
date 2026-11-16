using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Where an <see cref="EnvironmentState"/> applies.
/// A loaded scenario must set one of these on every cell. A world that has not
/// loaded a scenario leaves <see cref="EnvironmentState.Scope"/> null.
/// </summary>
public abstract record EnvironmentScope
{
    private EnvironmentScope()
    {
    }

    /// <summary>A planetary mean. It is not a regional measurement.</summary>
    public sealed record PlanetaryMean : EnvironmentScope;

    /// <summary>A named site. The site id is required and must contain text.</summary>
    public sealed record Regional : EnvironmentScope
    {
        public Regional(SiteId siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId.Value))
            {
                throw new ArgumentException("A regional scope needs a site id.", nameof(siteId));
            }

            SiteId = siteId;
        }

        public SiteId SiteId { get; }
    }

    /// <summary>A laboratory cell, not a place on Mars.</summary>
    public sealed record Lab : EnvironmentScope;

    /// <summary>A game cell. Its numbers are not a scientific measurement.</summary>
    public sealed record Game : EnvironmentScope;
}
