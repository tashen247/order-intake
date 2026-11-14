# Solution notes

## The core problem: repeat submissions

Reps resubmit because they're unsure the first attempt worked. So a resubmit should look exactly
like a success, but it must never create a second order.

**The rule:** the client-provided `externalReference` is the idempotency key.

| Situation | Result |
|---|---|
| New reference | `201 Created`, order stored as `Pending` |
| Same reference, same details | `200 OK` with the **original** order and an `Idempotent-Replayed: true` header. The UI tells the rep "already submitted, no duplicate created". |
| Same reference, different details | `409 Conflict` with the existing order's id. Never silently returns a different order, never overwrites. |

**What counts as "the same details":** the request is validated and normalised (trimmed, email
lower-cased, currency upper-cased, reference and customer id compared case-insensitively, line order
ignored), then hashed with SHA-256. The customer id is part of the hash, so the same reference for a
different customer is a clash, not a retry. The hash is stored on the order and compared on resubmit, so cosmetic
differences such as `po-1001` vs `PO-1001` or re-ordered lines still count as a retry.

**Why reject a clash instead of returning the existing order?** If a rep reuses a reference by
mistake for a genuinely different order, returning the old order would make the new one quietly
disappear. A `409` that points at the existing order is the safer failure.

**Race safety:** duplicate prevention lives in one atomic operation,
`IOrderRepository.AddIfReferenceAbsentAsync`. The in-memory store does it under a single write
lock. In a database the same contract is a unique index on the normalised reference with
insert-or-read-existing. A test fires 50 concurrent identical submissions and asserts exactly one
order is created.

Invalid submissions don't reserve a reference, so a rep can fix the errors and resubmit.

## Totals and money

- `decimal` throughout. Line total = quantity × unit price, rounded to 2 places with
  `MidpointRounding.AwayFromZero` (commercial rounding, not banker's rounding). Subtotal is the sum
  of the rounded line totals, so the lines on screen always add up to the subtotal.
- `Total` is kept separate from `Subtotal` even though they're equal today (no tax, discount or
  shipping in scope), so those can be added without changing the contract.
- Totals are never accepted from the client. The UI shows an *estimate* while you type and says
  the server's figures are final.
- Quantity is a positive whole number; unit price is ≥ 0 with at most 2 decimal places. Supported
  currencies are ZAR, USD, EUR and GBP, all with 2 minor units, which keeps rounding uniform.

## Status workflow

```
Pending ──► Confirmed ──► Fulfilled (final)
   │            │
   └────────────┴──► Cancelled (final)
```

