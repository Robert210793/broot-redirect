import { Component, input, output, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';

@Component({
    selector: 'app-password-modal',
    imports: [FormsModule],
    templateUrl: './password-modal.component.html',
    styleUrl: './password-modal.component.css'
})
export class PasswordModalComponent {

    isOpen = input.required<boolean>();

    authenticated = output<void>();
    closed = output<void>();

    private readonly authService = inject(AuthService);

    password = '';
    errorMessage = signal('');
    isLoading = signal(false);

    onSubmit(): void {
        const trimmed = this.password.trim();

        if (!trimmed) {
            return;
        }

        this.isLoading.set(true);
        this.errorMessage.set('');

        this.authService.login(trimmed).subscribe({
            next: (response) => {
                this.isLoading.set(false);

                if (response.success) {
                    this.password = '';
                    this.errorMessage.set('');
                    this.authenticated.emit();
                } else {
                    this.errorMessage.set('Falsches Passwort. Bitte versuche es erneut.');
                }
            },
            error: () => {
                this.isLoading.set(false);
                this.errorMessage.set('Verbindungsfehler. Bitte versuche es erneut.');
            }
        });
    }

    onClose(): void {
        if (this.isLoading()) {
            return;
        }

        this.password = '';
        this.errorMessage.set('');
        this.closed.emit();
    }

    onBackdropClick(event: MouseEvent): void {
        if ((event.target as HTMLElement).classList.contains('modal-backdrop')) {
            this.onClose();
        }
    }

    onKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape') {
            this.onClose();
        }
    }
}