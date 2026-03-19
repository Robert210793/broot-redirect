import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-nav',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './nav.component.html',
  styleUrl: './nav.component.css'
})
export class NavComponent {
 
    protected readonly authService = inject(AuthService);
 
    private readonly router = inject(Router);
 
    onLogout(): void {
        this.authService.logout().subscribe(() => {
            this.router.navigate(['/']);
        });
    }

    onClose(): void {
        this.router.navigate(['/']);
    }
}