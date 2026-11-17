using System.Runtime.CompilerServices;

// StateCheckpoint stays internal to the engine. The reflection test compares
// the public state it copies with EnvironmentState, ResourcePool and Population.
[assembly: InternalsVisibleTo("Martis.Simulation.Tests")]
