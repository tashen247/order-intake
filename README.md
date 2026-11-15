# Order Intake & Tracking

A small internal app for recording customer purchase orders and tracking their status.
Resubmitting an order with the same client reference never creates a duplicate.

- **Backend:** ASP.NET Core 8 (minimal APIs), in-memory store, xUnit tests
- **Frontend:** Angular 21 (standalone components, signals, zoneless), TypeScript 5.9

See [SOLUTION.md](SOLUTION.md) for the design and the trade-offs.

**Video walkthrough** (two parts): [Part 1](https://www.loom.com/share/770af2b660d14c47ab8ccd5788d8fb9e) · [Part 2](https://www.loom.com/share/6d8da4aee79d4fafbe48fcd2e3185157)

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 8.0 or later |
| Node.js | 20.19+ or 22.12+ |
| npm | 10+ |

## Run locally

Open two terminals from the repository root.

**1. API** (http://localhost:5080, Swagger at http://localhost:5080/swagger)

```bash
cd src/OrderIntake.Api
dotnet run
```

**2. Web app** (http://localhost:4200)

```bash
cd web
npm install
npm start
```

The Angular dev server proxies `/api` to `http://localhost:5080` (see `web/proxy.conf.json`),
so no CORS setup is needed. Open http://localhost:4200 and click **Fill sample** for a ready-made order.

> Storage is in memory, so orders are cleared when the API restarts.

## Run the tests

```bash
dotnet test
```

72 tests cover totals and rounding, validation, duplicate handling (including 50 concurrent submits of
the same order), the status rules, listing order and paging, and the HTTP contract.

```bash
cd web
npm test -- --watch=false
```

15 Angular tests (Vitest) cover how the API client recognises a resubmit, the store's status filter and
search, and how the order form reports each kind of server response.

Both suites, plus the Angular production build, run on every push via GitHub Actions
(`.github/workflows/ci.yml`).

## API at a glance

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/orders` | Submit an order. `201 Created` when new; `200 OK` + `Idempotent-Replayed: true` when the same reference and the same details were already submitted; `409` when the reference is already used for different details. |
| `GET` | `/api/orders?status=&search=&page=&pageSize=` | List orders, newest first. |
| `GET` | `/api/orders/{id}` | One order with line items, totals, allowed next statuses and history. |
| `PATCH` | `/api/orders/{id}/status` | Change status. Body: `{ "status": "Confirmed", "expectedVersion": 1, "reason": "optional" }`. `422` with an explanation when the change isn't allowed; `409` when `expectedVersion` is stale. |

Errors use RFC 7807 problem details (`application/problem+json`), with field-level `errors`
for validation failures and a stable `code` for the others.

### Example

```powershell
$body = @{
  externalReference = "PO-1001"
  customer = @{ id = "CUST-0042"; email = "buyer@acme.co.za"; name = "Acme Trading" }
  currency = "ZAR"
  items = @(
    @{ sku = "SKU-A"; name = "Widget"; quantity = 3; unitPrice = 19.99 },
    @{ sku = "SKU-B"; name = "Gadget"; quantity = 1; unitPrice = 250 }
  )
} | ConvertTo-Json -Depth 5

Invoke-WebRequest -Uri http://localhost:5080/api/orders -Method Post `
  -ContentType "application/json" -Body $body |
  Select-Object StatusCode, @{n="Replayed";e={$_.Headers["Idempotent-Replayed"]}}
```

Run it twice in PowerShell: the first call returns `201`, the second `200` with `Replayed` = `true` and the same order.

## Project layout

```
src/
  OrderIntake.Core/    domain (Order, status rules, money), validation, service, repository
  OrderIntake.Api/     HTTP endpoints, problem-details mapping, DI wiring
tests/
  OrderIntake.Tests/   unit tests + a few HTTP contract tests
web/                   Angular app
  src/app/orders/      models, API client, signal store, form, list and detail components
```
