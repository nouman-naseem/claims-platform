import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { errMsg } from '../core/util';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="narrow card">
      <h1>Sign in</h1>
      @if (error) { <div class="alert error">{{ error }}</div> }
      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="field"><label for="email">Email</label><input id="email" type="email" formControlName="email" autocomplete="username"></div>
        <div class="field"><label for="pw">Password</label><input id="pw" type="password" formControlName="password" autocomplete="current-password"></div>
        <button type="submit" [disabled]="form.invalid || busy">Sign in</button>
        <a routerLink="/register" style="margin-left:1rem">Register as a claimant</a>
      </form>
      <hr>
      <p class="muted">Demo accounts (password <code>Password123!</code>):</p>
      <div class="row">
        @for (d of demo; track d.email) {
          <button type="button" class="secondary" (click)="fill(d.email)">{{ d.label }}</button>
        }
      </div>
    </div>`,
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);

  demo = [
    { label: 'Claimant (SG)', email: 'claimant.sg1@example.com' },
    { label: 'Officer (SG)', email: 'officer.sg@claims.example' },
    { label: 'Manager', email: 'manager@claims.example' },
  ];
  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  error = '';
  busy = false;

  fill(email: string) { this.form.setValue({ email, password: 'Password123!' }); }

  submit() {
    this.busy = true; this.error = '';
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => this.router.navigate([this.auth.homeRoute()]),
      error: (e) => { this.error = errMsg(e); this.busy = false; },
    });
  }
}
