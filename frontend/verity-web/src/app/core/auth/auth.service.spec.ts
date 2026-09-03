import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

const STORAGE_KEY = 'verity.session';

function fakeJwt(expiresInSeconds: number): string {
  const payload = { exp: Math.floor(Date.now() / 1000) + expiresInSeconds };
  const base64 = btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `header.${base64}.signature`;
}

describe('AuthService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'login', children: [] }])],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('sessão expirada em sessionStorage é tratada como não autenticada e é limpa', () => {
    sessionStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ accessToken: 't', username: 'u', displayName: 'U', expiresAt: Date.now() - 1000 }),
    );

    const auth = TestBed.inject(AuthService);

    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('sessão válida (não expirada) em sessionStorage é restaurada como autenticada', () => {
    sessionStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ accessToken: 't', username: 'u', displayName: 'Usuário', expiresAt: Date.now() + 60_000 }),
    );

    const auth = TestBed.inject(AuthService);

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.displayName()).toBe('Usuário');
  });

  it('login bem-sucedido armazena a sessão e marca o usuário como autenticado', async () => {
    const auth = TestBed.inject(AuthService);
    const httpMock = TestBed.inject(HttpTestingController);

    const loginPromise = auth.login('comerciante', 'verity123');

    const req = httpMock.expectOne(`${environment.ledgerApiUrl}/api/v1/auth/login`);
    expect(req.request.body).toEqual({ username: 'comerciante', password: 'verity123' });
    req.flush({ accessToken: fakeJwt(3600), username: 'comerciante', displayName: 'Comerciante' });

    await loginPromise;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.getToken()).not.toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).not.toBeNull();

    httpMock.verify();
  });

  it('logout remove a sessão de sessionStorage e derruba o estado autenticado', async () => {
    const auth = TestBed.inject(AuthService);
    const httpMock = TestBed.inject(HttpTestingController);

    const loginPromise = auth.login('comerciante', 'verity123');
    httpMock.expectOne(`${environment.ledgerApiUrl}/api/v1/auth/login`).flush({
      accessToken: fakeJwt(3600),
      username: 'comerciante',
      displayName: 'Comerciante',
    });
    await loginPromise;

    auth.logout();

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.getToken()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();

    httpMock.verify();
  });

  it('sessão é encerrada automaticamente no instante em que o token expira, mesmo sem nenhuma chamada de API', async () => {
    vi.useFakeTimers();

    const auth = TestBed.inject(AuthService);
    const httpMock = TestBed.inject(HttpTestingController);

    const loginPromise = auth.login('comerciante', 'verity123');
    httpMock.expectOne(`${environment.ledgerApiUrl}/api/v1/auth/login`).flush({
      accessToken: fakeJwt(60),
      username: 'comerciante',
      displayName: 'Comerciante',
    });
    await loginPromise;

    expect(auth.isAuthenticated()).toBe(true);

    vi.advanceTimersByTime(60_001);

    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();

    httpMock.verify();
  });
});
