/**
 * App shell: the page layout. Order form on the left; on the right the selected order's detail
 * above the order list. Loads the list on start and refreshes it after any submit or change.
 */

import { Component, OnInit, inject } from '@angular/core';
import { OrderDetailComponent } from './orders/order-detail.component';
import { OrderFormComponent } from './orders/order-form.component';
import { OrderListComponent } from './orders/order-list.component';
import { Order } from './orders/order.models';
import { OrdersStore } from './orders/orders.store';

@Component({
  selector: 'app-root',
  imports: [OrderFormComponent, OrderListComponent, OrderDetailComponent],
  template: `
    <header class="top">
      <h1>Order Intake</h1>
      <span class="muted">Record purchase orders and track their status</span>
    </header>
    <main class="layout">
      <div class="col">
        <app-order-form (submitted)="onSaved($event)" (openOrder)="store.select($event)" />
      </div>
      <div class="col">
        @if (store.selected(); as order) {
          <app-order-detail [order]="order" (changed)="onSaved($event)" (closed)="store.clearSelection()" />
        }
        <app-order-list (selected)="store.select($event)" />
      </div>
    </main>
  `,
})
export class App implements OnInit {
  readonly store = inject(OrdersStore);

  ngOnInit(): void {
    this.store.refresh();
  }

  onSaved(order: Order): void {
    this.store.upsert(order);
  }
}
