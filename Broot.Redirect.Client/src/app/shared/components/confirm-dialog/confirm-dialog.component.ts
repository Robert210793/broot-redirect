import { Component, input, output } from '@angular/core';

@Component({
    selector: 'app-confirm-dialog',
    imports: [],
    templateUrl: './confirm-dialog.component.html',
    styleUrl: './confirm-dialog.component.css'
})
export class ConfirmDialogComponent {

    isOpen = input.required<boolean>();
    title = input('Confirm');
    message = input('Are you sure?');
    confirmText = input('Delete');
    cancelText = input('Cancel');
    variant = input<'danger' | 'warning' | 'info'>('danger');

    confirmed = output<void>();
    cancelled = output<void>();

    get confirmButtonClass(): string {
        switch (this.variant()) {
            case 'danger':
                return 'btn btn-danger';
            case 'warning':
                return 'btn btn-primary';
            case 'info':
                return 'btn btn-primary';
        }
    }

    onConfirm(): void {
        this.confirmed.emit();
    }

    onCancel(): void {
        this.cancelled.emit();
    }

    onBackdropClick(event: MouseEvent): void {
        if ((event.target as HTMLElement).classList.contains('confirm-backdrop')) {
            this.onCancel();
        }
    }

    onKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape') {
            this.onCancel();
        }
    }
}