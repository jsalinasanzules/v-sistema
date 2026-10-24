import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./pages/login.component').then(m => m.LoginComponent) },
  { path: 'app', canActivate: [authGuard], loadComponent: () => import('./pages/workspace.component').then(m => m.WorkspaceComponent) },
  { path: '', pathMatch: 'full', redirectTo: 'app' },
  { path: '**', redirectTo: 'app' }
];
