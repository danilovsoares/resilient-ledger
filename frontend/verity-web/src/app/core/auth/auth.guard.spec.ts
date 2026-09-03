import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { authGuard, guestGuard } from './auth.guard';

const STORAGE_KEY = 'verity.session';

function setAuthenticatedSession(): void {
  sessionStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({ accessToken: 't', username: 'u', displayName: 'U', expiresAt: Date.now() + 60_000 }),
  );
}

describe('authGuard / guestGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  it('authGuard bloqueia e redireciona para /login sem sessão autenticada', () => {
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/login');
  });

  it('authGuard preserva a URL original em returnUrl para retomar o deep link após o login', () => {
    const state = { url: '/saldo' } as never;
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, state));

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/login?returnUrl=%2Fsaldo');
  });

  it('authGuard libera a navegação com sessão autenticada', () => {
    setAuthenticatedSession();

    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));

    expect(result).toBe(true);
  });

  it('guestGuard libera o acesso a /login sem sessão autenticada', () => {
    const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));

    expect(result).toBe(true);
  });

  it('guestGuard redireciona para /lancamentos quando já autenticado', () => {
    setAuthenticatedSession();

    const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/lancamentos');
  });
});
