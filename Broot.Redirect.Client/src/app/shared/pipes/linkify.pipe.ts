import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

// Matches http(s) URLs; the final char excludes common trailing punctuation
// so a URL at the end of a sentence doesn't swallow the period/comma.
const URL_PATTERN = /(https?:\/\/[^\s<]+[^\s<.,:;!?)\]}'"])/g;

/**
 * Renders plain admin text with clickable links. The whole string is HTML-escaped
 * first (so it's safe to bypass sanitization), then only http(s) URLs are turned
 * into anchors that open in a new tab. Bind via [innerHTML].
 */
@Pipe({ name: 'linkify' })
export class LinkifyPipe implements PipeTransform {

    private readonly sanitizer = inject(DomSanitizer);

    transform(value: string | null | undefined): SafeHtml {
        const escaped = this.escapeHtml(value ?? '');

        const linked = escaped.replace(
            URL_PATTERN,
            (url) => `<a href="${url}" target="_blank" rel="noopener noreferrer">${url}</a>`
        );

        return this.sanitizer.bypassSecurityTrustHtml(linked);
    }

    private escapeHtml(text: string): string {
        return text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
}
