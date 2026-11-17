using Martis.Domain.Biology;
using Martis.Domain.Environment;

namespace Martis.Domain.World;

/// <summary>
/// World state stored in linear arrays (TDD §12.2, H-02, ADR-005).
/// Cell <c>i</c> is <c>y * width + x</c>. The dimensions are constructor arguments.
/// Each cell has its own environment, resource pool, water budget and allocation buffer.
/// The buffer records demand; nothing in this type subtracts from the pool.
/// The water budget records the total; it does not move water between compartments.
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
        WaterBudgets = new CellWaterBudget[count];
        MassLedgers = new CellMassLedger[count];
        for (var i = 0; i < count; i++)
        {
            Environment[i] = new EnvironmentState();
            Resources[i] = new ResourcePool();
            AllocationBuffers[i] = new AllocationBuffer();
            WaterBudgets[i] = new CellWaterBudget();
            MassLedgers[i] = new CellMassLedger();
        }

        Populations = [];
        Statistics = new SimulationStatistics();
    }

    public int Width { get; }

    public int Height { get; }

    public int CellCount => checked(Width * Height);

    public long Tick { get; set; }

    /// <summary>
    /// Id the next new population receives. Kept in the save (schema v2) so an id is
    /// never reused after an extinction. <see cref="AllocatePopulationId"/> also stays
    /// above every id in <see cref="Populations"/>.
    /// </summary>
    public long NextPopulationId { get; set; } = 1;

    /// <summary>
    /// Returns a fresh population id and advances <see cref="NextPopulationId"/>.
    /// Only populations appended since the last call are scanned while the list has not
    /// been shortened or reordered (the last scanned population is still in its place);
    /// otherwise the whole list is scanned again (T10: a full scan per mutation was O(N²)).
    /// </summary>
    public long AllocatePopulationId()
    {
        var id = NextPopulationId;
        var start = 0;
        if (_scannedCount > 0
            && _scannedCount <= Populations.Count
            && ReferenceEquals(Populations[_scannedCount - 1], _lastScanned)
            && id >= _scannedFloor)
        {
            start = _scannedCount;
        }

        for (var i = start; i < Populations.Count; i++)
        {
            if (Populations[i].Id.Value >= id)
            {
                id = Populations[i].Id.Value + 1;
            }
        }

        NextPopulationId = id + 1;
        _scannedCount = Populations.Count;
        _lastScanned = _scannedCount > 0 ? Populations[_scannedCount - 1] : null;
        _scannedFloor = NextPopulationId;
        return id;
    }

    private int _scannedCount;
    private Population? _lastScanned;
    private long _scannedFloor;

    public EnvironmentState[] Environment { get; }

    public ResourcePool[] Resources { get; }

    public AllocationBuffer[] AllocationBuffers { get; }

    /// <summary>
    /// One accounted water total per cell, aligned with <see cref="Resources"/> and
    /// <see cref="Environment"/>.
    /// </summary>
    public CellWaterBudget[] WaterBudgets { get; }

    /// <summary>
    /// One initial-mass account per cell. Empty until a scenario records the deposits.
    /// </summary>
    public CellMassLedger[] MassLedgers { get; }

    /// <summary>
    /// Populations in caller order. The validator walks this list in that order.
    /// Adding a population does not move resources.
    /// </summary>
    public List<Population> Populations { get; }

    public SimulationStatistics Statistics { get; }

    public GridIndex Index(int x, int y) => new(x, y, Width, Height);

    public int ToLinearIndex(int x, int y) => Index(x, y).ToLinearIndex();
}
