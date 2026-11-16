using Martis.Domain.Environment;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Column order shared by snapshots and run histories: the nine slots of
/// <see cref="ResourcePool.Quantities"/>, which <see cref="AllocationBuffer"/> repeats.
/// Writes go by name, so a reordered pool fails here instead of shifting values.
/// </summary>
public static class ResourceColumns
{
    public static IReadOnlyList<string> Names { get; } =
        new ResourcePool().Quantities.Select(quantity => quantity.Name).ToArray();

    public static void WriteDeposits(ResourcePool pool, IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(pool);
        RequireLength(values);
        for (var i = 0; i < Names.Count; i++)
        {
            var value = values[i];
            switch (Names[i])
            {
                case nameof(ResourcePool.CarbonDioxide): pool.CarbonDioxide = value; break;
                case nameof(ResourcePool.Water): pool.Water = value; break;
                case nameof(ResourcePool.Oxygen): pool.Oxygen = value; break;
                case nameof(ResourcePool.Nitrogen): pool.Nitrogen = value; break;
                case nameof(ResourcePool.Phosphorus): pool.Phosphorus = value; break;
                case nameof(ResourcePool.Sulfur): pool.Sulfur = value; break;
                case nameof(ResourcePool.Iron): pool.Iron = value; break;
                case nameof(ResourcePool.OrganicCarbon): pool.OrganicCarbon = value; break;
                case nameof(ResourcePool.MineralCarbon): pool.MineralCarbon = value; break;
                default: throw new InvalidOperationException($"Deposit '{Names[i]}' cannot be written.");
            }
        }
    }

    public static void WriteDemands(AllocationBuffer buffer, IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        RequireLength(values);
        var names = buffer.Quantities.Select(quantity => quantity.Name).ToArray();
        if (!names.SequenceEqual(Names, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("AllocationBuffer slots no longer follow ResourcePool.");
        }

        for (var i = 0; i < Names.Count; i++)
        {
            var value = values[i];
            switch (Names[i])
            {
                case nameof(AllocationBuffer.CarbonDioxide): buffer.CarbonDioxide = value; break;
                case nameof(AllocationBuffer.Water): buffer.Water = value; break;
                case nameof(AllocationBuffer.Oxygen): buffer.Oxygen = value; break;
                case nameof(AllocationBuffer.Nitrogen): buffer.Nitrogen = value; break;
                case nameof(AllocationBuffer.Phosphorus): buffer.Phosphorus = value; break;
                case nameof(AllocationBuffer.Sulfur): buffer.Sulfur = value; break;
                case nameof(AllocationBuffer.Iron): buffer.Iron = value; break;
                case nameof(AllocationBuffer.OrganicCarbon): buffer.OrganicCarbon = value; break;
                case nameof(AllocationBuffer.MineralCarbon): buffer.MineralCarbon = value; break;
                default: throw new InvalidOperationException($"Demand '{Names[i]}' cannot be written.");
            }
        }
    }

    private static void RequireLength(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != Names.Count)
        {
            throw new InvalidOperationException(
                $"Expected {Names.Count} resource values, found {values.Count}.");
        }
    }
}
