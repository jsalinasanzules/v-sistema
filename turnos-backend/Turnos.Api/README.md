# Turnos.Api (.NET 8)

API REST ASP.NET Core 8 + PostgreSQL/Supabase, Dapper, JWT firmado y cifrado (JWE), BCrypt, Serilog y Swagger.

## Configuración

Configura secretos mediante variables de entorno; no subas claves reales al repositorio:

```bash
export ConnectionStrings__DefaultConnection='Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true'
export Security__JwtSigningKey='una-clave-aleatoria-de-al-menos-32-caracteres'
export Security__JwtEncryptionKey='otra-clave-aleatoria-de-al-menos-32-caracteres'
export Security__DataEncryptionKey='clave-privada-para-cifrar-datos-personales'
export Security__LookupHmacKey='clave-privada-distinta-para-indices-HMAC'
dotnet restore
dotnet run
```

Swagger se habilita en Development: `https://localhost:<puerto>/swagger`. Health check: `GET /health`. CORS permite por defecto Angular en `http://localhost:4200`.

Ejecuta en Supabase SQL Editor, en orden, `../database/001_initial_schema.sql` y `../database/002_dynamic_menu.sql`.

## Endpoints

- `POST /api/auth/login`, `GET /api/auth/me`
- `GET /api/menu` (menú consultado de base de datos por rol)
- `GET/POST /api/users`, `POST /api/users/{id}/approve`, `POST /api/users/{id}/reject`, `DELETE /api/users/{id}`
- `GET /api/turns/types`, `GET/POST /api/turns`, `PATCH /api/turns/{id}/status`
- `GET/POST /api/clients`
- `GET/POST /api/cashboxes`, `POST /api/cashboxes/{id}/assign`
- `GET/POST /api/services`, `GET/POST /api/contracts`, `POST /api/contracts/{id}/replace`, `POST /api/contracts/{id}/cancel`, `GET/POST /api/payments`

## Seguridad / reglas
- Las contraseñas se almacenan con BCrypt; identificación y email se cifran con AES-GCM y se indexan con HMAC-SHA256 para comprobar unicidad.
- JWT JWE: los claims quedan cifrados y el token firmado.
- Usuario creado por gestor queda PENDING; administrador aprueba/rechaza.
- Generación de código usa función PostgreSQL atómica `app.generate_turn_code`.
- Auditoría funcional en `app.audit_events` y logs estructurados Serilog.
- La base de datos limita a dos cajeros asignados por caja; asignación se serializa con bloqueo de fila.

## Importante antes de producción
Usa secretos robustos de un gestor de secretos, HTTPS, políticas RLS/privilegios y revisión de seguridad. Revisa las reglas de atención concurrente y los permisos de menú según la matriz definitiva. Los endpoints de CRUD aún pueden ampliarse para edición/importación y reporting, según la prueba.
