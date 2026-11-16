using Martis.Domain.Biology;
using Martis.Domain.Environment;

namespace Martis.Domain.World;

/// <summary>
/// World state stored in linear arrays (TDD §12.2, H-02, ADR-005).
/// Cell <c>i</c> is <c>y * width + x</c>. The dimensions are constructor arguments.
/// Each cell has its own environment, resource pool and allocation buffer.
/// The buffer records demand; nothing in this type subtracts from the pool.
/// </summary>
public sealed class SimulationState
{
    public SimulationState(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        }

        var count = checked(width * height);
        Width = width;
        Height = height;
        Environment = new EnvironmentState[count];
        Resources = new ResourcePool[count];
        AllocationBuffers = new AllocationBuffer[count];
        for (var i = 0; i < count; i++)
        {
            Environment[i] = new EnvironmentState();
            Resources[i] = new ResourcePool();
            AllocationBuffers[i] = new AllocationBuffer();
        }

        Populations = [];
        Statistics = new SimulationStatistics();
    }

    public int Width { get; }

    public int Height { get; }

    public int CellCount => checked(Width * Height);

    public long Tick { get; set; }

    public EnvironmentState[] Environment { get; }

    public ResourcePool[] Resources { get; }

    public AllocationBuffer[] AllocationBuffers { get; }

    /// <summary>
    /// Populations in caller order. The validator walks this list in that order.
    /// Adding a population does not move resources.
    /// </summary>
    public List<Population> Populations { get; }

    public SimulationStatistics Statistics { get; }

    public GridIndex Index(int x, int y) => new(x, y, Width, Height);

    public int ToLinearIndex(int x, int y) => Index(x, y).ToLinearIndex();
}
