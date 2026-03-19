import { Injectable, signal } from '@angular/core';

export interface Toast {
    id: number;
    message: string;
    type: 'success' | 'error' | 'info';
}

@Injectable({
    providedIn: 'root'
})
export class ToastService {

    private counter = 0;

    public toasts = signal<Toast[]>([]);

    show(message: string, type: 'success' | 'error' | 'info' = 'info'): void {
        const id = ++this.counter;

        this.toasts.update((current) => [...current, { id, message, type }]);

        setTimeout(() => {
            this.dismiss(id);
        }, 4000);
    }

    dismiss(id: number): void {
        this.toasts.update((current) => current.filter((toast) => toast.id !== id));
    }
}