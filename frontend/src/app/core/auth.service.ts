import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthResponse, Market, Role, User } from './models';

const KEY = 'claims.auth';

/**
 * Keeps the JWT in sessionStorage (cleared when the tab closes). That is a trade-off:
 * it is readable by scripts, so a strict CSP and no third-party scripts matter. See docs/TRADE-OFFS.md.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  private state = signal<AuthResponse | null>(this.restore());
  readonly user = computed<User | null>(() => this.state()?.user ?? null);
  readonly role = computed<Role | null>(() => this.user()?.role ?? null);

  get token(): string | null {
    const s = this.state();
    return s && new Date(s.expiresAt).getTime() > Date.now() ? s.token : null;
  }

  isLoggedIn(): boolean { return this.token !== null; }
  hasRole(...roles: Role[]): boolean { const r = this.role(); return !!r && roles.includes(r); }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', { email, password }).pipe(tap((r) => this.store(r)));
  }

  register(body: { email: string; password: string; fullName: string; phone: string; market: Market }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', body).pipe(tap((r) => this.store(r)));
  }

  logout(): void {
    sessionStorage.removeItem(KEY);
    this.state.set(null);
    this.router.navigate(['/login']);
  }

  homeRoute(): string { return this.role() === 'Manager' ? '/dashboard' : '/claims'; }

  private store(r: AuthResponse): void {
    sessionStorage.setItem(KEY, JSON.stringify(r));
    this.state.set(r);
  }

  private restore(): AuthResponse | null {
    try { return JSON.parse(sessionStorage.getItem(KEY) ?? 'null'); } catch { return null; }
  }
}
