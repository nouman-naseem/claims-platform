import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { MARKETS } from '../core/models';
import { errMsg, label } from '../core/util';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="narrow card">
      <h1>Register as a claimant</h1>
      <p class="muted">Policies issued to your email address are linked automatically. Try <code>new.customer&#64;example.com</code> to see pre-issued demo policies.</p>
      @if (error) { <div class="alert error">{{ error }}</div> }
      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="field"><label for="n">Full name</label><input id="n" formControlName="fullName"></div>
        <div class="field"><label for="e">Email</label><input id="e" type="email" formControlName="email"></div>
        <div class="field"><label for="p">Phone</label><input id="p" formControlName="phone" placeholder="+65 9123 4567"></div>
        <div class="field"><label for="m">Market</label>
          <select id="m" formControlName="market">@for (m of markets; track m) { <option [value]="m">{{ label(m) }}</option> }</select></div>
        <div class="field"><label for="pw">Password</label><input id="pw" type="password" formControlName="password" autocomplete="new-password">
          <span class="muted">At least 8 characters with upper case, lower case and a digit.</span></div>
        <button type="submit" [disabled]="form.invalid || busy">Create account</button>
        <a routerLink="/login" style="margin-left:1rem">Back to sign in</a>
      </form>
    </div>`,
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);
  markets = MARKETS;
  label = label;
  error = '';
  busy = false;
  form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required, Validators.pattern(/^[0-9+\-\s()]{6,30}$/)]],
    market: ['Singapore' as (typeof MARKETS)[number], Validators.required],
    password: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$/)]],
  });

  submit() {
    this.busy = true; this.error = '';
    this.auth.register(this.form.getRawValue()).subscribe({
      next: () => this.router.navigate(['/claims']),
      error: (e) => { this.error = errMsg(e); this.busy = false; },
    });
  }
}
