import { Injectable, signal } from '@angular/core';

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Estado compartilhado entre o painel de lançamentos e o painel de saldo: a data de negócio
 * selecionada, e um "tick" que os componentes de saldo observam para se atualizar depois de
 * um novo lançamento — sem apostar em consistência instantânea (o efeito no saldo é
 * assíncrono, ver docs/architecture/05-fluxos-principais.md).
 */
@Injectable({ providedIn: 'root' })
export class CashDeskStateService {
  readonly selectedDate = signal(todayIsoDate());
  readonly refreshTick = signal(0);

  notifyTransactionRegistered(): void {
    this.refreshTick.update((tick) => tick + 1);
  }
}
