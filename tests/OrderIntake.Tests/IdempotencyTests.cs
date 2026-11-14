using OrderIntake.Core;
using OrderIntake.Core.Domain;
using OrderIntake.Core.Orders;

namespace OrderIntake.Tests;

public class IdempotencyTests
{
    [Fact]
    public async Task First_submission_creates_the_order_as_pending()
    {
        var (service, _, _) = TestData.Service();

        var result = await service.SubmitAsync(TestData.Request());

        Assert.Equal(SubmitOutcome.Created, result.Value.Outcome);
        Assert.Equal(OrderStatus.Pending, result.Value.Order.Status);
        Assert.Equal(1, result.Value.Order.Version);
    }

    [Fact]
    public async Task Resubmitting_the_same_order_returns_the_original_without_creating_a_duplicate()
    {
        var (service, repo, clock) = TestData.Service();

        var first = await service.SubmitAsync(TestData.Request());
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await service.SubmitAsync(TestData.Request());

        Assert.Equal(SubmitOutcome.Replayed, second.Value.Outcome);
        Assert.Equal(first.Value.Order.Id, second.Value.Order.Id);
        Assert.Equal(first.Value.Order.CreatedAt, second.Value.Order.CreatedAt);
        Assert.Equal(1, (await repo.ListAsync(null, null, 1, 100)).TotalCount);
    }

    [Fact]
    public async Task Cosmetic_differences_still_count_as_the_same_order()
    {
        var (service, _, _) = TestData.Service();
        await service.SubmitAsync(TestData.Request(reference: "PO-1001"));

        // Different reference casing and whitespace, customer id and email casing, currency casing and line order.
        var resubmit = new CreateOrderRequest(
            " po-1001 ",
            new CustomerInput("cust-0042", "BUYER@acme.co.za", "Acme Trading"),
            "zar",
            null,
            [new LineItemInput("SKU-B", "Gadget", 1, 250.00m), new LineItemInput("SKU-A", "Widget", 3, 19.99m)]);

        var result = await service.SubmitAsync(resubmit);

        Assert.Equal(SubmitOutcome.Replayed, result.Value.Outcome);
    }

    [Fact]
    public async Task Same_reference_with_different_details_is_rejected_and_points_at_the_existing_order()
    {
        var (service, repo, _) = TestData.Service();
        var original = await service.SubmitAsync(TestData.Request());

        var clash = await service.SubmitAsync(TestData.Request(items: [TestData.Item("SKU-Z", 9, 1m)]));

        Assert.False(clash.IsSuccess);
        Assert.Equal(ErrorKind.DuplicateReference, clash.Error!.Kind);
        Assert.Equal(original.Value.Order.Id, clash.Error.Details["existingOrderId"]);
        Assert.Equal(1, (await repo.ListAsync(null, null, 1, 100)).TotalCount);
    }

    [Fact]
    public async Task Replay_returns_the_current_state_not_the_state_at_submission()
    {
        var (service, _, _) = TestData.Service();
        var created = await service.SubmitAsync(TestData.Request());
        await service.ChangeStatusAsync(created.Value.Order.Id, new ChangeStatusRequest("Confirmed", null, null));

        var replay = await service.SubmitAsync(TestData.Request());

        Assert.Equal(SubmitOutcome.Replayed, replay.Value.Outcome);
        Assert.Equal(OrderStatus.Confirmed, replay.Value.Order.Status);
    }

    [Fact]
    public async Task Concurrent_double_submits_create_exactly_one_order()
    {
        var (service, repo, _) = TestData.Service();

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => service.SubmitAsync(TestData.Request()))));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Single(results, r => r.Value.Outcome == SubmitOutcome.Created);
        Assert.Single(results.Select(r => r.Value.Order.Id).Distinct());
        Assert.Equal(1, (await repo.ListAsync(null, null, 1, 100)).TotalCount);
    }

    [Fact]
    public async Task Invalid_submissions_do_not_reserve_the_reference()
    {
        var (service, _, _) = TestData.Service();

        var bad = await service.SubmitAsync(TestData.Request(items: [TestData.Item(qty: 0)]));
        var good = await service.SubmitAsync(TestData.Request());

        Assert.False(bad.IsSuccess);
        Assert.Equal(SubmitOutcome.Created, good.Value.Outcome);
    }

    [Fact]
    public async Task Same_reference_for_a_different_customer_is_a_clash()
    {
        var (service, _, _) = TestData.Service();
        await service.SubmitAsync(TestData.Request());

        var other = TestData.Request() with { Customer = new CustomerInput("CUST-0099", "buyer@acme.co.za", "Acme Trading") };
        var result = await service.SubmitAsync(other);

        Assert.Equal(ErrorKind.DuplicateReference, result.Error!.Kind);
    }
}
