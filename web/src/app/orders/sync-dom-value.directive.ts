import { Directive, ElementRef, HostListener, inject } from '@angular/core';
import { NgControl } from '@angular/forms';

/**
 * Keeps a reactive-form control in step with what the input actually shows.
 *
 * Browsers can put text into a field without firing the `input` event Angular listens to:
 * restoring form values after a reload or back-navigation, and some autofill paths in
 * Chrome and Edge. The field then looks filled while the control is still empty, and
 * validation reports "Required" on a visibly filled field. This directive re-reads the
 * element on `change` and `focusout`, and the form calls `sync()` on every field just
 * before it validates a submit.
 */
@Directive({
  selector: 'input[formControlName], textarea[formControlName], select[formControlName]',
})
export class SyncDomValueDirective {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly ngControl = inject(NgControl);

  @HostListener('change')
  @HostListener('focusout')
  sync(): void {
    const control = this.ngControl.control;
    if (!control) return;

    const raw = this.element.value;
    const next = this.element.type === 'number' ? (raw === '' ? null : Number(raw)) : raw;

    if (String(next ?? '') !== String(control.value ?? '')) {
      control.setValue(next);
      control.markAsDirty();
    }
  }
}