- The rules live in one place, `OrderStatusRules`. Each disallowed change gets a specific
  explanation ("A Pending order must be Confirmed before it can be Fulfilled", "Fulfilled is a
  final status", "can't go back to Pending").
- A disallowed change returns `422` with the current status and the allowed next statuses.
  The order DTO also carries `allowedTransitions`, so the UI only shows valid buttons and never
  duplicates the rules.
- Every change is recorded in a history with a timestamp and an optional reason.
- **Optimistic concurrency:** each order has a `Version`. The UI sends `expectedVersion`, so if a
  colleague confirmed the order while you were looking at it, your "Cancel" fails with `409`
  instead of overwriting their change. Callers that omit it get a short retry loop that re-checks
  the rules against fresh state.

## Design choices

- **Two backend projects.** `Core` holds the domain, validation, service and repository
  abstraction with no ASP.NET dependency; `Api` is a thin HTTP layer. Business rules are tested
  without spinning up a web host.
- **Immutable order aggregate.** Status changes return a new `Order` with `Version + 1`. Readers
  never see a half-updated order, and the store can do a compare-and-swap rather than locking reads.
- **Expected failures are values.** The service returns `Result<T>` with a typed `ErrorKind`; one
  function maps those to problem-details status codes. Exceptions are kept for genuine faults.
- **Validation reports every problem at once** with field paths such as `items[2].quantity`, which
  the UI shows next to the right input. Request DTOs are deliberately loose (nullable, `decimal`
  quantity) so that `1.5` gets a clear "must be a whole number" message rather than a generic
  JSON binding error.
- **`TimeProvider`** is injected so tests control the clock (newest-first ordering, timestamps).
- **Newest first** sorts by `CreatedAt`, then by an insertion sequence as a tie-breaker, so orders
  created in the same instant still sort correctly. Status changes don't reorder the list.
- **Minimal APIs** with endpoint groups and OpenAPI metadata keep the HTTP surface small and readable.
- **Angular:** standalone components, signals and zoneless change detection. A small signal store
  holds the list and selection. Typed reactive forms with a `FormArray` handle line items. Client-
  side status filter and search, as the brief suggested; the API supports the same filters
  server-side for when the list grows.
- The submit button is disabled while a request is in flight, which stops double clicks at the
  source. The server-side handling is still what guarantees no duplicates.
- Browsers can put text into a field without firing the `input` event Angular listens to (restoring
  form values after a reload, some autofill paths). The field then looks filled while its control is
  empty, and validation says "Required". A small directive (`SyncDomValueDirective`) re-reads each
  field on `change`/`focusout`, the form re-syncs every field before validating a submit, and the
  form sets `autocomplete="off"` so stale values aren't restored.

## Trade-offs and what I'd do next

| Choice | Why | Next step for production |
|---|---|---|
| In-memory storage | Zero setup, as the brief recommended | EF Core + SQL Server/PostgreSQL behind the existing `IOrderRepository`, with a unique index on the normalised reference and a row-version column |
| Reference is unique globally | Simplest reading of "client-provided reference" | Scope it per customer or per tenant if different customers can share PO numbers |
| Customer (id, email, name) is stored on the order; the id is taken as supplied | No customer directory in scope, so the id isn't checked against one | A customers table, with the id validated against it and orders referencing it |
| No authentication | Internal tool, time-boxed | Entra ID / OIDC, with the acting user recorded in the status history |
| `Total = Subtotal` | No tax rules given | VAT and discounts as explicit, tested calculations |
| Client-side filtering in the UI | Fine for hundreds of orders | Use the API's `status`, `search` and paging parameters |
| Frontend tests cover the API client, store and form logic only | Optional in the brief; the business rules live on the server | Component rendering tests and an end-to-end test of the submit-and-retry flow |
| Idempotency forever | The order *is* the record of the reference | Fine as is. A separate `Idempotency-Key` with expiry would only be needed for non-order operations |

## Tests

`dotnet test` runs 72 backend tests:

- **Totals:** line totals, subtotal, zero-priced lines, midpoint rounding, large values
- **Validation:** positive whole quantities, non-negative prices with 2 decimals, required fields
  (including customer id),
  duplicate SKUs, email, currency and reference format, all errors reported together
- **Duplicates:** replay returns the original, cosmetic differences still match, a clash (including the same reference for a different
  customer) is rejected and points at the existing order, replay reflects the current status, 50 concurrent
  submits create one order, invalid submissions don't reserve the reference
- **Status:** the full 4×4 transition matrix, the explanation messages, history and versioning,
  stale `expectedVersion`, unknown statuses, unknown orders
- **Listing:** newest first, same-instant tie-break, status changes don't reorder, filter and paging
- **HTTP contract:** 201 then 200 with the replay header, 409 problem, 400 field errors, 422 with
  allowed transitions, 404

`npm test` in `web/` runs 15 Angular tests (Vitest):

- **API client:** 201 is a new order, 200 + `Idempotent-Replayed` is a resubmit, a 409 keeps its
  details, an unreachable server becomes a friendly message, status changes send the expected version
- **Store:** newest-first order kept, status filter, case-insensitive search across reference and
  customer fields, filter + search combined, counts per status
- **Order form:** no API call while invalid, the replay and reused-reference banners, server field errors
  mapped to their inputs, the request trimmed before sending

GitHub Actions runs both suites and the production build on every push.
