import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Bloqueia rotas protegidas sem sessão válida, redirecionando para /login com a URL original em
 * `returnUrl` (para retomar o deep link depois do login — ver LoginPageComponent). A autoridade
 * real de autorização continua sendo o backend ([Authorize] em cada endpoint) — este guard só
 * evita renderizar a casca autenticada sem sessão local.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Impede que um usuário já autenticado veja a tela de login de novo. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated() ? router.createUrlTree(['/lancamentos']) : true;
};
