import { Directive, ElementRef, HostListener, Renderer2, forwardRef, inject } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

const CURRENCY_FORMATTER = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
  minimumFractionDigits: 2,
});

/**
 * Máscara de dinheiro (R$ 1.234,56): os dígitos entram da direita para a esquerda, como nos
 * inputs de valor mais comuns em apps brasileiros (Nubank, iFood, EncomendaAí). O modelo do
 * FormControl continua sendo um `number` puro (reais, não centavos) — só a representação visual
 * no input é mascarada.
 */
@Directive({
  selector: '[appCurrencyMask]',
  standalone: true,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => CurrencyMaskDirective),
      multi: true,
    },
  ],
})
export class CurrencyMaskDirective implements ControlValueAccessor {
  private readonly el = inject(ElementRef<HTMLInputElement>);
  private readonly renderer = inject(Renderer2);

  private onChange: (value: number) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: number | null): void {
    this.setDisplay(value ?? 0);
  }

  registerOnChange(fn: (value: number) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.renderer.setProperty(this.el.nativeElement, 'disabled', isDisabled);
  }

  @HostListener('input')
  protected onInput(): void {
    const digitsOnly = this.el.nativeElement.value.replace(/\D/g, '');
    const numericValue = digitsOnly ? Number(digitsOnly) / 100 : 0;
    this.setDisplay(numericValue);
    this.onChange(numericValue);
  }

  @HostListener('blur')
  protected onBlur(): void {
    this.onTouched();
  }

  private setDisplay(value: number): void {
    this.renderer.setProperty(this.el.nativeElement, 'value', CURRENCY_FORMATTER.format(value));
  }
}
