namespace Martis.Domain.Parameters;

/// <summary>Why <see cref="ParameterCatalog"/> refused to hand a value to a system.</summary>
public enum ParameterUnavailableReason
{
    /// <summary>The id is not in the catalog. The loader did not invent an entry.</summary>
    Missing,

    /// <summary>Status is PENDING. No v0 system may read it.</summary>
    Pending,

    /// <summary>Status is HISTORICAL. The entry stays in the catalog and is not delivered.</summary>
    Historical,

    /// <summary>Status is SUPERADO. The entry stays in the catalog and is not delivered.</summary>
    Superseded,

    /// <summary><c>used_in_v0</c> is false. The entry stays in the catalog and is not delivered.</summary>
    NotUsedInV0,

    /// <summary>The stored JSON kind is not the kind the caller asked for.</summary>
    WrongKind,
}

/// <summary>
/// The JSON document is not a usable parameter catalog.
/// <see cref="ParameterId"/> is set once the failing entry has an id.
/// </summary>
public sealed class ParameterLoadException : InvalidOperationException
{
    public ParameterLoadException(string message, string? parameterId = null)
        : base(message)
    {
        ParameterId = parameterId;
    }

    public string? ParameterId { get; }
}

/// <summary>
/// A system asked for a parameter that cannot be delivered.
/// The message always names <see cref="ParameterId"/>. No default is substituted.
/// </summary>
public sealed class ParameterUnavailableException : InvalidOperationException
{
    public ParameterUnavailableException(string parameterId, ParameterUnavailableReason reason)
        : base(Describe(parameterId, reason))
    {
        ParameterId = parameterId;
        Reason = reason;
    }

    public ParameterUnavailableException(string parameterId, ParameterUnavailableReason reason, string message)
        : base(message)
    {
        if (!message.Contains(parameterId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The message must name the parameter id.", nameof(message));
        }

        ParameterId = parameterId;
        Reason = reason;
    }

    public string ParameterId { get; }

    public ParameterUnavailableReason Reason { get; }

    private static string Describe(string parameterId, ParameterUnavailableReason reason)
    {
        return reason switch
        {
            ParameterUnavailableReason.Missing =>
                $"Parameter '{parameterId}' is not in the catalog. No default was invented.",
            ParameterUnavailableReason.Pending =>
                $"Parameter '{parameterId}' is PENDING. No v0 system may read it, and no default was invented.",
            ParameterUnavailableReason.Historical =>
                $"Parameter '{parameterId}' is HISTORICAL and is not delivered to a system.",
            ParameterUnavailableReason.Superseded =>
                $"Parameter '{parameterId}' is SUPERADO and is not delivered to a system.",
            ParameterUnavailableReason.NotUsedInV0 =>
                $"Parameter '{parameterId}' has used_in_v0 false and is not delivered to a system.",
            ParameterUnavailableReason.WrongKind =>
                $"Parameter '{parameterId}' does not hold the requested kind of value.",
            _ => $"Parameter '{parameterId}' cannot be read ({reason}).",
        };
    }
}
