// Thread-safe in-memory implementation of IOrderRepository, used for this exercise.
// Data is lost when the API restarts.

using System.Collections.Concurrent;
using OrderIntake.Core.Domain;

namespace OrderIntake.Core.Orders;

/// <summary>
/// Thread-safe in-memory store. Orders are immutable, so readers never need the lock; writers
/// take a single lock so the reference index and the order table can't disagree.
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, Order> _byId = new();
    private readonly Dictionary<string, Guid> _byReference = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    public Task<(Order Order, bool Added)> AddIfReferenceAbsentAsync(Order order, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byReference.TryGetValue(order.ExternalReference, out var existingId))
                return Task.FromResult((_byId[existingId], false));

            var stored = order with { Sequence = ++_sequence };
            _byId[stored.Id] = stored;
            _byReference[stored.ExternalReference] = stored.Id;
            return Task.FromResult((stored, true));
        }
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var order) ? order : null);

    public Task<bool> TryReplaceAsync(Order updated, int expectedVersion, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(updated.Id, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);

            _byId[updated.Id] = updated with { Sequence = current.Sequence };
            return Task.FromResult(true);
        }
    }

    public Task<PagedResult<Order>> ListAsync(OrderStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        IEnumerable<Order> query = _byId.Values;

        if (status is not null)
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o =>
                o.ExternalReference.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.Customer.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.Customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.Customer.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Sequence)
            .ToList();

        var items = matches.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Task.FromResult(new PagedResult<Order>(items, page, pageSize, matches.Count));
    }
}
