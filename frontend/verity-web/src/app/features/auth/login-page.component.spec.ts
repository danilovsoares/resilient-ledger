import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { LoginPageComponent } from './login-page.component';

function fakeJwt(expiresInSeconds: number): string {
  const payload = { exp: Math.floor(Date.now() / 1000) + expiresInSeconds };
  const base64 = btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `header.${base64}.signature`;
}

async function submitLogin(returnUrl: string | null): Promise<ReturnType<typeof vi.spyOn>> {
  sessionStorage.clear();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) } },
      },
    ],
  });

  const fixture = TestBed.createComponent(LoginPageComponent);
  fixture.detectChanges();

  const router = TestBed.inject(Router);
  const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  const httpMock = TestBed.inject(HttpTestingController);

  const compiled = fixture.nativeElement as HTMLElement;
  const usernameInput = compiled.querySelector('input[type=text]') as HTMLInputElement;
  const passwordInput = compiled.querySelector('input[type=password]') as HTMLInputElement;
  usernameInput.value = 'comerciante';
  usernameInput.dispatchEvent(new Event('input'));
  passwordInput.value = 'verity123';
  passwordInput.dispatchEvent(new Event('input'));
  fixture.detectChanges();

  (compiled.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));

  const req = httpMock.expectOne(`${environment.ledgerApiUrl}/api/v1/auth/login`);
  req.flush({ accessToken: fakeJwt(3600), username: 'comerciante', displayName: 'Comerciante' });

  await fixture.whenStable();
  httpMock.verify();

  return navigateSpy;
}

describe('LoginPageComponent', () => {
  it('após login bem-sucedido, navega para o returnUrl interno pedido antes do login', async () => {
    const navigateSpy = await submitLogin('/saldo');

    expect(navigateSpy).toHaveBeenCalledWith('/saldo');
  });

  it('ignora um returnUrl que aponta para fora da aplicação (open redirect) e usa /lancamentos', async () => {
    const navigateSpy = await submitLogin('//evil.example.com');

    expect(navigateSpy).toHaveBeenCalledWith('/lancamentos');
  });

  it('usa /lancamentos quando não há returnUrl', async () => {
    const navigateSpy = await submitLogin(null);

    expect(navigateSpy).toHaveBeenCalledWith('/lancamentos');
  });
});
