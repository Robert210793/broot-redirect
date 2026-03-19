import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
    const router = inject(Router);

    return next(request).pipe(
        catchError((error: HttpErrorResponse) => {
            if (error.status === 403) {
                // Skip redirect for login and status check endpoints to avoid loops
                const isAuthEndpoint = request.url.includes('/api/auth/login')
                    || request.url.includes('/api/auth/status');

                if (!isAuthEndpoint) {
                    router.navigate(['/login']);
                }
            }

            return throwError(() => error);
        })
    );
};