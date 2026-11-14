/**
 * OrderDetailComponent: one order's line items, totals, status history and status actions.
 *
 * The status buttons come from the server's allowedTransitions, so the UI never duplicates the
 * workflow rules. Each change sends the order's version; a 409 means someone changed it first,
 * a 422 means the rules don't allow that change, and both are shown with the server's message.
 * "Try an invalid change" is a demo aid that asks the API for a disallowed change on purpose.
 */

import { CurrencyPipe, DatePipe, LowerCasePipe } from '@angular/common';
import { Component, effect, inject, input, output, signal } from '@angular/core';
import { ApiProblem, Order, OrderStatus } from './order.models';
import { OrdersApi } from './orders.api';

const ACTION_LABEL: Record<OrderStatus, string> = {
  Pending: 'Back to Pending',
  Confirmed: 'Confirm',
  Fulfilled: 'Mark fulfilled',
  Cancelled: 'Cancel order',
};

@Component({
  selector: 'app-order-detail',
  imports: [CurrencyPipe, DatePipe, LowerCasePipe],
  template: `
    @let o = order();
    <section class="card detail">
      <header class="card-head">
        <h2>
          <span class="mono">{{ o.externalReference }}</span>
          <span class="status {{ o.status | lowercase }}">{{ o.status }}</span>
        </h2>
        <button type="button" class="icon" (click)="closed.emit()" aria-label="Close">×</button>
      </header>

      <dl class="meta">
        <div><dt>Customer</dt><dd>{{ o.customer.name }} · {{ o.customer.email }} <small class="muted mono">{{ o.customer.id }}</small></dd></div>
        <div><dt>Submitted</dt><dd>{{ o.createdAt | date: 'medium' }}</dd></div>
        <div><dt>Last updated</dt><dd>{{ o.updatedAt | date: 'medium' }} <small class="muted">v{{ o.version }}</small></dd></div>
        @if (o.notes) { <div><dt>Notes</dt><dd>{{ o.notes }}</dd></div> }
      </dl>

      <table>
        <thead><tr><th>SKU</th><th>Item</th><th class="num">Qty</th><th class="num">Unit price</th><th class="num">Line total</th></tr></thead>
        <tbody>
          @for (i of o.items; track i.sku) {
            <tr>
              <td class="mono">{{ i.sku }}</td><td>{{ i.name }}</td><td class="num">{{ i.quantity }}</td>
              <td class="num">{{ i.unitPrice | currency: o.currency : 'symbol-narrow' }}</td>
              <td class="num">{{ i.lineTotal | currency: o.currency : 'symbol-narrow' }}</td>
            </tr>
          }
        </tbody>
        <tfoot>
          <tr><td colspan="4" class="num">Subtotal</td><td class="num">{{ o.subtotal | currency: o.currency : 'symbol-narrow' }}</td></tr>
          <tr class="total"><td colspan="4" class="num">Total ({{ o.currency }})</td><td class="num">{{ o.total | currency: o.currency : 'symbol-narrow' }}</td></tr>
        </tfoot>
      </table>

      <div class="actions">
        @if (o.allowedTransitions.length) {
          <input placeholder="Reason (optional)" [value]="reason()" (input)="reason.set($any($event.target).value)" aria-label="Reason" />
          @for (s of o.allowedTransitions; track s) {
            <button type="button" [class.danger]="s === 'Cancelled'" [class.primary]="s !== 'Cancelled'"
                    [disabled]="busy()" (click)="change(s)">{{ label[s] }}</button>
          }
        } @else {
          <span class="muted">{{ o.status }} is a final status. No further changes.</span>
        }
        <button type="button" class="link" [disabled]="busy()" (click)="tryInvalid()" title="Ask the API for a change the rules don't allow">
          Try an invalid change
        </button>
      </div>

      @if (problem(); as p) {
        <div class="banner {{ p.status === 409 ? 'warning' : 'error' }}" role="alert">
          <strong>{{ p.title }}</strong> <span>{{ p.detail }}</span>
        </div>
      }

      @if (o.history.length) {
        <h3>History</h3>
        <ol class="history">
          <li><small class="muted">{{ o.createdAt | date: 'd MMM HH:mm:ss' }}</small> Submitted as Pending</li>
          @for (h of o.history; track h.at) {
            <li>
              <small class="muted">{{ h.at | date: 'd MMM HH:mm:ss' }}</small>
              {{ h.from }} → <strong>{{ h.to }}</strong>
              @if (h.reason) { <em>— {{ h.reason }}</em> }
            </li>
          }
        </ol>
      }
    </section>
  `,
})
export class OrderDetailComponent {
  private readonly api = inject(OrdersApi);

  readonly order = input.required<Order>();
  readonly changed = output<Order>();
  readonly closed = output<void>();

  readonly label = ACTION_LABEL;
  readonly busy = signal(false);
  readonly reason = signal('');
  readonly problem = signal<ApiProblem | null>(null);

  constructor() {
    // Clear stale feedback when a different order (or a newer version) is shown.
    effect(() => {
      this.order();
      this.problem.set(null);
      this.reason.set('');
    });
  }

  change(status: OrderStatus): void {
    const o = this.order();
    this.busy.set(true);
    // expectedVersion makes a stale screen fail loudly instead of silently overwriting a colleague's change.
    this.api.changeStatus(o.id, status, o.version, this.reason()).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.changed.emit(updated);
      },
      error: (p: ApiProblem) => {
        this.busy.set(false);
        this.problem.set(p);
      },
    });
  }

  /** Demo aid: pick a status the rules don't allow from here and show the API's explanation. */
  tryInvalid(): void {
    const o = this.order();
    const target: OrderStatus =
      o.status === 'Pending' ? 'Fulfilled' : o.status === 'Confirmed' ? 'Pending' : 'Confirmed';
    this.change(target);
  }
}
