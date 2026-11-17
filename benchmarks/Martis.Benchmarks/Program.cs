using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Running;
using Martis.Benchmarks;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Snapshots;

// `--profile [ticks]` prints the time of each pipeline step (EnvironmentUpdate,
// MetabolismUpdate, PopulationUpdate, MutationUpdate...) for the §7.4 load without
// BenchmarkDotNet. Anything else goes to BenchmarkDotNet (for example --filter *).
if (args.Length > 0 && args[0] == "--profile")
{
    var ticks = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1000;
    Profile.Run(ticks);
    return 0;
}

BenchmarkSwitcher.FromAssembly(typeof(FullSimulation).Assembly).Run(args);
return 0;

namespace Martis.Benchmarks
{
    internal static class Profile
    {
        public static void Run(int ticks)
        {
            var workspace = new MetabolismWorkspace(World.Catalog);
            var timed = BiologyPipeline.Systems(workspace).Select(system => new TimedSystem(system)).ToArray();
            var engine = World.Stress(workspace, timed);

            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var gen0 = GC.CollectionCount(0);
            var gen2 = GC.CollectionCount(2);
            var start = Stopwatch.GetTimestamp();
            var peak = 0L;
            for (var tick = 0; tick < ticks; tick++)
            {
                engine.Advance(1);
                if (tick % 50 == 0)
                {
                    peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
                }
            }

            var total = Stopwatch.GetElapsedTime(start);
            peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
            var snapStart = Stopwatch.GetTimestamp();
            var snapshot = StateSnapshot.Capture(engine, null);
            var bytes = StateSnapshotBinary.Write(snapshot);
            var snapTime = Stopwatch.GetElapsedTime(snapStart);

            Console.WriteLine($"MARTIS profile: {ticks} ticks, {World.StressPopulations} populations at start, {engine.State.Populations.Count} at the end");
            Console.WriteLine($"Runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {System.Environment.ProcessorCount} logical CPUs");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Total {total.TotalSeconds:0.000} s ({total.TotalMilliseconds / ticks:0.000} ms/tick)"));
            foreach (var system in timed.OrderByDescending(system => system.Elapsed))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {system.Name,-28} {system.Elapsed.TotalMilliseconds,10:0.0} ms  {system.Elapsed / total,6:P1}"));
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Allocated {(GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1048576.0:0.0} MB, GC gen0 {GC.CollectionCount(0) - gen0}, gen2 {GC.CollectionCount(2) - gen2}, peak working set {peak / 1048576.0:0.0} MB"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Snapshot capture + binary write {snapTime.TotalMilliseconds:0.0} ms, {bytes.Length / 1048576.0:0.00} MB"));
        }
    }
}
