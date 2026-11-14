// Result<T> and Error: how the service reports expected failures (validation, not found,
// duplicate reference, stale version, disallowed status change) as values instead of exceptions.
// The API maps each ErrorKind to an HTTP status code in OrderEndpoints.ToProblem().

using OrderIntake.Core.Domain;

namespace OrderIntake.Core;

public enum ErrorKind
{
    Validation,
    NotFound,
    /// <summary>The external reference already belongs to an order with different details.</summary>
    DuplicateReference,
    /// <summary>The caller's expectedVersion is stale: someone else changed the order first.</summary>
    ConcurrencyConflict,
    InvalidTransition,
}

public sealed record Error(ErrorKind Kind, string Message)
{
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } = new Dictionary<string, string[]>();
    public IReadOnlyDictionary<string, object?> Details { get; init; } = new Dictionary<string, object?>();

    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ErrorKind.Validation, "One or more fields are invalid.") { FieldErrors = fieldErrors };

    public static Error NotFound(Guid id) => new(ErrorKind.NotFound, $"Order {id} was not found.");

    public static Error DuplicateReference(string reference, Order existing) =>
        new(ErrorKind.DuplicateReference,
            $"Reference '{reference}' was already used for an order submitted at {existing.CreatedAt:u} " +
            "with different details. Use a new reference for a new order, or open the existing one.")
        {
            Details = new Dictionary<string, object?> { ["existingOrderId"] = existing.Id },
        };

    public static Error ConcurrencyConflict(Order current) =>
        new(ErrorKind.ConcurrencyConflict,
            $"This order was changed by someone else (it is now {current.Status}). Refresh and try again.")
        {
            Details = new Dictionary<string, object?>
            {
                ["currentVersion"] = current.Version,
                ["currentStatus"] = current.Status.ToString(),
            },
        };

    public static Error InvalidTransition(string message, OrderStatus current, IReadOnlyList<OrderStatus> allowed) =>
        new(ErrorKind.InvalidTransition, message)
        {
            Details = new Dictionary<string, object?>
            {
                ["currentStatus"] = current.ToString(),
                ["allowedTransitions"] = allowed.Select(s => s.ToString()).ToArray(),
            },
        };
}

/// <summary>Expected failures are values, not exceptions; exceptions stay for genuinely unexpected faults.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value) { _value = value; Error = null; }
    private Result(Error error) { _value = default; Error = error; }

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {Error!.Message}");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}
