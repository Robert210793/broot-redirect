import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../shared/services/auth.service';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent {
 
    password = '';
    isLoading = false;
    errorMessage = '';
 
    private readonly authService = inject(AuthService);
    private readonly router = inject(Router);
    private readonly toastService = inject(ToastService);
 
    onSubmit(): void {
        if (!this.password || this.isLoading) {
            return;
        }
 
        this.isLoading = true;
        this.errorMessage = '';
 
        this.authService.login(this.password).subscribe({
            next: (response) => {
                this.isLoading = false;
 
                if (response.success) {
                    this.toastService.show('Logged in successfully.', 'success');
                    this.router.navigate(['/rules']);
                } else {
                    this.errorMessage = 'Invalid password.';
                }
            },
            error: () => {
                this.isLoading = false;
                this.errorMessage = 'Invalid password. Please try again.';
            }
        });
    }
}