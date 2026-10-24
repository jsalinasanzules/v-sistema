# Turnos Web — Angular
Frontend modular para gestión de turnos. Incluye login, guardias de ruta, interceptor JWT, menú por rol desde API y dashboard responsive.

## Requisitos
- Node.js y npm compatibles con Angular CLI del proyecto.
- API ASP.NET Core disponible en `http://localhost:5065`.
- Ejecutar en Supabase los scripts SQL iniciales, incluido `002_dynamic_menu.sql`.

## Desarrollo
```bash
npm ci
npm start
```
Abre http://localhost:4200. `proxy.conf.json` reenvía las peticiones `/api` a `http://localhost:5065`; cambia el destino si tu API usa otro puerto.

## Compilar
```bash
npm run build
```

## Estructura modular
- `src/app/core/auth.service.ts`: login, sesión, usuario actual y menú dinámico.
- `src/app/core/auth.guard.ts`: protección de rutas.
- `src/app/core/api.interceptor.ts`: adjunta JWT Bearer a las peticiones API.
- `src/app/pages/login.component.ts`: formulario validado, errores, loading y pantalla informativa de recuperación.
- `src/app/pages/workspace.component.ts`: bienvenida, indicadores, usuarios, turnos, clientes y cajas.
- `src/app/app.routes.ts`: módulos cargados de forma diferida.

## Endpoints consumidos
`POST /api/auth/login`, `GET /api/auth/me`, `GET /api/menu`, endpoints de usuarios, turnos, clientes, cajas, servicios, contratos y pagos documentados en el backend.

## Pendiente de completar con el backend
La recuperación de contraseña todavía no tiene endpoint; la UI lo informa y no simula envío. La importación CSV solo lee/valida el archivo y avisa que no se han persistido filas porque falta un endpoint de importación masiva; XLSX requiere soporte adicional. Faltan endpoints dedicados para actualización de usuarios/clientes, filtros de fecha, reportes y asignación específica de turnos a cajeros.

## Docker
El Dockerfile compila el frontend y sirve los archivos estáticos con Nginx. Nginx reenvía `/api` al servicio Docker `turnos-api:8080`. El backend y frontend deben compartir una red en Docker Compose.
