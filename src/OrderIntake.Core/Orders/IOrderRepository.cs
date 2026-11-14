// Storage abstraction for orders. Two operations carry the correctness guarantees:
// AddIfReferenceAbsentAsync (atomic duplicate prevention by external reference) and
// TryReplaceAsync (optimistic concurrency by version). A database implementation maps these to
// a unique index and a rowversion column.

using OrderIntake.Core.Domain;

namespace OrderIntake.Core.Orders;

public interface IOrderRepository
{
    /// <summary>
    /// Atomically stores the order unless its external reference is already taken.
    /// Returns the stored order and whether it was added (false = the existing order for that reference).
    /// This is the single point that makes duplicate prevention race-free; in a database it maps to
    /// a unique index on the normalised reference.
    /// </summary>
    Task<(Order Order, bool Added)> AddIfReferenceAbsentAsync(Order order, CancellationToken ct = default);

    Task<Order?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Optimistic concurrency: replaces the order only if the stored version still equals expectedVersion.</summary>
    Task<bool> TryReplaceAsync(Order updated, int expectedVersion, CancellationToken ct = default);

    Task<PagedResult<Order>> ListAsync(OrderStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
}
