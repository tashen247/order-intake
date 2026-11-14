using OrderIntake.Core;
using OrderIntake.Core.Domain;
using OrderIntake.Core.Orders;

namespace OrderIntake.Tests;

public class StatusTests
{
    public static TheoryData<OrderStatus, OrderStatus, bool> AllTransitions()
    {
        var allowed = new HashSet<(OrderStatus, OrderStatus)>
        {
            (OrderStatus.Pending, OrderStatus.Confirmed),
            (OrderStatus.Pending, OrderStatus.Cancelled),
            (OrderStatus.Confirmed, OrderStatus.Fulfilled),
            (OrderStatus.Confirmed, OrderStatus.Cancelled),
        };

        var data = new TheoryData<OrderStatus, OrderStatus, bool>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        foreach (var to in Enum.GetValues<OrderStatus>())
            data.Add(from, to, allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition_matrix_matches_the_sales_flow(OrderStatus from, OrderStatus to, bool expectedAllowed)
    {
        var message = OrderStatusRules.Explain(from, to);

        if (expectedAllowed)
            Assert.Null(message);
        else
            Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Fulfilled, "must be Confirmed before")]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Cancelled, "final status")]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, "final status")]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending, "can't go back to Pending")]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Confirmed, "already Confirmed")]
    public void Rejected_transitions_explain_themselves(OrderStatus from, OrderStatus to, string expected)
    {
        Assert.Contains(expected, OrderStatusRules.Explain(from, to));
    }

    [Fact]
    public async Task Happy_path_records_history_and_bumps_version()
    {
        var (service, _, clock) = TestData.Service();
        var id = (await service.SubmitAsync(TestData.Request())).Value.Order.Id;

        clock.Advance(TimeSpan.FromHours(1));
        await service.ChangeStatusAsync(id, new ChangeStatusRequest("Confirmed", 1, null));
        clock.Advance(TimeSpan.FromHours(1));
        var fulfilled = await service.ChangeStatusAsync(id, new ChangeStatusRequest("fulfilled", 2, "Delivered"));

        var order = fulfilled.Value;
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3, order.Version);
        Assert.Equal(TestData.Start.AddHours(2), order.UpdatedAt);
        Assert.Equal(TestData.Start, order.CreatedAt);
        Assert.Collection(order.History,
            h => Assert.Equal((OrderStatus.Pending, OrderStatus.Confirmed), (h.From, h.To)),
            h => Assert.Equal((OrderStatus.Confirmed, OrderStatus.Fulfilled, "Delivered"), (h.From, h.To, h.Reason)));
        Assert.Empty(order.AllowedTransitions);
    }

    [Fact]
    public async Task Disallowed_change_leaves_the_order_untouched_and_lists_what_is_allowed()
    {
        var (service, _, _) = TestData.Service();
        var id = (await service.SubmitAsync(TestData.Request())).Value.Order.Id;

        var result = await service.ChangeStatusAsync(id, new ChangeStatusRequest("Fulfilled", null, null));

        Assert.Equal(ErrorKind.InvalidTransition, result.Error!.Kind);
        Assert.Equal(new[] { "Confirmed", "Cancelled" }, (string[])result.Error.Details["allowedTransitions"]!);
        var stored = (await service.GetAsync(id)).Value;
        Assert.Equal(OrderStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected_instead_of_overwriting()
    {
        var (service, _, _) = TestData.Service();
        var id = (await service.SubmitAsync(TestData.Request())).Value.Order.Id;

        // Rep A confirms; rep B, still looking at version 1, tries to cancel.
        await service.ChangeStatusAsync(id, new ChangeStatusRequest("Confirmed", 1, null));
        var result = await service.ChangeStatusAsync(id, new ChangeStatusRequest("Cancelled", 1, null));

        Assert.Equal(ErrorKind.ConcurrencyConflict, result.Error!.Kind);
        Assert.Equal(OrderStatus.Confirmed, (await service.GetAsync(id)).Value.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Shipped")]
    [InlineData("99")]
    public async Task Unknown_status_is_a_validation_error(string? status)
    {
        var (service, _, _) = TestData.Service();
        var id = (await service.SubmitAsync(TestData.Request())).Value.Order.Id;

        var result = await service.ChangeStatusAsync(id, new ChangeStatusRequest(status, null, null));

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Changing_an_unknown_order_is_not_found()
    {
        var (service, _, _) = TestData.Service();
        var result = await service.ChangeStatusAsync(Guid.NewGuid(), new ChangeStatusRequest("Confirmed", null, null));
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }
}
