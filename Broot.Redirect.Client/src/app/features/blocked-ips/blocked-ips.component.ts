import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../shared/services/auth.service';
import { ToastService } from '../../shared/services/toast.service';
import { BlockedIpInfo } from '../../shared/models/redirect-rule';

@Component({
    selector: 'app-blocked-ips',
    imports: [FormsModule],
    templateUrl: './blocked-ips.component.html',
    styleUrl: './blocked-ips.component.css'
})
export class BlockedIpsComponent implements OnInit {

    private readonly authService = inject(AuthService);
    private readonly toastService = inject(ToastService);

    blockedIps = signal<BlockedIpInfo[]>([]);
    isLoading = signal(true);
    showClearConfirm = signal(false);
    isClearing = signal(false);
    isBlocking = signal(false);

    newIpAddress = '';

    ngOnInit(): void {
        this.loadBlockedIps();
    }

    loadBlockedIps(): void {
        this.isLoading.set(true);

        this.authService.getBlockedIps().subscribe({
            next: (result) => {
                this.blockedIps.set(result);
                this.isLoading.set(false);
            },
            error: () => {
                this.toastService.show('Blockierte IPs konnten nicht geladen werden.', 'error');
                this.isLoading.set(false);
            }
        });
    }

    onBlockIp(): void {
        const trimmedIp = this.newIpAddress.trim();

        if (!trimmedIp) {
            return;
        }

        this.isBlocking.set(true);

        this.authService.blockIp(trimmedIp).subscribe({
            next: () => {
                this.toastService.show(`IP ${trimmedIp} blockiert.`, 'success');
                this.newIpAddress = '';
                this.isBlocking.set(false);
                this.loadBlockedIps();
            },
            error: (error) => {
                const message = error?.error?.error || 'IP konnte nicht blockiert werden.';
                this.toastService.show(message, 'error');
                this.isBlocking.set(false);
            }
        });
    }

    onUnblock(ip: string): void {
        this.authService.unblockIp(ip).subscribe({
            next: () => {
                this.toastService.show(`IP ${ip} Blockierung aufgehoben.`, 'success');
                this.loadBlockedIps();
            },
            error: () => {
                this.toastService.show('Blockierung konnte nicht aufgehoben werden.', 'error');
            }
        });
    }

    onClearAll(): void {
        this.showClearConfirm.set(true);
    }

    onCancelClear(): void {
        this.showClearConfirm.set(false);
    }

    onConfirmClear(): void {
        this.isClearing.set(true);

        this.authService.clearBlockedIps().subscribe({
            next: () => {
                this.toastService.show('Alle Blockierungen aufgehoben.', 'success');
                this.showClearConfirm.set(false);
                this.isClearing.set(false);
                this.loadBlockedIps();
            },
            error: () => {
                this.toastService.show('Blockierungen konnten nicht aufgehoben werden.', 'error');
                this.isClearing.set(false);
            }
        });
    }

    formatDate(isoString: string): string {
        const date = new Date(isoString);

        return date.toLocaleString('de-CH', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    formatTimeRemaining(isoString: string): string {
        const blockedUntil = new Date(isoString).getTime();
        const now = Date.now();
        const remainingMs = blockedUntil - now;

        if (remainingMs <= 0) {
            return 'Abgelaufen';
        }

        const remainingMinutes = Math.ceil(remainingMs / 60000);

        if (remainingMinutes < 60) {
            return `${remainingMinutes}m`;
        }

        const hours = Math.floor(remainingMinutes / 60);
        const minutes = remainingMinutes % 60;

        return `${hours}h ${minutes}m`;
    }
}