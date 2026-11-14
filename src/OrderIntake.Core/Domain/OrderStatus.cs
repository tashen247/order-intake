// The four order statuses and the workflow between them (OrderStatusRules): which changes are
// allowed from each status, and a specific explanation for every change that isn't.

namespace OrderIntake.Core.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Fulfilled,
    Cancelled
}

/// <summary>
/// The status workflow for a simple sales flow, kept in one place so the API,
/// the UI (via AllowedTransitions on the DTO) and the tests all agree.
///
///   Pending ──► Confirmed ──► Fulfilled (final)
///      │            │
///      └────────────┴──► Cancelled (final)
/// </summary>
public static class OrderStatusRules
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Fulfilled, OrderStatus.Cancelled],
            [OrderStatus.Fulfilled] = [],
            [OrderStatus.Cancelled] = [],
        };

    public static IReadOnlyList<OrderStatus> AllowedFrom(OrderStatus current) => Transitions[current];

    public static bool IsFinal(OrderStatus status) => Transitions[status].Length == 0;

    /// <summary>Returns null when the transition is allowed, otherwise a message a sales rep can act on.</summary>
    public static string? Explain(OrderStatus from, OrderStatus to)
    {
        if (from == to)
            return $"The order is already {from}.";

        if (Transitions[from].Contains(to))
            return null;

        if (IsFinal(from))
            return $"A {from} order can't be changed. {from} is a final status.";

        var allowed = string.Join(" or ", Transitions[from]);
        return (from, to) switch
        {
            (OrderStatus.Pending, OrderStatus.Fulfilled) =>
                "A Pending order must be Confirmed before it can be Fulfilled.",
            (_, OrderStatus.Pending) =>
                $"A {from} order can't go back to Pending. From {from} you can move it to {allowed}.",
            _ => $"Can't move an order from {from} to {to}. From {from} you can move it to {allowed}."
        };
    }
}
