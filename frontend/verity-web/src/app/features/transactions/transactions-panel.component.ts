import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { CashDeskStateService } from '../../core/cash-desk-state.service';
import { LedgerApiService } from '../../core/api/ledger-api.service';
import { ProblemDetails, Transaction, TransactionType } from '../../core/api/models';
import { CurrencyMaskDirective } from '../../core/directives/currency-mask.directive';
import { ConfirmDialogComponent } from '../../core/ui/confirm-dialog.component';

const AMOUNT_FORMATTER = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2 });

function nowForDatetimeLocal(): string {
  const now = new Date();
  now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
  return now.toISOString().slice(0, 16);
}

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
}

@Component({
  selector: 'app-transactions-panel',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, DecimalPipe, DatePipe, CurrencyMaskDirective, ConfirmDialogComponent],
  templateUrl: './transactions-panel.component.html',
  styleUrl: './transactions-panel.component.css',
})
export class TransactionsPanelComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ledgerApi = inject(LedgerApiService);
  protected readonly state = inject(CashDeskStateService);

  protected readonly TransactionType = TransactionType;
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly transactions = signal<Transaction[]>([]);
  protected readonly loadingList = signal(false);

  /** Lançamento com a confirmação de estorno aberta no modal — null quando o modal está fechado. */
  protected readonly pendingCancel = signal<Transaction | null>(null);
  protected readonly cancellingId = signal<string | null>(null);
  protected readonly cancelError = signal<string | null>(null);

  protected readonly pendingCancelMessage = computed(() => {
    const transaction = this.pendingCancel();
    if (!transaction) {
      return '';
    }

    const kind = transaction.type === TransactionType.Credit ? 'crédito' : 'débito';
    const amount = AMOUNT_FORMATTER.format(transaction.amount);
    const description = transaction.description ? ` (${transaction.description})` : '';

    return `Confirma o estorno deste ${kind} de R$ ${amount}${description}? Um novo lançamento reverso será registrado — o original não será apagado.`;
  });

  protected readonly isToday = computed(() => this.state.selectedDate() === todayIsoDate());

  protected readonly pageSize = 10;
  protected readonly page = signal(1);
  protected readonly totalPages = signal(1);

  protected readonly form = this.fb.nonNullable.group({
    type: [TransactionType.Credit, Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    occurredAt: [nowForDatetimeLocal(), Validators.required],
    description: [''],
  });

  constructor() {
    effect(() => {
      const date = this.state.selectedDate();
      const page = this.page();
      this.state.refreshTick();
      this.loadTransactions(date, page);
    });

    effect(() => {
      this.state.selectedDate();
      this.page.set(1);
    });
  }

  protected previousPage(): void {
    this.page.update((current) => Math.max(1, current - 1));
  }

  protected nextPage(): void {
    this.page.update((current) => Math.min(this.totalPages(), current + 1));
  }

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const raw = this.form.getRawValue();
    const idempotencyKey = crypto.randomUUID();

    this.ledgerApi
      .register(
        {
          type: raw.type,
          amount: raw.amount,
          occurredAt: new Date(raw.occurredAt).toISOString(),
          description: raw.description || null,
        },
        idempotencyKey,
      )
      .subscribe({
        next: (transaction) => {
          this.submitting.set(false);
          this.form.patchValue({ amount: 0, description: '', occurredAt: nowForDatetimeLocal() });
          this.state.selectedDate.set(transaction.businessDate);
          this.state.notifyTransactionRegistered();
        },
        error: (err) => {
          this.submitting.set(false);
          this.errorMessage.set(this.extractErrorMessage(err));
        },
      });
  }

  protected goToToday(): void {
    this.state.selectedDate.set(todayIsoDate());
  }

  protected requestCancel(transaction: Transaction): void {
    this.cancelError.set(null);
    this.pendingCancel.set(transaction);
  }

  protected dismissCancel(): void {
    this.pendingCancel.set(null);
  }

  protected confirmCancel(): void {
    const transaction = this.pendingCancel();
    if (!transaction) {
      return;
    }

    this.pendingCancel.set(null);
    this.cancellingId.set(transaction.id);
    this.cancelError.set(null);

    this.ledgerApi.cancel(transaction.id).subscribe({
      next: () => {
        this.cancellingId.set(null);
        this.state.notifyTransactionRegistered();
      },
      error: (err) => {
        this.cancellingId.set(null);
        this.cancelError.set(this.extractErrorMessage(err, 'Falha ao estornar o lançamento.'));
      },
    });
  }

  private loadTransactions(date: string, page: number): void {
    this.loadingList.set(true);
    this.ledgerApi.getByDate(date, page, this.pageSize).subscribe({
      next: (result) => {
        this.transactions.set(result.items);
        this.totalPages.set(Math.max(1, result.totalPages));
        this.loadingList.set(false);
      },
      error: () => {
        this.transactions.set([]);
        this.totalPages.set(1);
        this.loadingList.set(false);
      },
    });
  }

  private extractErrorMessage(err: { error?: ProblemDetails }, fallback = 'Falha ao registrar o lançamento.'): string {
    const problem = err.error;
    if (!problem) {
      return fallback;
    }

    if (problem.errors) {
      return Object.values(problem.errors).flat().join(' ');
    }

    return problem.detail ?? problem.title ?? fallback;
  }
}
