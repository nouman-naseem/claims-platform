import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/guards';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login.component').then((m) => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./pages/register.component').then((m) => m.RegisterComponent) },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'claims' },
      { path: 'claims', loadComponent: () => import('./pages/claim-list.component').then((m) => m.ClaimListComponent) },
      {
        path: 'claims/new',
        canActivate: [roleGuard],
        data: { roles: ['Claimant'] },
        loadComponent: () => import('./pages/claim-new.component').then((m) => m.ClaimNewComponent),
      },
      { path: 'claims/:id', loadComponent: () => import('./pages/claim-detail.component').then((m) => m.ClaimDetailComponent) },
      {
        path: 'dashboard',
        canActivate: [roleGuard],
        data: { roles: ['Manager'] },
        loadComponent: () => import('./pages/dashboard.component').then((m) => m.DashboardComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
