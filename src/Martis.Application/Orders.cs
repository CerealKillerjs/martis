namespace Martis.Application;

public enum OrderStatus
{
    Pending = 0,
    Done = 1,
    Failed = 2,
}

/// <summary>
/// An order of the AI to the world (Documento Maestro v2.0 §7.2): what, where, who, when it was
/// issued, its estimated cost, its state and the logged result. Orders never touch the engine
/// themselves: the host turns them into validated session commands applied at a tick boundary.
/// </summary>
public sealed record Order(
    long Id,
    string Kind,
    string Target,
    string Actor,
    int Priority,
    long IssuedTick,
    double EstimatedCost,
    OrderStatus Status = OrderStatus.Pending,
    string? Result = null,
    long? ResolvedTick = null);

/// <summary>The register of orders, in issue order, with ids that never repeat.</summary>
public sealed class OrderBook
{
    private readonly List<Order> _orders = [];
    private long _nextId = 1;

    public IReadOnlyList<Order> All => _orders;

    public IEnumerable<Order> Pending => _orders.Where(order => order.Status == OrderStatus.Pending);

    public Order Issue(string kind, string target, string actor, long tick, double estimatedCost = 0, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        if (!double.IsFinite(estimatedCost) || estimatedCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedCost), estimatedCost, "The cost must be finite and not negative.");
        }

        var order = new Order(_nextId++, kind, target, actor, priority, tick, estimatedCost);
        _orders.Add(order);
        return order;
    }

    public Order Complete(long id, long tick, string result) => Resolve(id, tick, OrderStatus.Done, result);

    public Order Fail(long id, long tick, string reason) => Resolve(id, tick, OrderStatus.Failed, reason);

    /// <summary>The last <paramref name="count"/> orders, newest first.</summary>
    public IReadOnlyList<Order> Recent(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return _orders.AsEnumerable().Reverse().Take(count).ToArray();
    }

    private Order Resolve(long id, long tick, OrderStatus status, string result)
    {
        var index = _orders.FindIndex(order => order.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"There is no order #{id}.");
        }

        var order = _orders[index];
        if (order.Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Order #{id} is already {order.Status}.");
        }

        if (tick < order.IssuedTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "An order cannot be resolved before it was issued.");
        }

        var resolved = order with { Status = status, Result = result, ResolvedTick = tick };
        _orders[index] = resolved;
        return resolved;
    }
}
