import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';
import { ShellComponent } from './core/layout/shell.component';
import { LoginPageComponent } from './features/auth/login-page.component';
import { DailyBalancePanelComponent } from './features/daily-balance/daily-balance-panel.component';
import { TransactionsPanelComponent } from './features/transactions/transactions-panel.component';

export const routes: Routes = [
  { path: 'login', component: LoginPageComponent, canActivate: [guestGuard], title: 'Entrar — Fluxo de Caixa' },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'lancamentos' },
      { path: 'lancamentos', component: TransactionsPanelComponent, title: 'Lançamentos — Fluxo de Caixa' },
      { path: 'saldo', component: DailyBalancePanelComponent, title: 'Saldo Diário — Fluxo de Caixa' },
    ],
  },
  { path: '**', redirectTo: '' },
];
