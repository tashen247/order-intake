/**
 * OrderFormComponent: the "New order" form.
 *
 * A typed reactive form with a FormArray of line items and a live estimated total (the server's
 * total is the one that counts). On submit it shows one of three outcomes: created, already
 * submitted (an identical resubmit, no duplicate created), or reference already used for
 * different details (409, with a link to the existing order). Server field errors such as
 * "items[0].quantity" are shown next to the matching input. "Fill sample" and "simulate a retry"
 * exist to make the demo cases quick to show.
 */

import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, output, signal, viewChildren } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CURRENCIES, ApiProblem, CreateOrderRequest, Order } from './order.models';
import { OrdersApi } from './orders.api';
import { SyncDomValueDirective } from './sync-dom-value.directive';

type ItemGroup = FormGroup<{
  sku: FormControl<string>;
  name: FormControl<string>;
  quantity: FormControl<number | null>;
  unitPrice: FormControl<number | null>;
}>;

interface Banner {
  kind: 'success' | 'info' | 'warning' | 'error';
  title: string;
  detail?: string;
  existingOrderId?: string;
}

@Component({
  selector: 'app-order-form',
  imports: [ReactiveFormsModule, CurrencyPipe, SyncDomValueDirective],
  templateUrl: './order-form.component.html',
})
export class OrderFormComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly api = inject(OrdersApi);

  private readonly fields = viewChildren(SyncDomValueDirective);

  readonly submitted = output<Order>();
  readonly openOrder = output<string>();

  readonly currencies = CURRENCIES;
  readonly submitting = signal(false);
  readonly banner = signal<Banner | null>(null);
  /** Field errors from the server, keyed by the API's paths, e.g. "items[0].quantity". */
  readonly serverErrors = signal<Record<string, string[]>>({});
  /** The last request that reached the server, kept so a retry can be demonstrated. */
  readonly lastRequest = signal<CreateOrderRequest | null>(null);

  readonly form = this.fb.group({
    externalReference: ['', [Validators.required, Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9\-_./#]*$/)]],
    customer: this.fb.group({
      id: ['', [Validators.required, Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9\-_.]*$/)]],
      name: ['', [Validators.required, Validators.maxLength(200)]],
      email: ['', [Validators.required, Validators.email]],
    }),
    currency: ['ZAR', Validators.required],
    notes: [''],
    items: this.fb.array<ItemGroup>([this.newItem()]),
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** Live preview only; the server's figures are the ones that count. */
  readonly estimate = computed(() =>
    (this.value().items ?? []).reduce((sum, i) => {
      const q = Number(i?.quantity), p = Number(i?.unitPrice);
      return Number.isFinite(q) && Number.isFinite(p) && q > 0 && p >= 0 ? sum + Math.round(q * p * 100) / 100 : sum;
    }, 0),
  );

  get items(): FormArray<ItemGroup> {
    return this.form.controls.items;
  }

  newItem(): ItemGroup {
    return this.fb.group({
      sku: this.fb.control('', [Validators.required, Validators.maxLength(64)]),
      name: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
      quantity: this.fb.control<number | null>(1, [Validators.required, Validators.min(1), Validators.pattern(/^\d+$/)]),
      unitPrice: this.fb.control<number | null>(null, [Validators.required, Validators.min(0)]),
    });
  }

  addItem(): void {
    this.items.push(this.newItem());
  }

  removeItem(index: number): void {
    if (this.items.length > 1) this.items.removeAt(index);
  }

  lineTotal(index: number): number | null {
    const { quantity, unitPrice } = this.items.at(index).getRawValue();
    return quantity && unitPrice != null && quantity > 0 && unitPrice >= 0 ? Math.round(quantity * unitPrice * 100) / 100 : null;
  }

  /** Handy for the demo: a realistic order with a fresh reference. */
  fillSample(): void {
    const n = Math.floor(1000 + Math.random() * 9000);
    const lines = [
      { sku: 'LAP-14-PRO', name: 'Laptop 14" Pro', quantity: 2, unitPrice: 18999.99 },
      { sku: 'DOCK-USB-C', name: 'USB-C Dock', quantity: 2, unitPrice: 1499.5 },
      { sku: 'WARR-3Y', name: '3-year warranty', quantity: 2, unitPrice: 0 },
    ];
    this.resizeItems(lines.length);
    lines.forEach((line, i) => this.items.at(i).setValue(line));
    this.form.patchValue({
      externalReference: `PO-${n}`,
      customer: { id: 'CUST-0042', name: 'Acme Trading (Pty) Ltd', email: 'procurement@acme.co.za' },
      currency: 'ZAR',
      notes: 'Deliver to Sandton office, reception.',
    });
    this.banner.set(null);
    this.serverErrors.set({});
  }

  fieldError(path: string): string | null {
    return this.serverErrors()[path]?.[0] ?? null;
  }

  submit(): void {
    if (this.submitting()) return; // the button is disabled too; this guards Enter-key double submits
    // Pick up any text the browser restored or autofilled without telling Angular.
    this.fields().forEach((field) => field.sync());
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.banner.set({ kind: 'error', title: 'Some details need fixing', detail: 'Check the highlighted fields.' });
      return;
    }

    this.send(this.buildRequest());
  }

  /** Re-sends exactly the last submission, as a rep unsure whether it went through would. */
  resubmitLast(): void {
    const last = this.lastRequest();
    if (last && !this.submitting()) this.send(last);
  }

  private buildRequest(): CreateOrderRequest {
    const v = this.form.getRawValue();
    const request: CreateOrderRequest = {
      externalReference: v.externalReference.trim(),
      customer: { id: v.customer.id.trim(), name: v.customer.name.trim(), email: v.customer.email.trim() },
      currency: v.currency,
      notes: v.notes.trim() || null,
      items: v.items.map((i) => ({ sku: i.sku.trim(), name: i.name.trim(), quantity: i.quantity, unitPrice: i.unitPrice })),
    };
    return request;
  }

  private send(request: CreateOrderRequest): void {
    this.submitting.set(true);
    this.lastRequest.set(request);
    this.serverErrors.set({});
    this.api.submit(request).subscribe({
      next: ({ order, replayed }) => {
        this.submitting.set(false);
        this.banner.set(
          replayed
            ? {
                kind: 'info',
                title: `${order.externalReference} was already submitted`,
                detail: 'No duplicate was created. Showing the existing order.',
              }
            : { kind: 'success', title: `${order.externalReference} submitted`, detail: 'The order is Pending.' },
        );
        this.resetForm();
        this.submitted.emit(order);
      },
      error: (p: ApiProblem) => {
        this.submitting.set(false);
        this.serverErrors.set(p.errors ?? {});
        this.banner.set({
          kind: p.code === 'duplicate_reference' ? 'warning' : 'error',
          title: p.title || 'Submission failed',
          detail: p.detail,
          existingOrderId: p.existingOrderId,
        });
      },
    });
  }

  private resetForm(): void {
    this.resizeItems(1);
    this.form.reset({ currency: 'ZAR' });
  }

  /**
   * Grows or shrinks the line-item array to `count` rows, keeping the existing row objects.
   * Replacing rows would make the @for loop (which tracks rows by identity) destroy and
   * rebuild their DOM, so rows are reused and only their values change.
   */
  private resizeItems(count: number): void {
    while (this.items.length > count) this.items.removeAt(this.items.length - 1);
    while (this.items.length < count) this.items.push(this.newItem());
  }
}
