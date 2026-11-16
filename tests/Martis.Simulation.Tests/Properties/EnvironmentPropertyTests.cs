using CsCheck;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Properties;

/// <summary>
/// CsCheck chooses the seed and the step count. The simulation still uses pcg32-v1.
/// The fixture numbers are synthetic. The published file is covered by the load test.
/// </summary>
public sealed class EnvironmentPropertyTests
{
    [Fact]
    public void TemperatureStaysPutAndQuantitiesStayNonNegative()
    {
        Gen.Select(Gen.UInt, Gen.Int[0, 8]).Sample(sample =>
        {
            var scenario = MarsBaselineScenario.Load(ScenarioFixtures.Science, ScenarioFixtures.Deposits);
            var engine = MarsBaselineWorld.Create(scenario, sample.Item1, 1, 1);
            var before = engine.State.Environment[0].TemperatureKelvin.Value;
            engine.Advance(sample.Item2);
            if (BitConverter.DoubleToInt64Bits(engine.State.Environment[0].TemperatureKelvin.Value)
                != BitConverter.DoubleToInt64Bits(before))
            {
                return false;
            }

            foreach (var quantity in engine.State.Resources[0].Quantities)
            {
                if (!double.IsFinite(quantity.Value) || quantity.Value < 0.0)
                {
                    return false;
                }
            }

            foreach (var quantity in engine.State.Environment[0].Quantities)
            {
                if (!double.IsFinite(quantity.Value) || quantity.Value < 0.0)
                {
                    return false;
                }
            }

            return engine.State.MassLedgers[0].Matches(
                engine.State.Resources[0],
                engine.State.Environment[0].WaterIce);
        });
    }
}
