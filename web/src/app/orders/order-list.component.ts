/**
 * OrderListComponent: the order list, newest first.
 *
 * Status filter chips with counts and a free-text search over reference, customer id, name and
 * email, all read from OrdersStore. Clicking a row (or pressing Enter on it) opens that order.
 */

import { CurrencyPipe, DatePipe, LowerCasePipe } from '@angular/common';
import { Component, inject, output } from '@angular/core';
import { ORDER_STATUSES, OrderStatus } from './order.models';
import { OrdersStore } from './orders.store';

@Component({
  selector: 'app-order-list',
  imports: [CurrencyPipe, DatePipe, LowerCasePipe],
  template: `
    <section class="card">
      <header class="card-head">
        <h2>Orders <small class="muted">newest first</small></h2>
        <button type="button" class="link" (click)="store.refresh()" [disabled]="store.loading()">
          {{ store.loading() ? 'Loading…' : 'Refresh' }}
        </button>
      </header>

      <div class="filters">
        <div class="chips" role="group" aria-label="Filter by status">
          @for (s of filters; track s) {
            <button type="button" class="chip" [class.on]="store.statusFilter() === s" (click)="store.statusFilter.set(s)">
              {{ s }} <span class="count">{{ store.counts()[s] ?? 0 }}</span>
            </button>
          }
        </div>
        <input type="search" placeholder="Search reference or customer" [value]="store.search()"
               (input)="store.search.set($any($event.target).value)" aria-label="Search orders" />
      </div>

      @if (store.listError(); as e) {
        <div class="banner error"><strong>{{ e.title }}</strong> <span>{{ e.detail }}</span></div>
      }

      @if (store.filtered().length === 0) {
        <p class="empty muted">
          {{ store.orders().length === 0 ? 'No orders yet. Submit one, or use "Fill sample".' : 'No orders match the filter.' }}
        </p>
      } @else {
        <table>
          <thead>
            <tr><th>Reference</th><th>Customer</th><th class="num">Units</th><th class="num">Total</th><th>Status</th><th>Submitted</th></tr>
          </thead>
          <tbody>
            @for (o of store.filtered(); track o.id) {
              <tr (click)="selected.emit(o.id)" [class.sel]="store.selectedId() === o.id" tabindex="0" (keydown.enter)="selected.emit(o.id)">
                <td class="mono">{{ o.externalReference }}</td>
                <td>{{ o.customerName }}<br /><small class="muted"><span class="mono">{{ o.customerId }}</span> · {{ o.customerEmail }}</small></td>
                <td class="num">{{ o.itemCount }}</td>
                <td class="num">{{ o.total | currency: o.currency : 'symbol-narrow' }}</td>
                <td><span class="status {{ o.status | lowercase }}">{{ o.status }}</span></td>
                <td><small>{{ o.createdAt | date: 'd MMM, HH:mm:ss' }}</small></td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
})
export class OrderListComponent {
  readonly store = inject(OrdersStore);
  readonly selected = output<string>();
  readonly filters: (OrderStatus | 'All')[] = ['All', ...ORDER_STATUSES];
}
