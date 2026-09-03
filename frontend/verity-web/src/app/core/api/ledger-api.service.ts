import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult, RegisterTransactionRequest, Transaction } from './models';

@Injectable({ providedIn: 'root' })
export class LedgerApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.ledgerApiUrl}/api/v1/transactions`;

  register(request: RegisterTransactionRequest, idempotencyKey: string): Observable<Transaction> {
    return this.http.post<Transaction>(this.baseUrl, request, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  getByDate(date: string, page: number, pageSize: number): Observable<PagedResult<Transaction>> {
    return this.http.get<PagedResult<Transaction>>(this.baseUrl, { params: { date, page, pageSize } });
  }

  cancel(transactionId: string): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.baseUrl}/${transactionId}/cancel`, null);
  }
}
