using OrderIntake.Core.Domain;

namespace OrderIntake.Tests;

public class TotalsTests
{
    [Fact]
    public async Task Server_computes_line_totals_subtotal_and_total()
    {
        var (service, _, _) = TestData.Service();

        var result = await service.SubmitAsync(TestData.Request());

        var order = result.Value.Order;
        Assert.Equal(59.97m, order.Items[0].LineTotal);   // 3 × 19.99
        Assert.Equal(250.00m, order.Items[1].LineTotal);  // 1 × 250
        Assert.Equal(309.97m, order.Subtotal);
        Assert.Equal(309.97m, order.Total);
    }

    [Fact]
    public async Task Zero_priced_lines_are_allowed_and_contribute_nothing()
    {
        var (service, _, _) = TestData.Service();

        var result = await service.SubmitAsync(TestData.Request(items:
            [TestData.Item("FREE", 5, 0m), TestData.Item("PAID", 2, 12.50m)]));

        Assert.Equal(0m, result.Value.Order.Items[0].LineTotal);
        Assert.Equal(25.00m, result.Value.Order.Total);
    }

    [Theory]
    [InlineData(3, 0.335, 1.01)]   // 1.005 → 1.01: midpoint rounds away from zero, not to even
    [InlineData(1, 0.125, 0.13)]
    [InlineData(7, 0.10, 0.70)]
    public void Line_total_rounds_commercially_to_two_places(int qty, decimal price, decimal expected)
    {
        Assert.Equal(expected, Money.LineTotal(qty, price));
    }

    [Fact]
    public async Task Large_orders_do_not_lose_precision()
    {
        var (service, _, _) = TestData.Service();

        var result = await service.SubmitAsync(TestData.Request(items:
            [TestData.Item("BIG", 100_000, 9_999_999.99m)]));

        Assert.Equal(999_999_999_000.00m, result.Value.Order.Total);
    }
}
