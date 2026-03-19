import {
    Directive,
    ElementRef,
    AfterViewInit,
    OnDestroy,
    Input,
    Renderer2
} from '@angular/core';

/**
 * Directive that adds column resizing to a <table> element.
 *
 * Usage:
 *   <table class="data-table" appResizableColumns="rules-table">
 *
 * The input string is used as the localStorage key for persisting column widths.
 * Drag handles are appended to each <th> except the last column.
 * Column widths are stored as pixel values and restored on init.
 */
@Directive({
    selector: '[appResizableColumns]',
    standalone: true
})
export class ResizableColumnsDirective implements AfterViewInit, OnDestroy {

    @Input('appResizableColumns') storageKey = '';

    private tableElement: HTMLTableElement;
    private renderer: Renderer2;
    private handles: HTMLDivElement[] = [];
    private listeners: (() => void)[] = [];

    private activeHeader: HTMLTableCellElement | null = null;
    private startX = 0;
    private startWidth = 0;

    private boundOnMouseMove = this.onMouseMove.bind(this);
    private boundOnMouseUp = this.onMouseUp.bind(this);

    constructor(elementRef: ElementRef<HTMLTableElement>, renderer: Renderer2) {
        this.tableElement = elementRef.nativeElement;
        this.renderer = renderer;
    }

    ngAfterViewInit(): void {
        // Short delay to ensure the table has rendered its headers
        setTimeout(() => this.init(), 0);
    }

    ngOnDestroy(): void {
        this.cleanup();
    }

    private init(): void {
        const headers = this.tableElement.querySelectorAll('thead th');

        if (headers.length === 0) {
            return;
        }

        // Set table layout to fixed for predictable column resizing
        this.renderer.setStyle(this.tableElement, 'table-layout', 'fixed');

        // Restore persisted widths
        const savedWidths = this.loadWidths();

        headers.forEach((header, index) => {
            const thElement = header as HTMLTableCellElement;

            // Apply saved width if available
            if (savedWidths && savedWidths[index] !== undefined) {
                this.renderer.setStyle(thElement, 'width', savedWidths[index] + 'px');
            }

            // Make the header position relative for the handle
            this.renderer.setStyle(thElement, 'position', 'relative');

            // Skip the last column (it fills remaining space)
            if (index >= headers.length - 1) {
                return;
            }

            // Create resize handle
            const handle = this.renderer.createElement('div') as HTMLDivElement;

            this.renderer.setStyle(handle, 'position', 'absolute');
            this.renderer.setStyle(handle, 'top', '0');
            this.renderer.setStyle(handle, 'right', '0');
            this.renderer.setStyle(handle, 'bottom', '0');
            this.renderer.setStyle(handle, 'width', '6px');
            this.renderer.setStyle(handle, 'cursor', 'col-resize');
            this.renderer.setStyle(handle, 'z-index', '1');
            this.renderer.setStyle(handle, 'user-select', 'none');

            // Visual indicator on hover
            const unlistenEnter = this.renderer.listen(handle, 'mouseenter', () => {
                this.renderer.setStyle(handle, 'background', 'var(--color-primary)');
                this.renderer.setStyle(handle, 'opacity', '0.3');
            });

            const unlistenLeave = this.renderer.listen(handle, 'mouseleave', () => {
                if (this.activeHeader !== thElement) {
                    this.renderer.setStyle(handle, 'background', 'transparent');
                    this.renderer.setStyle(handle, 'opacity', '1');
                }
            });

            // Start resize on mousedown
            const unlistenDown = this.renderer.listen(handle, 'mousedown', (event: MouseEvent) => {
                event.preventDefault();
                event.stopPropagation();

                this.activeHeader = thElement;
                this.startX = event.clientX;
                this.startWidth = thElement.offsetWidth;

                this.renderer.setStyle(handle, 'background', 'var(--color-primary)');
                this.renderer.setStyle(handle, 'opacity', '0.5');

                document.addEventListener('mousemove', this.boundOnMouseMove);
                document.addEventListener('mouseup', this.boundOnMouseUp);

                // Prevent text selection while dragging
                this.renderer.setStyle(document.body, 'user-select', 'none');
                this.renderer.setStyle(document.body, 'cursor', 'col-resize');
            });

            this.renderer.appendChild(thElement, handle);

            this.handles.push(handle);
            this.listeners.push(unlistenEnter, unlistenLeave, unlistenDown);
        });
    }

    private onMouseMove(event: MouseEvent): void {
        if (!this.activeHeader) {
            return;
        }

        const delta = event.clientX - this.startX;
        const newWidth = Math.max(40, this.startWidth + delta);

        this.renderer.setStyle(this.activeHeader, 'width', newWidth + 'px');
    }

    private onMouseUp(): void {
        if (!this.activeHeader) {
            return;
        }

        this.activeHeader = null;

        document.removeEventListener('mousemove', this.boundOnMouseMove);
        document.removeEventListener('mouseup', this.boundOnMouseUp);

        this.renderer.removeStyle(document.body, 'user-select');
        this.renderer.removeStyle(document.body, 'cursor');

        // Reset all handle visuals
        for (const handle of this.handles) {
            this.renderer.setStyle(handle, 'background', 'transparent');
            this.renderer.setStyle(handle, 'opacity', '1');
        }

        // Persist current widths
        this.saveWidths();
    }

    private saveWidths(): void {
        if (!this.storageKey) {
            return;
        }

        const headers = this.tableElement.querySelectorAll('thead th');
        const widths: number[] = [];

        headers.forEach((header) => {
            widths.push((header as HTMLTableCellElement).offsetWidth);
        });

        try {
            localStorage.setItem('col-widths:' + this.storageKey, JSON.stringify(widths));
        } catch {
            // localStorage may be unavailable
        }
    }

    private loadWidths(): number[] | null {
        if (!this.storageKey) {
            return null;
        }

        try {
            const raw = localStorage.getItem('col-widths:' + this.storageKey);

            if (!raw) {
                return null;
            }

            const parsed = JSON.parse(raw);

            if (Array.isArray(parsed) && parsed.every((value: unknown) => typeof value === 'number')) {
                return parsed;
            }

            return null;
        } catch {
            return null;
        }
    }

    private cleanup(): void {
        document.removeEventListener('mousemove', this.boundOnMouseMove);
        document.removeEventListener('mouseup', this.boundOnMouseUp);

        for (const unlisten of this.listeners) {
            unlisten();
        }

        this.listeners = [];
        this.handles = [];
    }
}