# Sistema de gestión de turnos

Aplicación full-stack para la prueba técnica .NET 8 + Angular, con PostgreSQL en Supabase.

## Estructura
- `turnos-backend/`: API ASP.NET Core 8, Dapper/Npgsql, JWT/JWE, Serilog, SQL inicial y función de generación de turnos.
- `turnos-fronted/turnos-web/`: Angular modular, login, guardias de rutas, interceptor JWT, menú por rol desde la API y dashboard responsive.
- `docker-compose.yml`: ejecución conjunta de API y frontend.
- `.env.example`: plantilla de configuración local, sin credenciales reales.

## Desarrollo local
1. Ejecuta en Supabase los scripts `turnos-backend/database/001_initial_schema.sql` y `002_dynamic_menu.sql` en ese orden.
2. Inicia la API desde `turnos-backend/Turnos.Api` con `dotnet run`. El proxy del frontend espera `http://localhost:5164`; ajusta `turnos-fronted/turnos-web/proxy.conf.json` si tu puerto es distinto.
3. En otra terminal, ejecuta `cd turnos-fronted/turnos-web && npm ci && npm start`.
4. Abre http://localhost:4200/login.

## Docker
1. Copia `.env.example` a `.env`.
2. Rellena las credenciales de Session Pooler de Supabase y genera claves secretas independientes para JWT, cifrado y HMAC. No compartas ni publiques el archivo `.env`.
3. Ejecuta desde la raíz `docker compose up --build`.
4. Abre http://localhost:8081.

## Notas de alcance
La interfaz ya consume los endpoints existentes. La recuperación de contraseña, importación masiva persistente CSV/XLSX, edición de usuarios/clientes, filtros completos por fechas, reportes y flujo completo de asignación/atención en caja requieren endpoints y reglas adicionales en backend; la interfaz no simula que esas operaciones hayan sido guardadas. Revisa el README de cada proyecto para más detalles.
