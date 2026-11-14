// Request and response shapes for the API. Requests are deliberately loose (nullable fields,
// decimal quantity) so bad input reaches OrderValidator and gets a field-level message.
// Responses: OrderDto for one order, OrderSummaryDto for list rows, PagedResult for the list.

using OrderIntake.Core.Domain;

namespace OrderIntake.Core.Orders;

// ── Requests ────────────────────────────────────────────────────────────────
// Inputs are deliberately loose (nullable, decimal quantity) so that bad input reaches the
// validator and gets a field-level message, instead of failing in the JSON binder with a
// generic "could not deserialize" error.

public sealed record CreateOrderRequest(
    string? ExternalReference,
    CustomerInput? Customer,
    string? Currency,
    string? Notes,
    IReadOnlyList<LineItemInput>? Items);

public sealed record CustomerInput(string? Id, string? Email, string? Name);

public sealed record LineItemInput(string? Sku, string? Name, decimal? Quantity, decimal? UnitPrice);

public sealed record ChangeStatusRequest(string? Status, int? ExpectedVersion, string? Reason);

public sealed record OrderQuery(string? Status = null, string? Search = null, int Page = 1, int PageSize = 20);

// ── Responses ───────────────────────────────────────────────────────────────

public sealed record OrderDto(
    Guid Id,
    string ExternalReference,
    CustomerDto Customer,
    string Currency,
    string? Notes,
    IReadOnlyList<LineItemDto> Items,
    decimal Subtotal,
    decimal Total,
    string Status,
    IReadOnlyList<string> AllowedTransitions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    IReadOnlyList<StatusChangeDto> History)
{
    public static OrderDto From(Order o) => new(
        o.Id,
        o.ExternalReference,
        new CustomerDto(o.Customer.Id, o.Customer.Email, o.Customer.Name),
        o.Currency,
        o.Notes,
        o.Items.Select(i => new LineItemDto(i.Sku, i.Name, i.Quantity, i.UnitPrice, i.LineTotal)).ToArray(),
        o.Subtotal,
        o.Total,
        o.Status.ToString(),
        o.AllowedTransitions.Select(s => s.ToString()).ToArray(),
        o.CreatedAt,
        o.UpdatedAt,
        o.Version,
        o.History.Select(h => new StatusChangeDto(h.From.ToString(), h.To.ToString(), h.At, h.Reason)).ToArray());
}

public sealed record CustomerDto(string Id, string Email, string Name);

public sealed record LineItemDto(string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record StatusChangeDto(string From, string To, DateTimeOffset At, string? Reason);

/// <summary>Slim row for the list view: everything needed to scan, nothing that isn't.</summary>
public sealed record OrderSummaryDto(
    Guid Id,
    string ExternalReference,
    string CustomerId,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    int ItemCount,
    decimal Total,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderSummaryDto From(Order o) => new(
        o.Id, o.ExternalReference, o.Customer.Id, o.Customer.Name, o.Customer.Email, o.Currency,
        o.Items.Sum(i => i.Quantity), o.Total, o.Status.ToString(), o.CreatedAt, o.UpdatedAt);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public enum SubmitOutcome
{
    /// <summary>A new order was stored.</summary>
    Created,
    /// <summary>The same reference and the same details were sent again; the original order is returned.</summary>
    Replayed,
}

public sealed record SubmitResult(Order Order, SubmitOutcome Outcome);
