import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { OrderSummary } from './order.models';
import { OrdersApi } from './orders.api';
import { OrdersStore } from './orders.store';

const summary = (ref: string, status: OrderSummary['status'], customerName: string, customerId = 'CUST-1'): OrderSummary => ({
  id: ref,
  externalReference: ref,
  customerId,
  customerName,
  customerEmail: `${customerName.toLowerCase()}@example.com`,
  currency: 'ZAR',
  itemCount: 1,
  total: 100,
  status,
  createdAt: '2026-11-14T09:00:00Z',
  updatedAt: '2026-11-14T09:00:00Z',
});

const orders = [
  summary('PO-3', 'Fulfilled', 'Acme', 'CUST-0042'),
  summary('PO-2', 'Pending', 'Globex'),
  summary('PO-1', 'Pending', 'Initech'),
];

describe('OrdersStore', () => {
  let store: OrdersStore;

  beforeEach(() => {
    const api = { list: () => of({ items: orders, page: 1, pageSize: 100, totalCount: 3 }) };
    TestBed.configureTestingModule({ providers: [{ provide: OrdersApi, useValue: api }] });
    store = TestBed.inject(OrdersStore);
    store.refresh();
  });

  it('keeps the server order (newest first) when unfiltered', () => {
    expect(store.filtered().map((o) => o.externalReference)).toEqual(['PO-3', 'PO-2', 'PO-1']);
  });

  it('filters by status', () => {
    store.statusFilter.set('Pending');
    expect(store.filtered().map((o) => o.externalReference)).toEqual(['PO-2', 'PO-1']);
  });

  it('searches reference, customer id, name and email, ignoring case', () => {
    store.search.set('cust-0042');
    expect(store.filtered().map((o) => o.externalReference)).toEqual(['PO-3']);

    store.search.set('GLOBEX');
    expect(store.filtered().map((o) => o.externalReference)).toEqual(['PO-2']);

    store.search.set('initech@');
    expect(store.filtered().map((o) => o.externalReference)).toEqual(['PO-1']);
  });

  it('combines the status filter and search', () => {
    store.statusFilter.set('Fulfilled');
    store.search.set('globex');
    expect(store.filtered()).toEqual([]);
  });

  it('counts orders per status for the filter chips', () => {
    expect(store.counts()).toEqual({ All: 3, Fulfilled: 1, Pending: 2 });
  });
});
