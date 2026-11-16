namespace Martis.Domain.Parameters;

/// <summary>
/// Where a parameter value applies. This is not <c>Martis.Domain.Environment.EnvironmentScope</c>.
/// Cell scope still has laboratory and game cases (ADR-011). A parameter value does not:
/// the scenario strings accepted here are only <c>regional:&lt;site&gt;</c>,
/// <c>planetary_mean</c> and <c>analog:&lt;name&gt;</c>.
/// A planetary mean is not a regional measurement (TDD §10.3).
/// </summary>
public abstract record ParameterScope
{
    private ParameterScope()
    {
    }

    /// <summary>A planetary mean. It does not stand in for a regional measurement.</summary>
    public sealed record PlanetaryMean : ParameterScope;

    /// <summary>A named site. The site text is required.</summary>
    public sealed record Regional : ParameterScope
    {
        public Regional(string site)
        {
            if (string.IsNullOrWhiteSpace(site))
            {
                throw new ArgumentException("A regional parameter scope needs a site.", nameof(site));
            }

            Site = site;
        }

        public string Site { get; }
    }

    /// <summary>A named analog. The analog is not a site on Mars and not a cell scope.</summary>
    public sealed record Analog : ParameterScope
    {
        public Analog(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An analog parameter scope needs a name.", nameof(name));
            }

            Name = name;
        }

        public string Name { get; }
    }
}
