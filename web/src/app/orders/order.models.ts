/**
 * Shared TypeScript types for the orders feature.
 *
 * These mirror the API's JSON contract: the request sent when submitting an order, the full
 * order returned by GET /api/orders/{id}, the slim summary used in the list, and ApiProblem,
 * the single error shape every failed call is turned into (see orders.api.ts).
 */

export const ORDER_STATUSES = ['Pending', 'Confirmed', 'Fulfilled', 'Cancelled'] as const;
export type OrderStatus = (typeof ORDER_STATUSES)[number];

export const CURRENCIES = ['ZAR', 'USD', 'EUR', 'GBP'] as const;

export interface LineItemInput {
  sku: string;
  name: string;
  quantity: number | null;
  unitPrice: number | null;
}

export interface CreateOrderRequest {
  externalReference: string;
  customer: { id: string; email: string; name: string };
  currency: string;
  notes: string | null;
  items: LineItemInput[];
}

export interface LineItem {
  sku: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface StatusChange {
  from: OrderStatus;
  to: OrderStatus;
  at: string;
  reason: string | null;
}

export interface Order {
  id: string;
  externalReference: string;
  customer: { id: string; email: string; name: string };
  currency: string;
  notes: string | null;
  items: LineItem[];
  subtotal: number;
  total: number;
  status: OrderStatus;
  allowedTransitions: OrderStatus[];
  createdAt: string;
  updatedAt: string;
  version: number;
  history: StatusChange[];
}

export interface OrderSummary {
  id: string;
  externalReference: string;
  customerId: string;
  customerName: string;
  customerEmail: string;
  currency: string;
  itemCount: number;
  total: number;
  status: OrderStatus;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** RFC 7807 problem details as returned by the API, including our extensions. */
export interface ApiProblem {
  status: number;
  title: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
  existingOrderId?: string;
  allowedTransitions?: OrderStatus[];
}
