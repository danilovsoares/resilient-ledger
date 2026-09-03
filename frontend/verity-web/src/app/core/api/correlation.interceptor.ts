import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

const API_URLS = [environment.ledgerApiUrl, environment.dailyBalanceApiUrl];

/** Gera um X-Correlation-ID por requisição — propaga a jornada até os logs do backend (ver docs/observability.md). */
export const correlationInterceptor: HttpInterceptorFn = (req, next) => {
  if (!API_URLS.some((url) => req.url.startsWith(url))) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-Correlation-ID': crypto.randomUUID() } }));
};
