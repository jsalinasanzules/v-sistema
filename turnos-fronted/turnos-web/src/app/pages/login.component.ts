import { Component, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-login', standalone: true, imports: [ReactiveFormsModule],
  template: `
  <main class="login-page">
    <section class="login-brand">
      <div class="brand-mark">T<span>+</span></div>
      <p class="eyebrow">PLATAFORMA DE ATENCIÓN</p>
      <h1>Una atención<br>más simple.</h1>
      <p class="brand-copy">Gestiona turnos, cajas y clientes desde un solo lugar.</p>
      <div class="brand-bottom"><span class="live-dot"></span> Plataforma de gestión de turnos</div>
    </section>
    <section class="login-panel">
      <div class="login-card">
        <div class="mobile-brand"><span class="brand-mark small">T<span>+</span></span><span>Turnos</span></div>
        @if (!recovering()) {
          <p class="eyebrow">BIENVENIDO DE NUEVO</p><h2>Iniciar sesión</h2>
          <p class="muted">Ingresa tus credenciales para continuar.</p>
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <label for="username">Usuario</label>
            <input id="username" formControlName="username" autocomplete="username" placeholder="Tu nombre de usuario" maxlength="20">
            @if (form.controls.username.touched && form.controls.username.invalid) { <small class="field-error">Ingresa tu usuario (8 a 20 caracteres).</small> }
            <label for="password">Contraseña</label>
            <input id="password" type="password" formControlName="password" autocomplete="current-password" placeholder="Ingresa tu contraseña">
            @if (form.controls.password.touched && form.controls.password.invalid) { <small class="field-error">La contraseña es obligatoria.</small> }
            @if (error()) { <div class="alert error" role="alert">{{ error() }}</div> }
            <button class="btn btn-primary login-submit" type="submit" [disabled]="loading()">{{ loading() ? 'Validando acceso…' : 'Ingresar' }} <span>→</span></button>
          </form>
          <button class="text-button forgot" type="button" (click)="recovering.set(true)">¿Olvidaste tu contraseña?</button>
        } @else {
          <button class="back-link" type="button" (click)="recovering.set(false)">← Volver al inicio de sesión</button>
          <p class="eyebrow">RECUPERAR ACCESO</p><h2>¿Olvidaste tu contraseña?</h2>
          <p class="muted">Ingresa tu usuario o correo para solicitar ayuda con el acceso.</p>
          <form [formGroup]="recoveryForm" (ngSubmit)="requestRecovery()">
            <label for="recovery">Usuario o correo</label><input id="recovery" formControlName="identity" placeholder="usuario o correo">
            @if (recoveryMessage()) { <div class="alert info">{{ recoveryMessage() }}</div> }
            <button class="btn btn-primary login-submit" type="submit" [disabled]="recoveryForm.invalid">Solicitar recuperación <span>→</span></button>
          </form>
          <p class="muted recovery-note">La recuperación automática requiere habilitar el endpoint de recuperación en el backend. Esta pantalla no envía ni cambia contraseñas todavía.</p>
        }
        <p class="login-footer">© {{ year }} Turnos · Acceso seguro</p>
      </div>
    </section>
  </main>`
})
export class LoginComponent {
  readonly year = new Date().getFullYear();
  readonly loading = signal(false); readonly error = signal(''); readonly recovering = signal(false); readonly recoveryMessage = signal('');
  readonly form: FormGroup<{ username: FormControl<string>; password: FormControl<string> }>;
  readonly recoveryForm: FormGroup<{ identity: FormControl<string> }>;
  constructor(fb: FormBuilder, private auth: AuthService, private router: Router) {
    this.form = fb.nonNullable.group({ username: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(20), Validators.pattern(/^(?=.*[A-Za-z])(?=.*[0-9])[A-Za-z0-9]+$/)]], password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(30), Validators.pattern(/^(?=.*[A-Z])(?=.*[0-9]).+$/)]] });
    this.recoveryForm = fb.nonNullable.group({ identity: ['', [Validators.required, Validators.maxLength(120)]] });
  }
  submit(): void {
    this.error.set(''); if (this.form.invalid) { this.form.markAllAsTouched(); this.error.set('Revisa los campos marcados antes de continuar.'); return; }
    this.loading.set(true); const { username, password } = this.form.getRawValue();
    this.auth.login(username.trim(), password).subscribe({ next: () => { this.loading.set(false); this.router.navigateByUrl('/app'); }, error: (e: HttpErrorResponse) => { this.loading.set(false); this.error.set(e.status === 401 ? (e.error?.message || 'Usuario o contraseña incorrectos, o cuenta pendiente de aprobación.') : 'No fue posible conectar con el servidor. Verifica que el backend esté activo e inténtalo de nuevo.'); } });
  }
  requestRecovery(): void { if (this.recoveryForm.invalid) { this.recoveryForm.markAllAsTouched(); return; } this.recoveryMessage.set('La solicitud está validada, pero el backend aún no dispone de un servicio de recuperación. Contacta al administrador del sistema.'); }
}
