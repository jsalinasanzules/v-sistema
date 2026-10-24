import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface SessionUser { id: string; username: string; role: string; name?: string; firstName?: string; lastName?: string; }
export interface LoginResponse { accessToken: string; tokenType: string; expiresIn: number; user: SessionUser; }
export interface MenuItem { id: number; code: string; label: string; route: string; icon?: string; sortOrder?: number; canView?: boolean; canCreate?: boolean; canUpdate?: boolean; canDelete?: boolean; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly baseUrl = '/api';
  readonly user = signal<SessionUser | null>(this.readUser());
  readonly token = signal<string | null>(this.readToken());
  constructor(private http: HttpClient) {}
  login(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.baseUrl}/auth/login`, { username, password }).pipe(tap(result => {
      sessionStorage.setItem('turnos_token', result.accessToken);
      sessionStorage.setItem('turnos_user', JSON.stringify(result.user));
      this.token.set(result.accessToken); this.user.set(result.user);
    }));
  }
  loadMenu(): Observable<MenuItem[]> { return this.http.get<MenuItem[]>(`${this.baseUrl}/menu`); }
  me(): Observable<SessionUser> { return this.http.get<SessionUser>(`${this.baseUrl}/auth/me`).pipe(tap(user => { this.user.set(user); sessionStorage.setItem('turnos_user', JSON.stringify(user)); })); }
  logout(): void { sessionStorage.removeItem('turnos_token'); sessionStorage.removeItem('turnos_user'); this.token.set(null); this.user.set(null); }
  isAuthenticated(): boolean { return !!this.token(); }
  private readToken(): string | null { return typeof sessionStorage === 'undefined' ? null : sessionStorage.getItem('turnos_token'); }
  private readUser(): SessionUser | null { if (typeof sessionStorage === 'undefined') return null; try { const value = sessionStorage.getItem('turnos_user'); return value ? JSON.parse(value) as SessionUser : null; } catch { return null; } }
}
