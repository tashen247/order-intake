/**
 * OrdersStore: the screen's state, held in Angular signals.
 *
 * Holds the order list, the selected order, loading/error flags and the current status filter
 * and search text. Derived values (the filtered list, the counts on the filter chips, the
 * selected row) are computed signals, so every view that reads them updates on its own when
 * the underlying state changes. Components call refresh(), select() and upsert() rather than
 * calling the API directly.
 */

import { Injectable, computed, inject, signal } from '@angular/core';
import { OrdersApi } from './orders.api';
import { ApiProblem, Order, OrderStatus, OrderSummary } from './order.models';

/** Small signal-based store: one source of truth for the list and the selected order. */
@Injectable({ providedIn: 'root' })
export class OrdersStore {
  private readonly api = inject(OrdersApi);

  readonly orders = signal<OrderSummary[]>([]);
  readonly loading = signal(false);
  readonly listError = signal<ApiProblem | null>(null);

  readonly selected = signal<Order | null>(null);
  readonly selectedId = computed(() => this.selected()?.id ?? null);

  // Client-side filtering (the API supports the same filters server-side).
  readonly statusFilter = signal<OrderStatus | 'All'>('All');
  readonly search = signal('');

  readonly filtered = computed(() => {
    const status = this.statusFilter();
    const term = this.search().trim().toLowerCase();
    return this.orders().filter(
      (o) =>
        (status === 'All' || o.status === status) &&
        (!term ||
          o.externalReference.toLowerCase().includes(term) ||
          o.customerId.toLowerCase().includes(term) ||
          o.customerName.toLowerCase().includes(term) ||
          o.customerEmail.toLowerCase().includes(term)),
    );
  });

  readonly counts = computed(() => {
    const counts: Partial<Record<string, number>> = { All: this.orders().length };
    for (const o of this.orders()) counts[o.status] = (counts[o.status] ?? 0) + 1;
    return counts;
  });

  refresh(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (page) => {
        this.orders.set(page.items);
        this.listError.set(null);
        this.loading.set(false);
      },
      error: (p: ApiProblem) => {
        this.listError.set(p);
        this.loading.set(false);
      },
    });
  }

  select(id: string): void {
    this.api.get(id).subscribe({ next: (o) => this.selected.set(o) });
  }

  /** Called after a submit or status change so the list and detail agree without a full reload. */
  upsert(order: Order): void {
    this.selected.set(order);
    this.refresh();
  }

  clearSelection(): void {
    this.selected.set(null);
  }
}
