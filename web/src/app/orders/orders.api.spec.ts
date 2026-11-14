import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiProblem, CreateOrderRequest, Order } from './order.models';
import { OrdersApi, SubmitResult } from './orders.api';

const request: CreateOrderRequest = {
  externalReference: 'PO-1001',
  customer: { id: 'CUST-0042', email: 'buyer@acme.co.za', name: 'Acme' },
  currency: 'ZAR',
  notes: null,
  items: [{ sku: 'SKU-A', name: 'Widget', quantity: 2, unitPrice: 10.5 }],
};

const order = { id: 'o-1', externalReference: 'PO-1001', status: 'Pending', version: 1 } as Order;

describe('OrdersApi', () => {
  let api: OrdersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(OrdersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('treats 201 Created as a new order', () => {
    let result: SubmitResult | undefined;
    api.submit(request).subscribe((r) => (result = r));

    const req = http.expectOne('/api/orders');
    expect(req.request.method).toBe('POST');
    req.flush(order, { status: 201, statusText: 'Created' });

    expect(result).toEqual({ order, replayed: false });
  });

  it('treats 200 with Idempotent-Replayed as a resubmit of an existing order', () => {
    let result: SubmitResult | undefined;
    api.submit(request).subscribe((r) => (result = r));

    http.expectOne('/api/orders').flush(order, {
      status: 200,
      statusText: 'OK',
      headers: { 'Idempotent-Replayed': 'true' },
    });

    expect(result?.replayed).toBe(true);
  });

  it('passes a 409 duplicate-reference problem through with its details', () => {
    let problem: ApiProblem | undefined;
    api.submit(request).subscribe({ error: (p: ApiProblem) => (problem = p) });

    http.expectOne('/api/orders').flush(
      { title: 'Reference already used', detail: 'Use a new reference.', code: 'duplicate_reference', existingOrderId: 'o-1' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(problem).toMatchObject({ status: 409, code: 'duplicate_reference', existingOrderId: 'o-1' });
  });

  it('turns an unreachable server into a friendly problem', () => {
    let problem: ApiProblem | undefined;
    api.list().subscribe({ error: (p: ApiProblem) => (problem = p) });

    http.expectOne('/api/orders?pageSize=100').error(new ProgressEvent('error'), { status: 0 });

    expect(problem?.status).toBe(0);
    expect(problem?.title).toBe("Can't reach the server");
  });

  it('sends the version the screen last saw when changing status', () => {
    api.changeStatus('o-1', 'Confirmed', 3, '').subscribe();

    const req = http.expectOne('/api/orders/o-1/status');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Confirmed', expectedVersion: 3, reason: null });
    req.flush(order);
  });
});
