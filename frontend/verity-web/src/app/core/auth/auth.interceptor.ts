import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

const API_URLS = [environment.ledgerApiUrl, environment.dailyBalanceApiUrl];
const LOGIN_PATH = '/api/v1/auth/login';

/**
 * Anexa `Authorization: Bearer <token>` às chamadas às APIs usando o token da sessão
 * autenticada (ver AuthService). Um 401 do backend derruba a sessão local — o token não é mais
 * válido (expirado ou revogado), então a UI deve voltar para a tela de login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const targetsOurApi = API_URLS.some((url) => req.url.startsWith(url));
  if (!targetsOurApi || req.url.endsWith(LOGIN_PATH)) {
    return next(req);
  }

  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.getToken();
  const authorizedReq = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authorizedReq).pipe(
    catchError((error) => {
      if (error?.status === 401) {
        authService.logout();
        router.navigateByUrl('/login');
      }
      return throwError(() => error);
    }),
  );
};
