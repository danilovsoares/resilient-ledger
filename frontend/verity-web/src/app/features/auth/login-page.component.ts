import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = this.fb.nonNullable.group({
    username: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { username, password } = this.form.getRawValue();

    try {
      await this.auth.login(username, password);
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch {
      this.errorMessage.set('Usuário ou senha inválidos.');
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * `returnUrl` vem da query string (posta lá pelo authGuard, ver auth.guard.ts) — trata como
   * entrada não confiável: só aceita um caminho interno de um único segmento inicial ("/x...",
   * nunca "//x..." — que o navegador trataria como URL absoluta de outro host), para não virar
   * um open redirect caso alguém distribua um link de login com esse parâmetro manipulado.
   */
  private safeReturnUrl(): string {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    return returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/lancamentos';
  }
}
