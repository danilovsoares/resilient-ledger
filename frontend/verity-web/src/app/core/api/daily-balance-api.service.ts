import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DailyBalance } from './models';

@Injectable({ providedIn: 'root' })
export class DailyBalanceApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.dailyBalanceApiUrl}/api/v1/daily-balances`;

  getByDate(date: string): Observable<DailyBalance> {
    return this.http.get<DailyBalance>(`${this.baseUrl}/${date}`);
  }
}
