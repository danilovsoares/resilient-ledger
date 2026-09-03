import { DecimalPipe, DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CashDeskStateService } from '../../core/cash-desk-state.service';
import { DailyBalanceApiService } from '../../core/api/daily-balance-api.service';
import { DailyBalance } from '../../core/api/models';

/** Instantes (ms) em que reconsultamos o saldo após um novo lançamento, para acomodar a
 * consistência eventual (Outbox -> RabbitMQ -> Worker) sem prometer atualização instantânea —
 * ver docs/architecture/05-fluxos-principais.md. */
const CATCH_UP_DELAYS_MS = [1000, 2500, 5000];

@Component({
  selector: 'app-daily-balance-panel',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe],
  templateUrl: './daily-balance-panel.component.html',
  styleUrl: './daily-balance-panel.component.css',
})
export class DailyBalancePanelComponent {
  private readonly dailyBalanceApi = inject(DailyBalanceApiService);
  protected readonly state = inject(CashDeskStateService);

  protected readonly balance = signal<DailyBalance | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect((onCleanup) => {
      const date = this.state.selectedDate();
      this.state.refreshTick();

      this.refresh(date);
      const handles = CATCH_UP_DELAYS_MS.map((delay) => setTimeout(() => this.refresh(date), delay));

      // Cancela os reagendamentos pendentes se a data/tick mudar de novo antes deles
      // dispararem, ou se o componente for destruído — evita chamadas a um estado obsoleto.
      onCleanup(() => handles.forEach(clearTimeout));
    });
  }

  protected refresh(date: string = this.state.selectedDate()): void {
    this.loading.set(true);
    this.dailyBalanceApi.getByDate(date).subscribe({
      next: (result) => {
        this.balance.set(result);
        this.loading.set(false);
        this.error.set(null);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Falha ao consultar o saldo diário.');
      },
    });
  }
}
