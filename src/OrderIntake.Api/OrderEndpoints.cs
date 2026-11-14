// HTTP surface of the API: the four /api/orders endpoints as minimal APIs.
// Endpoints only translate between HTTP and OrderService: 201 for a new order, 200 plus the
// Idempotent-Replayed header for an identical resubmit, and ToProblem() maps every domain error
// to one stable status code (400 validation, 404 not found, 409 conflict, 422 rule violation).

using OrderIntake.Core;
using OrderIntake.Core.Orders;

namespace OrderIntake.Api;

public static class OrderEndpoints
{
    /// <summary>Set on a 200 response to POST /api/orders when the order already existed.</summary>
    public const string ReplayHeader = "Idempotent-Replayed";

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("/", async (CreateOrderRequest? request, OrderService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.SubmitAsync(request, ct);
                if (!result.IsSuccess) return result.Error!.ToProblem();

                var (order, outcome) = result.Value;
                var dto = OrderDto.From(order);

                if (outcome == SubmitOutcome.Replayed)
                {
                    // Same reference, same details: answer as if the first call had just succeeded,
                    // but say so, so the UI can tell the rep "this was already submitted".
                    http.Response.Headers[ReplayHeader] = "true";
                    return Results.Ok(dto);
                }

                return Results.Created($"/api/orders/{order.Id}", dto);
            })
            .WithName("SubmitOrder")
            .WithSummary("Submit an order. Resubmitting the same externalReference with the same details is safe.")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .Produces<OrderDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapGet("/", async ([AsParameters] OrderQuery query, OrderService service, CancellationToken ct) =>
            {
                var result = await service.ListAsync(query, ct);
                if (!result.IsSuccess) return result.Error!.ToProblem();

                var page = result.Value;
                return Results.Ok(new PagedResult<OrderSummaryDto>(
                    page.Items.Select(OrderSummaryDto.From).ToArray(), page.Page, page.PageSize, page.TotalCount));
            })
            .WithName("ListOrders")
            .WithSummary("List orders, newest first. Optional status filter and free-text search.")
            .Produces<PagedResult<OrderSummaryDto>>()
            .ProducesValidationProblem();

        orders.MapGet("/{id:guid}", async (Guid id, OrderService service, CancellationToken ct) =>
            {
                var result = await service.GetAsync(id, ct);
                return result.IsSuccess ? Results.Ok(OrderDto.From(result.Value)) : result.Error!.ToProblem();
            })
            .WithName("GetOrder")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPatch("/{id:guid}/status", async (Guid id, ChangeStatusRequest? request, OrderService service, CancellationToken ct) =>
            {
                var result = await service.ChangeStatusAsync(id, request, ct);
                return result.IsSuccess ? Results.Ok(OrderDto.From(result.Value)) : result.Error!.ToProblem();
            })
            .WithName("ChangeOrderStatus")
            .WithSummary("Move an order through Pending → Confirmed → Fulfilled, or cancel it.")
            .Produces<OrderDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>Maps domain errors to RFC 7807 problem responses with stable, documented status codes.</summary>
    public static IResult ToProblem(this Error error)
    {
        if (error.Kind == ErrorKind.Validation)
            return Results.ValidationProblem(
                error.FieldErrors.ToDictionary(kv => kv.Key, kv => kv.Value),
                detail: error.Message,
                title: "Some details need fixing");

        var (status, title, code) = error.Kind switch
        {
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Order not found", "not_found"),
            ErrorKind.DuplicateReference => (StatusCodes.Status409Conflict, "Reference already used", "duplicate_reference"),
            ErrorKind.ConcurrencyConflict => (StatusCodes.Status409Conflict, "Order was changed", "concurrency_conflict"),
            ErrorKind.InvalidTransition => (StatusCodes.Status422UnprocessableEntity, "Status change not allowed", "invalid_transition"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "unexpected"),
        };

        var extensions = new Dictionary<string, object?>(error.Details) { ["code"] = code };
        return Results.Problem(detail: error.Message, statusCode: status, title: title, extensions: extensions);
    }
}
