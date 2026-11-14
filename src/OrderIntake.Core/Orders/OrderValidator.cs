// Validates and normalises an incoming order: reference format, customer id, email and name,
// supported currency, at least one line, positive whole quantities, non-negative prices with at
// most 2 decimals, no duplicate SKUs. Reports every problem at once, keyed by field path
// (e.g. "items[0].quantity") so the UI can show each message next to its input.

using System.Net.Mail;
using System.Text.RegularExpressions;
using OrderIntake.Core.Domain;

namespace OrderIntake.Core.Orders;

/// <summary>A submission that has passed validation, with every field trimmed and normalised.</summary>
public sealed record ValidOrder(
    string ExternalReference,
    Customer Customer,
    string Currency,
    string? Notes,
    IReadOnlyList<(string Sku, string Name, int Quantity, decimal UnitPrice)> Items);

public static partial class OrderValidator
{
    public const int MaxReferenceLength = 64;
    public const int MaxItems = 100;
    public const int MaxQuantity = 100_000;
    public const decimal MaxUnitPrice = 10_000_000m;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_./#]*$")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_.]*$")]
    private static partial Regex CustomerIdPattern();

    public static Result<ValidOrder> Validate(CreateOrderRequest? request)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        if (request is null)
        {
            Add("body", "A request body is required.");
            return Fail(errors);
        }

        // External reference
        var reference = request.ExternalReference?.Trim();
        if (string.IsNullOrEmpty(reference))
            Add("externalReference", "A reference is required (for example the customer's PO number).");
        else if (reference.Length > MaxReferenceLength)
            Add("externalReference", $"The reference can be at most {MaxReferenceLength} characters.");
        else if (!ReferencePattern().IsMatch(reference))
            Add("externalReference", "Use letters, digits and - _ . / # only, starting with a letter or digit.");

        // Customer
        var customerId = request.Customer?.Id?.Trim();
        if (string.IsNullOrEmpty(customerId))
            Add("customer.id", "Customer ID is required.");
        else if (customerId.Length > 64)
            Add("customer.id", "Customer ID can be at most 64 characters.");
        else if (!CustomerIdPattern().IsMatch(customerId))
            Add("customer.id", "Use letters, digits and - _ . only, starting with a letter or digit.");

        var email = request.Customer?.Email?.Trim();
        var name = request.Customer?.Name?.Trim();
        if (string.IsNullOrEmpty(email))
            Add("customer.email", "Customer email is required.");
        else if (!IsEmail(email))
            Add("customer.email", $"'{email}' is not a valid email address.");
        if (string.IsNullOrEmpty(name))
            Add("customer.name", "Customer name is required.");
        else if (name.Length > 200)
            Add("customer.name", "Customer name can be at most 200 characters.");

        // Currency
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency))
            Add("currency", "Currency is required.");
        else if (!Money.SupportedCurrencies.Contains(currency))
            Add("currency", $"Currency must be one of {string.Join(", ", Money.SupportedCurrencies.Order())}.");

        // Notes
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > 1000 })
            Add("notes", "Notes can be at most 1000 characters.");

        // Line items
        var items = new List<(string, string, int, decimal)>();
        if (request.Items is null || request.Items.Count == 0)
        {
            Add("items", "An order needs at least one line item.");
        }
        else if (request.Items.Count > MaxItems)
        {
            Add("items", $"An order can have at most {MaxItems} line items.");
        }
        else
        {
            var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                var p = $"items[{i}]";
                if (item is null) { Add(p, "Line item is empty."); continue; }

                var sku = item.Sku?.Trim();
                var itemName = item.Name?.Trim();

                if (string.IsNullOrEmpty(sku)) Add($"{p}.sku", "SKU is required.");
                else if (sku.Length > 64) Add($"{p}.sku", "SKU can be at most 64 characters.");
                else if (!seenSkus.Add(sku)) Add($"{p}.sku", $"SKU '{sku}' appears more than once. Combine it into one line.");

                if (string.IsNullOrEmpty(itemName)) Add($"{p}.name", "Item name is required.");
                else if (itemName.Length > 200) Add($"{p}.name", "Item name can be at most 200 characters.");

                var qty = item.Quantity;
                if (qty is null) Add($"{p}.quantity", "Quantity is required.");
                else if (qty <= 0) Add($"{p}.quantity", "Quantity must be greater than zero.");
                else if (qty != decimal.Truncate(qty.Value)) Add($"{p}.quantity", "Quantity must be a whole number.");
                else if (qty > MaxQuantity) Add($"{p}.quantity", $"Quantity can be at most {MaxQuantity:N0}.");

                var price = item.UnitPrice;
                if (price is null) Add($"{p}.unitPrice", "Unit price is required.");
                else if (price < 0) Add($"{p}.unitPrice", "Unit price can't be negative.");
                else if (price > MaxUnitPrice) Add($"{p}.unitPrice", $"Unit price can be at most {MaxUnitPrice:N0}.");
                else if (Money.DecimalPlaces(price.Value) > Money.MinorUnits)
                    Add($"{p}.unitPrice", $"Unit price can have at most {Money.MinorUnits} decimal places.");

                if (sku is not null && itemName is not null && qty is not null && price is not null)
                    items.Add((sku, itemName, (int)qty.Value, price.Value));
            }
        }

        if (errors.Count > 0) return Fail(errors);

        return new ValidOrder(reference!, new Customer(customerId!, email!.ToLowerInvariant(), name!), currency!, notes, items);
    }

    public static Result<OrderStatus> ParseStatus(string? status)
    {
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<OrderStatus>(status.Trim(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
            return parsed;

        return Error.Validation(new Dictionary<string, string[]>
        {
            ["status"] = [$"Status must be one of {string.Join(", ", Enum.GetNames<OrderStatus>())}."],
        });
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed) && parsed.Address == value && value.Contains('.', StringComparison.Ordinal);

    private static Error Fail(Dictionary<string, List<string>> errors) =>
        Error.Validation(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
}
