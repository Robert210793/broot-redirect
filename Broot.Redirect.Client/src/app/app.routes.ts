import { Routes } from '@angular/router';
import { authGuard } from './shared/guards/auth.guard';

export const routes: Routes = [
    {
        path: 'login',
        loadComponent: () => import('./features/auth/login.component').then((module) => module.LoginComponent)
    },
    {
        path: 'rules',
        canActivate: [authGuard],
        loadComponent: () => import('./features/rules/rules-list.component').then((module) => module.RulesListComponent)
    },
    {
        path: 'global-rules',
        canActivate: [authGuard],
        loadComponent: () => import('./features/global-rules/global-rules.component').then((module) => module.GlobalRulesComponent)
    },
    {
        path: 'settings',
        canActivate: [authGuard],
        loadComponent: () => import('./features/settings/settings.component').then((module) => module.SettingsComponent)
    },
    {
        path: 'import',
        canActivate: [authGuard],
        loadComponent: () => import('./features/import/import.component').then((module) => module.ImportComponent)
    },
    {
        path: 'stats',
        canActivate: [authGuard],
        loadComponent: () => import('./features/stats/stats.component').then((module) => module.StatsComponent)
    },
    {
        path: 'blocked-ips',
        canActivate: [authGuard],
        loadComponent: () => import('./features/blocked-ips/blocked-ips.component').then((module) => module.BlockedIpsComponent)
    },
    {
        path: 'validate',
        canActivate: [authGuard],
        loadComponent: () => import('./features/validate/validate.component').then((module) => module.ValidateComponent)
    },
    {
        // Root path: info page (public)
        path: '',
        loadComponent: () => import('./features/info-page/info-page.component').then((module) => module.InfoPageComponent),
        pathMatch: 'full',
        data: { hideNav: true }
    },
    {
        // Wildcard: any unmatched path shows the info page (public)
        path: '**',
        loadComponent: () => import('./features/info-page/info-page.component').then((module) => module.InfoPageComponent),
        data: { hideNav: true }
    }
];