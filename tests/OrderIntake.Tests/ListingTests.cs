using OrderIntake.Core;
using OrderIntake.Core.Orders;

namespace OrderIntake.Tests;

public class ListingTests
{
    [Fact]
    public async Task Orders_are_listed_newest_first()
    {
        var (service, _, clock) = TestData.Service();
        foreach (var r in new[] { "PO-1", "PO-2", "PO-3" })
        {
            await service.SubmitAsync(TestData.Request(reference: r));
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var page = (await service.ListAsync(new OrderQuery())).Value;

        Assert.Equal(new[] { "PO-3", "PO-2", "PO-1" }, page.Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public async Task Orders_created_in_the_same_instant_still_list_newest_first()
    {
        var (service, _, _) = TestData.Service(); // clock never advances
        await service.SubmitAsync(TestData.Request(reference: "PO-A"));
        await service.SubmitAsync(TestData.Request(reference: "PO-B"));

        var page = (await service.ListAsync(new OrderQuery())).Value;

        Assert.Equal("PO-B", page.Items[0].ExternalReference);
    }

    [Fact]
    public async Task A_status_change_does_not_reorder_the_list()
    {
        var (service, _, clock) = TestData.Service();
        var older = (await service.SubmitAsync(TestData.Request(reference: "PO-OLD"))).Value.Order.Id;
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.SubmitAsync(TestData.Request(reference: "PO-NEW"));
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.ChangeStatusAsync(older, new ChangeStatusRequest("Confirmed", null, null));

        var page = (await service.ListAsync(new OrderQuery())).Value;

        Assert.Equal("PO-NEW", page.Items[0].ExternalReference);
    }

    [Fact]
    public async Task Filters_by_status_and_search_and_pages()
    {
        var (service, _, clock) = TestData.Service();
        for (var i = 1; i <= 5; i++)
        {
            await service.SubmitAsync(TestData.Request(reference: $"PO-{i}"));
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var confirmed = (await service.ListAsync(new OrderQuery(Search: "PO-2"))).Value.Items[0].Id;
        await service.ChangeStatusAsync(confirmed, new ChangeStatusRequest("Confirmed", null, null));

        Assert.Single((await service.ListAsync(new OrderQuery(Status: "confirmed"))).Value.Items);
        Assert.Equal(4, (await service.ListAsync(new OrderQuery(Status: "Pending"))).Value.TotalCount);

        var page2 = (await service.ListAsync(new OrderQuery(Page: 2, PageSize: 2))).Value;
        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(new[] { "PO-3", "PO-2" }, page2.Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public async Task Bad_status_filter_is_a_validation_error()
    {
        var (service, _, _) = TestData.Service();
        var result = await service.ListAsync(new OrderQuery(Status: "Shipped"));
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }
}
