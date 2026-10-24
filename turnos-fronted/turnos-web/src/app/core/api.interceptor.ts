import { HttpInterceptorFn } from '@angular/common/http';
export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  const token = typeof sessionStorage === 'undefined' ? null : sessionStorage.getItem('turnos_token');
  const headers: Record<string, string> = { 'X-Requested-With': 'XMLHttpRequest' };
  if (token && request.url.startsWith('/api/')) headers['Authorization'] = `Bearer ${token}`;
  return next(request.clone({ setHeaders: headers }));
};
