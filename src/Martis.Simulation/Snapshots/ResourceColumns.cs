using Martis.Domain.Environment;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Column order shared by snapshots and run histories: the nine slots of
/// <see cref="ResourcePool.Quantities"/>, which <see cref="AllocationBuffer"/> repeats.
/// Writes go by name, so a reordered pool fails here instead of shifting values.
/// </summary>
public static class ResourceColumns
{
    public static IReadOnlyList<string> Names { get; } = CreateNames();

    private static string[] CreateNames()
    {
        var names = new ResourcePool().Quantities.Select(quantity => quantity.Name).ToArray();
        string[] expected =
        [
            nameof(ResourcePool.CarbonDioxide), nameof(ResourcePool.Water), nameof(ResourcePool.Oxygen),
            nameof(ResourcePool.Nitrogen), nameof(ResourcePool.Phosphorus), nameof(ResourcePool.Sulfur),
            nameof(ResourcePool.Iron), nameof(ResourcePool.OrganicCarbon), nameof(ResourcePool.MineralCarbon),
        ];
        if (!names.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("ResourcePool slots changed order; update ResourceColumns.");
        }

        return names;
    }

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

    /// <summary>Slot index of a pool name. Throws for an unknown name.</summary>
    public static int Slot(string name)
    {
        for (var i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException($"'{name}' is not a resource slot.", nameof(name));
    }

    public static string Name(int slot) => Names[slot];

    /// <summary>Adds <paramref name="kg"/> (may be negative) to one deposit.</summary>
    public static void Add(ResourcePool pool, int slot, double kg)
    {
        ArgumentNullException.ThrowIfNull(pool);
        switch (slot)
        {
            case 0: pool.CarbonDioxide += kg; break;
            case 1: pool.Water += kg; break;
            case 2: pool.Oxygen += kg; break;
            case 3: pool.Nitrogen += kg; break;
            case 4: pool.Phosphorus += kg; break;
            case 5: pool.Sulfur += kg; break;
            case 6: pool.Iron += kg; break;
            case 7: pool.OrganicCarbon += kg; break;
            case 8: pool.MineralCarbon += kg; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    /// <summary>Adds a request to one slot of an allocation buffer.</summary>
    public static void Add(AllocationBuffer buffer, int slot, double kg)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        switch (slot)
        {
            case 0: buffer.CarbonDioxide += kg; break;
            case 1: buffer.Water += kg; break;
            case 2: buffer.Oxygen += kg; break;
            case 3: buffer.Nitrogen += kg; break;
            case 4: buffer.Phosphorus += kg; break;
            case 5: buffer.Sulfur += kg; break;
            case 6: buffer.Iron += kg; break;
            case 7: buffer.OrganicCarbon += kg; break;
            case 8: buffer.MineralCarbon += kg; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    public static void Clear(AllocationBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.CarbonDioxide = 0;
        buffer.Water = 0;
        buffer.Oxygen = 0;
        buffer.Nitrogen = 0;
        buffer.Phosphorus = 0;
        buffer.Sulfur = 0;
        buffer.Iron = 0;
        buffer.OrganicCarbon = 0;
        buffer.MineralCarbon = 0;
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
