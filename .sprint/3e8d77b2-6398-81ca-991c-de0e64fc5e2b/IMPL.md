# M2.2-S01 · PostgreSQL + EF Core + Migrations — Implementación

## Resumen

Primera tarjeta de MVP2.2: introduce PostgreSQL/EF Core/Npgsql como
infraestructura de base de datos relacional real, en paralelo a los
registries de archivos JSON existentes bajo `App_Data/` (ninguno de esos
registries se tocó). Deliberadamente sin modelo de dominio real (eso es
`M2.2-S02`).

## Decisiones sobre las ambigüedades del spec

### 1. Contenido de la migración inicial "mínima"

Se eligió la opción recomendada por el orquestador: una tabla marcador
(`SchemaProbe`, con `Id`/`CreatedAt`) en vez de cero `DbSet`s. Razón: el DoD
("DB persiste tras reiniciar container") es mucho más demostrable con un
registro real escrito/releído que con solo la tabla interna
`__EFMigrationsHistory`. Se verificó en la práctica (ver "Verificación manual
del DoD" abajo): se insertó una fila real vía `psql` dentro del container, se
reinició `postgres` con `docker compose restart postgres`, y la fila siguió
ahí.

`VectorizationDbContext` (`backend/Vectorify.Api/Data/VectorizationDbContext.cs`)
queda documentado en el propio código como descartable/reemplazable en
cuanto `M2.2-S02` traiga el modelo real.

### 2. Herramienta de testing contra PostgreSQL real

`Testcontainers.PostgreSql` 4.15.0, tal como recomendaba el spec: cada clase
de test (`VectorizationDbContextTests`, `DatabaseHealthEndpointTests`) levanta
su propia instancia efímera y aislada de `postgres:17-alpine`, sin depender de
que `docker compose up` esté corriendo de antemano. Nunca `UseInMemoryDatabase`.

### 3. Nombre/forma de las opciones tipadas de conexión

`Vectorify.Api.Options.PostgresOptions` (sección `Postgres`, un único campo
`ConnectionString`) — mismo patrón `IOptions<T>` que `PythonEngineOptions`/
`FrontendCorsOptions`, en vez de la convención estándar `ConnectionStrings` de
ASP.NET Core, para ser coherente con el resto del proyecto.

Decisión adicional (no forzada por el spec, pero necesaria para decidir
"Development vs. env vars" pedido en el punto 4 de la tarjeta): **la
connection string NUNCA vive en `appsettings.json` ni en
`appsettings.Development.json`** — se resuelve EXCLUSIVAMENTE por la variable
de entorno `Postgres__ConnectionString`, en los tres ambientes (Development
vía `docker-compose.yml`, Test vía Testcontainers, Production vía el
orquestador real). Motivo concreto: los 10 archivos de tests de integración
existentes (`EndToEnd/*.cs`, ~87 métodos `[Fact]`) usan
`WebApplicationFactory<Program>` sin overridear explícitamente el entorno, que
por default cae en "Development" — si `appsettings.Development.json` hubiera
tenido una connection string (aunque fuera un valor de ejemplo no-secreto), los
87 tests existentes habrían intentado una conexión real a Postgres en cada
`GET /api/v1/system/health` (varios de esos tests SÍ pegan a ese endpoint),
con el riesgo de timeouts lentos o comportamiento no determinista en CI sin
Postgres corriendo. Con la connection string SOLO por env var (vacía por
default), esos tests existentes siguen corriendo exactamente igual de rápido
que antes (Postgres se reporta `"unavailable"` sin intentar ninguna conexión
de red, ver `DatabaseHealthChecker.CheckHealthAsync`) y se actualizaron
únicamente las aserciones que dependían del contrato viejo (ver
"Tests existentes actualizados" abajo). El caso "Postgres real arriba" se
cubre aparte con Testcontainers (`DatabaseHealthEndpointTests`).

Para correr `dotnet run` localmente sin Docker y SÍ querer migraciones/health
en online, hay que exportar `Postgres__ConnectionString` a mano (documentado
en el README).

### 4. Migrar automáticamente al arrancar

`dbContext.Database.Migrate()` en `Program.cs`, inmediatamente después de
`var app = builder.Build();` y antes de cualquier middleware — tolerante a
fallos (try/catch, log de error sin exponer la connection string completa,
la API sigue arrancando igual) con el mismo criterio que ya existía para el
chequeo del motor Python. Nunca `EnsureCreated()` (prohibido explícitamente).
Si `Postgres:ConnectionString` no está configurado, se omite la migración con
un log de advertencia (no se intenta ninguna conexión).

## Componentes nuevos

- `backend/Vectorify.Api/Options/PostgresOptions.cs` — opciones tipadas (sección `Postgres`).
- `backend/Vectorify.Api/Data/VectorizationDbContext.cs` — `DbContext` mínimo + `SchemaProbe`.
- `backend/Vectorify.Api/Data/VectorizationDbContextFactory.cs` — `IDesignTimeDbContextFactory`, solo para `dotnet ef`.
- `backend/Vectorify.Api/Data/DatabaseConnectionDescriber.cs` — `Describe(connectionString)` → `"host:puerto/db"`, nunca usuario/password.
- `backend/Vectorify.Api/Data/DatabaseHealthChecker.cs` — `IDatabaseHealthChecker`, `DatabaseHealthState` (`Online`/`Unavailable`/`Error`), tolerante a fallos.
- `backend/Vectorify.Api/Migrations/20260930224631_InitialCreate.cs` (+ `.Designer.cs` + `VectorizationDbContextModelSnapshot.cs`) — migración inicial generada con `dotnet ef migrations add InitialCreate --output-dir Migrations`.

## Componentes modificados

- `backend/Vectorify.Api/Vectorify.Api.csproj` — agrega `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4 y `Microsoft.EntityFrameworkCore.Design` 9.0.20 (`PrivateAssets="all"`, solo tooling).
- `backend/Vectorify.Api/Contracts/SystemHealthResponse.cs` — nuevo `DatabaseHealthInfo(Status, Message)`, `SystemHealthResponse` ahora incluye `Database`.
- `backend/Vectorify.Api/Program.cs` — registro de `PostgresOptions`/`VectorizationDbContext`/`IDatabaseHealthChecker`, bloque de migración automática tolerante a fallos, `GET /api/v1/system/health` extendido (overall `"online"` solo si Python **y** Postgres están `"online"`).
- `backend/Vectorify.Api/appsettings.json` — agrega `"Postgres": { "ConnectionString": "" }` (vacío, documenta la forma sin credenciales).
- `docker-compose.yml` — servicio `postgres` nuevo (`postgres:17-alpine`, volumen `vectorify_postgres_data`, healthcheck `pg_isready`, puerto publicado `POSTGRES_PORT:-5432`); `backend` con `Postgres__ConnectionString` (`Host=postgres;...`) y `depends_on` convertido a forma mapa (`python-engine: condition: service_started`, `postgres: condition: service_healthy`).
- `.env.example` (raíz) — agrega `POSTGRES_PORT`/`POSTGRES_DB`/`POSTGRES_USER`/`POSTGRES_PASSWORD` (valores de ejemplo no-secretos).
- `README.md` — nueva sección "PostgreSQL + EF Core (M2.2-S01)", tabla de variables de entorno, ejemplo de contrato de `/api/v1/system/health` actualizado, notas en "Arranque con Docker"/"Arranque en local"/"Requisitos"/Tests.
- `backend/Vectorify.Api.Tests/Vectorify.Api.Tests.csproj` — agrega `Testcontainers.PostgreSql` 4.15.0 y pines explícitos de `Microsoft.EntityFrameworkCore`/`Microsoft.EntityFrameworkCore.Relational` 9.0.20 (evita un conflicto de versión MSB3277 contra el `Microsoft.EntityFrameworkCore.Design` que arrastra indirectamente `Vectorify.Api.csproj`).
- `backend/Vectorify.Api.Tests/EndToEnd/HealthEndpointsTests.cs` — actualiza 2 tests existentes para reflejar el nuevo campo `Database` (ver debajo).

## Tests nuevos

- `backend/Vectorify.Api.Tests/Data/VectorizationDbContextTests.cs` (Testcontainers): migra desde cero, migrar dos veces es idempotente, round-trip de escritura/lectura de un `SchemaProbe` con un `DbContext` nuevo (confirma que no es un efecto del change tracker en memoria).
- `backend/Vectorify.Api.Tests/EndToEnd/DatabaseHealthEndpointTests.cs` (Testcontainers + `WebApplicationFactory`): arranca la Web API real apuntando a una Postgres real efímera, confirma que la migración automática de `Program.cs` corre sola y que `GET /api/v1/system/health` responde `database.status = "online"` y `status` global `"online"`.

## Tests existentes actualizados

`HealthEndpointsTests.cs`: ninguno de sus tests configura `Postgres:ConnectionString`
(por diseño, ver decisión #3 arriba), así que `database.status` es siempre
`"unavailable"` ahí y el `status` global nunca es `"online"` puro por Python
solo. Se actualizaron:

- `GetSystemHealth_WhenPythonRespondsOk_ReturnsOnlineViaRealCall` → renombrado a
  `GetSystemHealth_WhenPythonRespondsOkButPostgresIsUnconfigured_ReturnsDegradedWithPythonOnline`,
  con aserciones actualizadas (`status` global ahora `"degraded"`, se agrega
  assert de `database.status == "unavailable"`).
- `GetSystemHealth_WhenPythonIsOffline_ReturnsDegradedWithoutBreakingApi` → se
  agrega un assert de `database.status == "unavailable"` (el resto del
  comportamiento no cambió).

Ningún otro test se tocó.

## Verificación manual del DoD con Docker (resultado real)

1. `docker compose down` del stack viejo que ya estaba corriendo (de una
   sesión anterior, sin Postgres) para liberar los puertos.
2. `docker compose up --build -d` desde cero: build de las 3 imágenes
   (`python-engine`, `backend`, `frontend`), creación del volumen nuevo
   `vectify_vectorify_postgres_data`, y arranque en orden:
   `python-engine`/`postgres` → `postgres` pasa a `Healthy` → `backend`
   arranca (recién ahí, confirmando el `depends_on: condition: service_healthy`)
   → `frontend`. `exit code 0`.
3. Logs reales de `backend` (`docker logs vectify-backend-1`):
   ```
   Aplicando migraciones de PostgreSQL en postgres:5432/vectorify...
   Applying migration '20260930224631_InitialCreate'.
   Migraciones de PostgreSQL aplicadas correctamente en postgres:5432/vectorify.
   ```
   Sin usuario/password en ningún lado del log (verificado con
   `grep -i password` sobre el log completo: 0 resultados).
4. `curl http://localhost:5080/api/v1/system/health`:
   ```json
   {"status":"online","timestamp":"2026-09-30T23:30:58.81...+00:00","api":{"status":"online"},"python":{"status":"online","service":"vectorify-python-engine","version":"0.1.0","message":null},"database":{"status":"online","message":null}}
   ```
5. Prueba de persistencia real: `docker exec vectify-postgres-1 psql -U vectorify -d vectorify -c "INSERT INTO schema_probes (\"CreatedAt\") VALUES (now()) RETURNING \"Id\", \"CreatedAt\";"` → insertó `Id=1`.
6. `docker compose restart postgres` → esperó a que `pg_isready` volviera a
   responder, y `SELECT "Id", "CreatedAt" FROM schema_probes;` siguió
   devolviendo la misma fila (`Id=1`, mismo `CreatedAt`) — el volumen nombrado
   garantiza la persistencia, confirmado en la práctica, no solo en la teoría.
7. `curl http://localhost:5080/api/v1/system/health` después del restart:
   sigue en `"status":"online"`, `"database":{"status":"online"}`.
8. `docker compose down` (sin `-v`) al terminar, para dejar el entorno limpio
   sin borrar el volumen de datos.

## Pooling

Npgsql trae connection pooling activado por defecto; no se agregó ninguna
configuración adicional (documentado también en `PostgresOptions.cs` y en el
README).

## Resultado de los 5 comandos de verificación

1. `dotnet build` (desde `backend/`): **0 errores, 0 advertencias**.
2. `dotnet test` (desde `backend/`): **666 pasaron, 0 fallaron** (16 min 16 s —
   incluye 4 tests nuevos contra Testcontainers, que levantan containers reales
   de `postgres:17-alpine`; Docker estuvo disponible y se usó de verdad, no
   simulado).
3. `pytest` (desde `services/python-engine/`, con `.venv` activo): **400
   pasaron, 0 fallaron** (6.89 s) — sin tocar nada de Python en esta tarjeta.
4. `npm test -- --run` (desde `frontend/`): **307 pasaron, 0 fallaron** (36
   archivos, 26.66 s) — sin tocar nada de frontend en esta tarjeta.
5. `npm run build` (desde `frontend/`): **compiló sin errores** (`tsc -b && vite build`, 380 ms de build de Vite).

## Fuera de alcance respetado

No se modeló ningún dominio real (proyectos/imágenes/paletas/capas) como
entidad EF. No se tocó Auth/usuarios. No se tocó almacenamiento binario
(`LocalFileStorage` sigue igual). Ningún registry de archivos JSON existente
se tocó ni se migró a PostgreSQL.
