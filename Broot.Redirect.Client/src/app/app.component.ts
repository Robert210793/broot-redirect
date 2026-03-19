import { Component, inject, computed, OnInit } from '@angular/core';
import { RouterOutlet, Router, NavigationEnd, ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, mergeMap } from 'rxjs';
import { NavComponent } from './shared/components/nav/nav.component';
import { ToastContainerComponent } from './shared/components/toast-container.component';
import { AuthService } from './shared/services/auth.service';

@Component({
    selector: 'app-root',
    imports: [RouterOutlet, NavComponent, ToastContainerComponent],
    templateUrl: './app.component.html',
    styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {

    private readonly router = inject(Router);
    private readonly activatedRoute = inject(ActivatedRoute);
    private readonly authService = inject(AuthService);

    private readonly routeData = toSignal(
        this.router.events.pipe(
            filter((event) => event instanceof NavigationEnd),
            map(() => {
                let route = this.activatedRoute;

                while (route.firstChild) {
                    route = route.firstChild;
                }

                return route;
            }),
            mergeMap((route) => route.data),
            map((data) => data as Record<string, unknown>)
        ),
        { initialValue: {} }
    );

    showNav = computed(() => {
        const data = this.routeData() as Record<string, unknown>;

        return !data['hideNav'];
    });

    ngOnInit(): void {
        this.authService.checkStatus().subscribe();
    }
}