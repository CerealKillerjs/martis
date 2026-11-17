using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Martis.Contracts.Commands;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Engine;

/// <summary>
/// R7-06. Every public stateful property of the environment, the resource pool
/// and a population has to be copied and restored. A new property fails this
/// test until the checkpoint names it and the round trip brings the old value back.
/// The cell loop stays one copy per cell. Sharing the 4096 copies is T10.
/// </summary>
public sealed class StateCheckpointTests
{
    /// <summary>
    /// Derived projections. They are not stored state, so the checkpoint does not copy them.
    /// </summary>
    private static readonly HashSet<string> Projections = new(StringComparer.Ordinal)
    {
        nameof(EnvironmentState.Quantities),
        nameof(ResourcePool.Quantities),
    };

    [Fact]
    public void CheckpointCopiesAndRestoresEveryStatefulProperty()
    {
        var environmentProperties = StatefulProperties(typeof(EnvironmentState));
        var resourceProperties = StatefulProperties(typeof(ResourcePool));
        var populationProperties = StatefulProperties(typeof(Population));
        AssertCheckpointMentions(environmentProperties, resourceProperties, populationProperties);

        var state = new SimulationState(2, 2);
        state.Tick = 4;
        for (var index = 0; index < state.CellCount; index++)
        {
            var leaveUnset = index == state.CellCount - 1;
            Plant(state.Environment[index], environmentProperties, seed: index + 1, leaveAssignedDoublesUnset: leaveUnset);
            Plant(state.Resources[index], resourceProperties, seed: index + 11, leaveAssignedDoublesUnset: false);
        }

        var population = new Population(
            new PopulationId(4),
            new OrganismId("organism-a"),
            0,
            GenomeFor(3));
        Plant(population, populationProperties, seed: 3, leaveAssignedDoublesUnset: false);
        state.Populations.Add(population);

        var random = RngStreams.Create(12345UL);
        var commands = new Queue<ISimulationCommand>();
        var before = Describe(state, environmentProperties, resourceProperties, populationProperties);
        var checkpoint = StateCheckpoint.Capture(state, random, commands);

        state.Tick = 99;
        for (var index = 0; index < state.CellCount; index++)
        {
            Plant(state.Environment[index], environmentProperties, seed: index + 40, leaveAssignedDoublesUnset: false);
            Plant(state.Resources[index], resourceProperties, seed: index + 70, leaveAssignedDoublesUnset: false);
        }

        Plant(population, populationProperties, seed: 80, leaveAssignedDoublesUnset: false);
        state.Populations.Add(new Population(new PopulationId(99), new OrganismId("extra"), 1, GenomeFor(90)));

        var mutated = Describe(state, environmentProperties, resourceProperties, populationProperties);
        Assert.NotEqual(before, mutated);

        checkpoint.Restore(state, random, commands);

        var restored = Describe(state, environmentProperties, resourceProperties, populationProperties);
        Assert.Equal(before, restored);
    }

    private static void AssertCheckpointMentions(
        IReadOnlyList<PropertyInfo> environment,
        IReadOnlyList<PropertyInfo> resources,
        IReadOnlyList<PropertyInfo> population)
    {
        var source = File.ReadAllText(Path.Combine(
            Repo.Root(),
            "src",
            "Martis.Simulation",
            "Engine",
            "StateCheckpoint.cs"));
        foreach (var property in environment.Concat(resources).Concat(population))
        {
            Assert.True(
                Regex.IsMatch(source, @"\." + Regex.Escape(property.Name) + @"\b"),
                $"StateCheckpoint does not mention {property.DeclaringType!.Name}.{property.Name}.");
        }
    }

