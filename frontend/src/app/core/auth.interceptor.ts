import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token;
  const isApi = req.url.startsWith('/api/');
  const authed = isApi && token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authed).pipe(
    catchError((err: HttpErrorResponse) => {
      // An expired/invalid token on a protected call ends the session. A failed login must not.
      if (err.status === 401 && isApi && !req.url.startsWith('/api/auth/')) auth.logout();
      return throwError(() => err);
    }),
  );
};
