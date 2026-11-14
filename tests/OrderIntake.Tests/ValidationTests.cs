using OrderIntake.Core;
using OrderIntake.Core.Orders;

namespace OrderIntake.Tests;

public class ValidationTests
{
    private static IReadOnlyDictionary<string, string[]> Errors(CreateOrderRequest? request)
    {
        var result = OrderValidator.Validate(request);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.FieldErrors;
    }

    [Fact]
    public void A_well_formed_request_is_valid_and_normalised()
    {
        var request = TestData.Request(reference: "  PO-1001 ", currency: "zar") with
        {
            Customer = new CustomerInput(" CUST-0042 ", "  Buyer@Acme.co.za ", " Acme Trading "),
        };

        var result = OrderValidator.Validate(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("PO-1001", result.Value.ExternalReference);
        Assert.Equal("ZAR", result.Value.Currency);
        Assert.Equal("buyer@acme.co.za", result.Value.Customer.Email);
        Assert.Equal("Acme Trading", result.Value.Customer.Name);
        Assert.Equal("CUST-0042", result.Value.Customer.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(int qty)
    {
        var errors = Errors(TestData.Request(items: [TestData.Item(qty: qty)]));
        Assert.Contains("greater than zero", errors["items[0].quantity"][0]);
    }

    [Fact]
    public void Quantity_must_be_a_whole_number()
    {
        var errors = Errors(TestData.Request(items: [TestData.Item(qty: 1.5m)]));
        Assert.Contains("whole number", errors["items[0].quantity"][0]);
    }

    [Fact]
    public void Price_cannot_be_negative()
    {
        var errors = Errors(TestData.Request(items: [TestData.Item(price: -0.01m)]));
        Assert.Contains("negative", errors["items[0].unitPrice"][0]);
    }

    [Fact]
    public void Price_cannot_have_more_than_two_decimals_but_trailing_zeros_are_fine()
    {
        var errors = Errors(TestData.Request(items: [TestData.Item(price: 1.005m)]));
        Assert.Contains("decimal places", errors["items[0].unitPrice"][0]);

        Assert.True(OrderValidator.Validate(TestData.Request(items: [TestData.Item(price: 1.5000m)])).IsSuccess);
    }

    [Fact]
    public void An_order_needs_at_least_one_line()
    {
        var errors = Errors(TestData.Request() with { Items = [] });
        Assert.True(errors.ContainsKey("items"));
    }

    [Fact]
    public void Duplicate_skus_in_one_order_are_rejected()
    {
        var errors = Errors(TestData.Request(items: [TestData.Item("SKU-A"), TestData.Item("sku-a")]));
        Assert.Contains("more than once", errors["items[1].sku"][0]);
    }

    [Fact]
    public void Missing_fields_are_all_reported_at_once()
    {
        var errors = Errors(new CreateOrderRequest(null, null, null, null, null));

        Assert.True(errors.ContainsKey("externalReference"));
        Assert.True(errors.ContainsKey("customer.id"));
        Assert.True(errors.ContainsKey("customer.email"));
        Assert.True(errors.ContainsKey("customer.name"));
        Assert.True(errors.ContainsKey("currency"));
        Assert.True(errors.ContainsKey("items"));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    public void Customer_email_must_be_valid(string email)
    {
        var errors = Errors(TestData.Request() with { Customer = new CustomerInput("CUST-1", email, "X") });
        Assert.True(errors.ContainsKey("customer.email"));
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("rands")]
    public void Unsupported_currency_is_rejected(string currency)
    {
        var errors = Errors(TestData.Request(currency: currency));
        Assert.True(errors.ContainsKey("currency"));
    }

    [Theory]
    [InlineData("-PO1")]
    [InlineData("PO 1")]
    public void Reference_format_is_enforced(string reference)
    {
        var errors = Errors(TestData.Request(reference: reference));
        Assert.True(errors.ContainsKey("externalReference"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("-CUST")]
    [InlineData("CUST 42")]
    public void Customer_id_is_required_and_well_formed(string? id)
    {
        var errors = Errors(TestData.Request() with { Customer = new CustomerInput(id, "buyer@acme.co.za", "Acme") });
        Assert.True(errors.ContainsKey("customer.id"));
    }
}
