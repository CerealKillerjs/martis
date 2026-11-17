using Martis.Application;

namespace Martis.Application.Tests;

/// <summary>V1: the register of orders (Documento Maestro v2.0 §7.2).</summary>
public sealed class OrderBookTests
{
    [Fact]
    public void AnOrderIsIssuedPendingWithItsTargetActorAndTick()
    {
        var book = new OrderBook();

        var order = book.Issue("deploy", "(32, 32)", "rover-1", 12, estimatedCost: 0.5);

        Assert.Equal(1, order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(12, order.IssuedTick);
        Assert.Single(book.Pending);
    }

    [Fact]
    public void OrdersAreResolvedOnceWithTheirResult()
    {
        var book = new OrderBook();
        var deploy = book.Issue("deploy", "(1, 1)", "rover-1", 3);
        var analyze = book.Issue("analyze", "(2, 2)", "station", 3);

        var done = book.Complete(deploy.Id, 4, "0.5 kg de Fotótrofo A01");
        var failed = book.Fail(analyze.Id, 3, "Fuera de alcance");

        Assert.Equal(OrderStatus.Done, done.Status);
        Assert.Equal(4, done.ResolvedTick);
        Assert.Equal(OrderStatus.Failed, failed.Status);
        Assert.Empty(book.Pending);
        Assert.Throws<InvalidOperationException>(() => book.Complete(deploy.Id, 5, "again"));
    }

    [Fact]
    public void RecentListsTheNewestFirstAndIdsNeverRepeat()
    {
        var book = new OrderBook();
        for (var i = 0; i < 5; i++)
        {
            book.Issue("scan", $"site {i}", "rover-1", i);
        }

        var recent = book.Recent(2);

        Assert.Equal([5L, 4L], recent.Select(order => order.Id));
        Assert.Equal(5, book.All.Select(order => order.Id).Distinct().Count());
    }

    [Fact]
    public void BadOrdersAreRejected()
    {
        var book = new OrderBook();

        Assert.Throws<ArgumentException>(() => book.Issue(" ", "x", "y", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => book.Issue("deploy", "x", "y", 0, estimatedCost: -1));
        Assert.Throws<KeyNotFoundException>(() => book.Complete(99, 0, "?"));
        var order = book.Issue("deploy", "x", "y", 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => book.Complete(order.Id, 9, "too early"));
    }
}
