import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { label } from './core/util';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    @if (auth.user(); as user) {
      <nav class="top">
        <div class="inner">
          <a class="brand" [routerLink]="auth.homeRoute()">Claims Platform</a>
          @if (auth.hasRole('Manager')) {
            <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
          }
          <a routerLink="/claims" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
            {{ auth.hasRole('Claimant') ? 'My claims' : 'Claims' }}
          </a>
          @if (auth.hasRole('Claimant')) {
            <a routerLink="/claims/new" routerLinkActive="active">Report incident</a>
          }
          <span class="spacer"></span>
          <span>{{ user.fullName }} · {{ roleLabel(user.role) }}</span>
          <a href="#" (click)="$event.preventDefault(); auth.logout()">Sign out</a>
        </div>
      </nav>
    }
    <router-outlet />
  `,
})
export class AppComponent {
  auth = inject(AuthService);
  roleLabel = label;
}
