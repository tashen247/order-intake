import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiProblem, Order } from './order.models';
import { OrderFormComponent } from './order-form.component';
import { OrdersApi } from './orders.api';

describe('OrderFormComponent', () => {
  const order = { id: 'o-1', externalReference: 'PO-1001', status: 'Pending' } as Order;
  let submit: ReturnType<typeof vi.fn>;

  function create() {
    TestBed.configureTestingModule({
      imports: [OrderFormComponent],
      providers: [{ provide: OrdersApi, useValue: { submit } }],
    });
    const fixture = TestBed.createComponent(OrderFormComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    submit = vi.fn();
  });

  it('does not call the API while the form is invalid', () => {
    const form = create();
    form.submit();

    expect(submit).not.toHaveBeenCalled();
    expect(form.banner()?.kind).toBe('error');
  });

  it('shows the "already submitted" banner when the server reports a replay', () => {
    submit.mockReturnValue(of({ order, replayed: true }));
    const form = create();
    form.fillSample();

    form.submit();

    expect(submit).toHaveBeenCalledOnce();
    expect(form.banner()).toMatchObject({ kind: 'info', title: 'PO-1001 was already submitted' });
  });

  it('shows a reused reference as a warning with a link to the existing order', () => {
    const problem: ApiProblem = {
      status: 409,
      title: 'Reference already used',
      code: 'duplicate_reference',
      existingOrderId: 'o-1',
    };
    submit.mockReturnValue(throwError(() => problem));
    const form = create();
    form.fillSample();

    form.submit();

    expect(form.banner()).toMatchObject({ kind: 'warning', existingOrderId: 'o-1' });
  });

  it('maps server field errors to the inputs they belong to', () => {
    const problem: ApiProblem = {
      status: 400,
      title: 'Some details need fixing',
      errors: { 'items[0].quantity': ['Quantity must be a whole number.'] },
    };
    submit.mockReturnValue(throwError(() => problem));
    const form = create();
    form.fillSample();

    form.submit();

    expect(form.fieldError('items[0].quantity')).toBe('Quantity must be a whole number.');
    expect(form.fieldError('customer.email')).toBeNull();
  });

  it('sends a trimmed request built from the form', () => {
    submit.mockReturnValue(of({ order, replayed: false }));
    const form = create();
    form.fillSample();
    form.form.controls.customer.controls.name.setValue('  Acme  ');

    form.submit();

    const sent = submit.mock.calls[0][0];
    expect(sent.customer).toEqual({ id: 'CUST-0042', name: 'Acme', email: 'procurement@acme.co.za' });
    expect(sent.items).toHaveLength(3);
  });
});
