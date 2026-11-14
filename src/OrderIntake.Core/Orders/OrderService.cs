// Application service: the use cases behind the endpoints.
// SubmitAsync   validate, fingerprint, add if the reference is new; otherwise replay or refuse
// ListAsync     newest first, optional status filter, search and paging
// ChangeStatusAsync  apply the workflow rules with an optimistic-concurrency version check

using OrderIntake.Core.Domain;

namespace OrderIntake.Core.Orders;

public sealed class OrderService(IOrderRepository repository, TimeProvider clock)
{
    public const int MaxPageSize = 100;
    private const int MaxConcurrencyRetries = 3;

    /// <summary>
    /// Submits an order. Resubmitting the same reference with the same details returns the original
    /// order (Replayed) rather than creating a second one; the same reference with different details
    /// is rejected so a typo'd reference can't silently swallow a different order.
    /// </summary>
    public async Task<Result<SubmitResult>> SubmitAsync(CreateOrderRequest? request, CancellationToken ct = default)
    {
        var validated = OrderValidator.Validate(request);
        if (!validated.IsSuccess) return validated.Error!;

        var input = validated.Value;
        var fingerprint = OrderFingerprint.Compute(input);

        var candidate = Order.Create(
            Guid.NewGuid(), input.ExternalReference, input.Customer, input.Currency, input.Notes,
            input.Items, fingerprint, clock.GetUtcNow());

        var (stored, added) = await repository.AddIfReferenceAbsentAsync(candidate, ct);

        if (added)
            return new SubmitResult(stored, SubmitOutcome.Created);

        if (stored.Fingerprint == fingerprint)
            return new SubmitResult(stored, SubmitOutcome.Replayed);

        return Error.DuplicateReference(input.ExternalReference, stored);
    }

    public async Task<Result<Order>> GetAsync(Guid id, CancellationToken ct = default) =>
        await repository.GetAsync(id, ct) is { } order ? order : Error.NotFound(id);

    public async Task<Result<PagedResult<Order>>> ListAsync(OrderQuery query, CancellationToken ct = default)
    {
        OrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var parsed = OrderValidator.ParseStatus(query.Status);
            if (!parsed.IsSuccess) return parsed.Error!;
            status = parsed.Value;
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        return await repository.ListAsync(status, query.Search, page, pageSize, ct);
    }

    /// <summary>
    /// Changes status. If the caller sends expectedVersion (the UI always does), a stale view is
    /// rejected rather than overwritten. Without it, a lost race is retried against fresh state,
    /// and the transition rules are re-checked each time.
    /// </summary>
    public async Task<Result<Order>> ChangeStatusAsync(Guid id, ChangeStatusRequest? request, CancellationToken ct = default)
    {
        var target = OrderValidator.ParseStatus(request?.Status);
        if (!target.IsSuccess) return target.Error!;

        var reason = string.IsNullOrWhiteSpace(request!.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > 500 })
            return Error.Validation(new Dictionary<string, string[]> { ["reason"] = ["Reason can be at most 500 characters."] });

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var current = await repository.GetAsync(id, ct);
            if (current is null) return Error.NotFound(id);

            if (request.ExpectedVersion is { } expected && expected != current.Version)
                return Error.ConcurrencyConflict(current);

            var changed = current.ChangeStatus(target.Value, clock.GetUtcNow(), reason);
            if (!changed.IsSuccess) return changed.Error!;

            if (await repository.TryReplaceAsync(changed.Value, current.Version, ct))
                return changed.Value;

            if (request.ExpectedVersion is not null)
            {
                var latest = await repository.GetAsync(id, ct);
                return Error.ConcurrencyConflict(latest ?? current);
            }
        }

        var final = await repository.GetAsync(id, ct);
        return final is null ? Error.NotFound(id) : Error.ConcurrencyConflict(final);
    }
}