    private static List<PropertyInfo> StatefulProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => !Projections.Contains(property.Name))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static void Plant(object target, IReadOnlyList<PropertyInfo> properties, int seed, bool leaveAssignedDoublesUnset)
    {
        var index = 0;
        foreach (var property in properties)
        {
            if (!property.CanWrite)
            {
                continue;
            }

            object value = property.PropertyType == typeof(AssignedDouble) && leaveAssignedDoublesUnset
                ? AssignedDouble.Unset
                : ValueFor(property, seed, index);
            property.SetValue(target, value);
            index++;
        }
    }

    private static object ValueFor(PropertyInfo property, int seed, int index)
    {
        var type = property.PropertyType;
        if (type == typeof(double))
        {
            return Number(seed, index);
        }

        if (type == typeof(AssignedDouble))
        {
            return AssignedDouble.Set(Number(seed, index));
        }

        if (type == typeof(int))
        {
            return seed * 10 + index;
        }

        if (type == typeof(EnvironmentScope))
        {
            return seed % 2 == 0
                ? new EnvironmentScope.Lab()
                : new EnvironmentScope.Regional(new SiteId("site-" + seed.ToString(CultureInfo.InvariantCulture)));
        }

        if (type == typeof(Genome))
        {
            return GenomeFor(seed);
        }

        throw new InvalidOperationException(
            $"No planted value for {property.DeclaringType?.Name}.{property.Name} ({type.Name}). A new stateful property has to be copied.");
    }

    private static double Number(int seed, int index) => ((seed + 1) * 0.125) + (index * 0.01);

    private static Genome GenomeFor(int seed)
    {
        return new Genome(new Dictionary<GeneId, double>
        {
            [new GeneId("g" + seed.ToString(CultureInfo.InvariantCulture))] = 0.25,
        });
    }

    private static string Describe(
        SimulationState state,
        IReadOnlyList<PropertyInfo> environmentProperties,
        IReadOnlyList<PropertyInfo> resourceProperties,
        IReadOnlyList<PropertyInfo> populationProperties)
    {
        var builder = new StringBuilder();
        builder.Append("tick:").Append(state.Tick.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for (var index = 0; index < state.CellCount; index++)
        {
            builder.Append("environment:").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendProperties(builder, state.Environment[index], environmentProperties);
            builder.Append("resources:").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendProperties(builder, state.Resources[index], resourceProperties);
        }

        builder.Append("populations:").Append(state.Populations.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var population in state.Populations)
        {
            AppendProperties(builder, population, populationProperties);
        }

        return builder.ToString();
    }

    private static void AppendProperties(StringBuilder builder, object target, IReadOnlyList<PropertyInfo> properties)
    {
        foreach (var property in properties)
        {
            builder.Append(property.Name).Append('=').Append(Format(property.GetValue(target))).Append('\n');
        }
    }

    private static string Format(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case double number:
                return Bits(number);
            case AssignedDouble assigned:
                return assigned.IsSet ? "set:" + Bits(assigned.Value) : "unset";
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case PopulationId id:
                return id.Value.ToString(CultureInfo.InvariantCulture);
            case OrganismId id:
                return id.Value;
            case EnvironmentScope scope:
                return FormatScope(scope);
            case Genome genome:
                return FormatGenome(genome);
            default:
                throw new InvalidOperationException(
                    $"No format for {value.GetType().Name}. A new stateful property has to be copied and compared.");
        }
    }

    private static string FormatScope(EnvironmentScope scope)
    {
        return scope switch
        {
            EnvironmentScope.PlanetaryMean => "planetary_mean",
            EnvironmentScope.Regional regional => "regional:" + regional.SiteId.Value,
            EnvironmentScope.Lab => "lab",
            EnvironmentScope.Game => "game",
            _ => throw new InvalidOperationException($"No format for scope {scope.GetType().Name}."),
        };
    }

    private static string FormatGenome(Genome genome)
    {
        var builder = new StringBuilder();
        foreach (var gene in genome.Expressions)
        {
            builder.Append(gene.GeneId.Value).Append('=').Append(Bits(gene.Expression)).Append(',');
        }

        return builder.ToString();
    }

    private static string Bits(double value)
        => BitConverter.DoubleToInt64Bits(value).ToString("x16", CultureInfo.InvariantCulture);
}
