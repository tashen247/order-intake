// The order aggregate and its parts (customer, line items, status history).
// Orders are immutable records: Create() computes totals on the server, and ChangeStatus()
// returns a new record with Version + 1 and a history entry, or an InvalidTransition error.

namespace OrderIntake.Core.Domain;

/// <summary>The customer as given on the order. The id is the client's own customer id, taken as supplied.</summary>
public sealed record Customer(string Id, string Email, string Name);

public sealed record LineItem(string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record StatusChange(OrderStatus From, OrderStatus To, DateTimeOffset At, string? Reason);

/// <summary>
/// Immutable order aggregate. Every change produces a new instance with Version + 1,
/// which lets the repository do a cheap compare-and-swap instead of locking readers.
/// </summary>
public sealed record Order
{
    public required Guid Id { get; init; }
    public required string ExternalReference { get; init; }
    public required Customer Customer { get; init; }
    public required string Currency { get; init; }
    public string? Notes { get; init; }
    public required IReadOnlyList<LineItem> Items { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal Total { get; init; }
    public required OrderStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<StatusChange> History { get; init; }

    /// <summary>Hash of the normalised submission, used to tell a harmless resubmit from a clash.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Insertion order, assigned by the store; tie-breaker for "newest first".</summary>
    public long Sequence { get; init; }

    public static Order Create(
        Guid id,
        string externalReference,
        Customer customer,
        string currency,
        string? notes,
        IEnumerable<(string Sku, string Name, int Quantity, decimal UnitPrice)> items,
        string fingerprint,
        DateTimeOffset now)
    {
        var lines = items
            .Select(i => new LineItem(i.Sku, i.Name, i.Quantity, i.UnitPrice, Money.LineTotal(i.Quantity, i.UnitPrice)))
            .ToArray();

        var subtotal = Money.Round(lines.Sum(l => l.LineTotal));

        return new Order
        {
            Id = id,
            ExternalReference = externalReference,
            Customer = customer,
            Currency = currency,
            Notes = notes,
            Items = lines,
            Subtotal = subtotal,
            // No tax, discount or shipping in scope; Total is kept separate so they can be added
            // without changing the contract.
            Total = subtotal,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
            History = [],
            Fingerprint = fingerprint,
        };
    }

    public IReadOnlyList<OrderStatus> AllowedTransitions => OrderStatusRules.AllowedFrom(Status);

    public Result<Order> ChangeStatus(OrderStatus to, DateTimeOffset now, string? reason)
    {
        var problem = OrderStatusRules.Explain(Status, to);
        if (problem is not null)
            return Error.InvalidTransition(problem, Status, AllowedTransitions);

        return this with
        {
            Status = to,
            UpdatedAt = now,
            Version = Version + 1,
            History = [.. History, new StatusChange(Status, to, now, reason)],
        };
    }
}
