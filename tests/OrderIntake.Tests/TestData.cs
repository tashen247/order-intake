using OrderIntake.Core.Orders;

namespace OrderIntake.Tests;

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now = Now.Add(by);
}

internal static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    public static CreateOrderRequest Request(
        string reference = "PO-1001",
        string currency = "ZAR",
        string? notes = null,
        params LineItemInput[] items) =>
        new(reference,
            new CustomerInput("CUST-0042", "buyer@acme.co.za", "Acme Trading"),
            currency,
            notes,
            items.Length > 0
                ? items
                : [new LineItemInput("SKU-A", "Widget", 3, 19.99m), new LineItemInput("SKU-B", "Gadget", 1, 250m)]);

    public static LineItemInput Item(string sku = "SKU-A", decimal? qty = 1, decimal? price = 10m, string name = "Item") =>
        new(sku, name, qty, price);

    public static (OrderService Service, InMemoryOrderRepository Repo, FakeClock Clock) Service()
    {
        var repo = new InMemoryOrderRepository();
        var clock = new FakeClock(Start);
        return (new OrderService(repo, clock), repo, clock);
    }
}
