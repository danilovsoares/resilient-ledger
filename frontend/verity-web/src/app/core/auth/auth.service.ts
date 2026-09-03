import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LoginResponse } from '../api/models';

const STORAGE_KEY = 'verity.session';

interface StoredSession {
  accessToken: string;
  username: string;
  displayName: string;
  /** Epoch ms do claim `exp` do token — só para decidir quando a UI deve pedir login de novo; a validação de verdade é sempre feita pelo backend a cada request. */
  expiresAt: number;
}

/**
 * Sessão do usuário: login real contra POST /api/v1/auth/login (credenciais validadas contra
 * um usuário persistido, ver docs/security.md), token guardado em sessionStorage (não
 * localStorage, para não sobreviver além da aba/sessão do navegador).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<StoredSession | null>(readStoredSession());
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly displayName = computed(() => this.session()?.displayName ?? null);

  constructor() {
    const current = this.session();
    if (current) {
      this.scheduleExpiry(current.expiresAt);
    }
  }

  getToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  async login(username: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<LoginResponse>(`${environment.ledgerApiUrl}/api/v1/auth/login`, { username, password }),
    );

    const expiresAt = readTokenExpiry(response.accessToken);
    if (expiresAt === null) {
      throw new Error('Token recebido do servidor não pôde ser interpretado.');
    }

    const stored: StoredSession = {
      accessToken: response.accessToken,
      username: response.username,
      displayName: response.displayName,
      expiresAt,
    };

    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    this.session.set(stored);
    this.scheduleExpiry(expiresAt);
  }

  logout(): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
    sessionStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
  }

  /**
   * Encerra a sessão localmente assim que o token expira, mesmo sem nenhuma chamada de API
   * acontecer nesse meio-tempo — evita que a aba renderize a casca autenticada com um token já
   * morto (o backend continua sendo a autoridade real: isso é só para a UI refletir o estado
   * corretamente mais cedo). Só navega para /login no disparo tardio do timer — na inicialização
   * do serviço (sessão já expirada ao abrir a aba) o Router ainda não terminou a navegação
   * inicial, e o authGuard já cuida de mandar para /login nesse caso.
   */
  private scheduleExpiry(expiresAt: number): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
    }

    const delay = expiresAt - Date.now();
    if (delay <= 0) {
      this.logout();
      return;
    }

    this.expiryTimer = setTimeout(() => {
      this.logout();
      this.router.navigateByUrl('/login');
    }, delay);
  }
}

function readStoredSession(): StoredSession | null {
  const raw = sessionStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as StoredSession;
    if (typeof parsed.expiresAt !== 'number' || parsed.expiresAt <= Date.now()) {
      sessionStorage.removeItem(STORAGE_KEY);
      return null;
    }

    return parsed;
  } catch {
    sessionStorage.removeItem(STORAGE_KEY);
    return null;
  }
}

/** Lê o claim `exp` (segundos) do payload do JWT, sem validar assinatura — só para fins de UX local; a validação de verdade é sempre feita pelo backend. */
function readTokenExpiry(token: string): number | null {
  const payload = token.split('.')[1];
  if (!payload) {
    return null;
  }

  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '=');
    const { exp } = JSON.parse(atob(base64)) as { exp?: number };
    return typeof exp === 'number' ? exp * 1000 : null;
  } catch {
    return null;
  }
}
