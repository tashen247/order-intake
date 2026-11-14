using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderIntake.Tests;

/// <summary>Thin HTTP-level checks: the business rules are covered above; these pin the contract.</summary>
public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static object Body(string reference, int qty = 2) => new
    {
        externalReference = reference,
        customer = new { id = "CUST-0042", email = "buyer@acme.co.za", name = "Acme" },
        currency = "ZAR",
        items = new[] { new { sku = "SKU-A", name = "Widget", quantity = qty, unitPrice = 10.50 } },
    };

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Post_then_repost_returns_201_then_200_with_replay_header_and_same_id()
    {
        var client = factory.CreateClient();
        var reference = $"API-{Guid.NewGuid():N}"[..20];

        var first = await client.PostAsJsonAsync("/api/orders", Body(reference));
        var second = await client.PostAsJsonAsync("/api/orders", Body(reference));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.NotNull(first.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        var firstBody = await Json(first);
        Assert.Equal(firstBody.GetProperty("id").GetString(), (await Json(second)).GetProperty("id").GetString());
        Assert.Equal(21.00m, firstBody.GetProperty("total").GetDecimal());
        Assert.Equal("Pending", firstBody.GetProperty("status").GetString());
        Assert.Equal("CUST-0042", firstBody.GetProperty("customer").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Same_reference_with_different_details_is_409_problem()
    {
        var client = factory.CreateClient();
        var reference = $"API-{Guid.NewGuid():N}"[..20];
        await client.PostAsJsonAsync("/api/orders", Body(reference, qty: 2));

        var clash = await client.PostAsJsonAsync("/api/orders", Body(reference, qty: 3));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("application/problem+json", clash.Content.Headers.ContentType?.MediaType);
        var problem = await Json(clash);
        Assert.Equal("duplicate_reference", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("existingOrderId", out _));
    }

    [Fact]
    public async Task Fractional_quantity_is_400_with_a_field_error()
    {
        var client = factory.CreateClient();
        var body = new
        {
            externalReference = "API-FRACTION",
            customer = new { id = "CUST-0042", email = "buyer@acme.co.za", name = "Acme" },
            currency = "ZAR",
            items = new[] { new { sku = "SKU-A", name = "Widget", quantity = 1.5, unitPrice = 10 } },
        };

        var response = await client.PostAsJsonAsync("/api/orders", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("items[0].quantity", out _));
    }

    [Fact]
    public async Task Disallowed_status_change_is_422_with_allowed_transitions()
    {
        var client = factory.CreateClient();
        var created = await Json(await client.PostAsJsonAsync("/api/orders", Body($"API-{Guid.NewGuid():N}"[..20])));
        var id = created.GetProperty("id").GetString();

        var response = await client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Fulfilled" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await Json(response);
        Assert.Contains("Confirmed before", problem.GetProperty("detail").GetString());
        Assert.Equal(2, problem.GetProperty("allowedTransitions").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        var response = await factory.CreateClient().GetAsync($"/api/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
