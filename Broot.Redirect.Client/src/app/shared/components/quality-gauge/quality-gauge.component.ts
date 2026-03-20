import { Component, input } from '@angular/core';
import { MatchQualityLevel } from '../../models/redirect-rule';

@Component({
    selector: 'app-quality-gauge',
    imports: [],
    templateUrl: './quality-gauge.component.html',
    styleUrl: './quality-gauge.component.css'
})
export class QualityGaugeComponent {

    quality = input.required<number>();
    level = input.required<MatchQualityLevel>();
    explanation = input<string>('');

    showTooltip = false;

    get colorClass(): string {
        switch (this.level()) {
            case 'red':
                return 'gauge-red';
            case 'yellow':
                return 'gauge-yellow';
            case 'green':
                return 'gauge-green';
        }
    }

    get ariaLabel(): string {
        return `Link-Qualität: ${this.quality()}%. ${this.explanation()}`;
    }
}