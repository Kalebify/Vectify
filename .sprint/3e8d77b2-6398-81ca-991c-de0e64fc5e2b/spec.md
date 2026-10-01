# M2.2-S01 · PostgreSQL + EF Core + Migrations
URL: https://app.notion.com/p/3e8d77b2639881ca991cde0e64fc5e2b

Primera tarjeta de MVP 2.2. Hasta acá TODA la persistencia del backend es en memoria o sidecars de archivos JSON en disco (`PersistentProjectRegistry`, `PersistentLayerLayoutVersionRegistry`, etc. bajo `App_Data/`, ver `docker-compose.yml`). Esta tarjeta introduce PostgreSQL como base de datos relacional real por primera vez en el proyecto — es la base sobre la que se construyen las 9 tarjetas siguientes de MVP2.2 (modelo de dominio, repositorios, storage de assets, versionado, autosave, etc.).

Stack: ASP.NET Core + Infra.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Incorporar PostgreSQL como base relacional principal y EF Core/Npgsql con migraciones reproducibles.

### Criterio de aceptación (propiedad Notion)
docker compose levanta PostgreSQL; API conecta, ejecuta migraciones controladas y health check; tests usan una DB real/efímera y no una falsa InMemory.

### Arquitectura
ASP.NET Core → EF Core → Npgsql → PostgreSQL. PostgreSQL será source of truth para metadata y relaciones; archivos pesados quedan fuera.

### Implementación
Añadir paquetes compatibles, `VectorizationDbContext`, configuración por environment, connection pooling y health check. PostgreSQL en Docker Compose con volumen persistente.

### Migrations
Crear migration inicial mínima. Documentar create/apply/rollback. No usar `EnsureCreated` en producción.

### Configuración
Secrets fuera del repo; `.env.example` sin credenciales reales. Separar Development/Test/Production.

### Testing
Preferir PostgreSQL real mediante container/test database para comportamientos específicos de PostgreSQL.

### Observabilidad
Logs de conexión/migration sin exponer connection strings.

### Definition of Done
Clone → `docker compose up --build` → API healthy → DB persiste tras reiniciar container → tests pasan.

### Fuera de alcance
Modelo completo de dominio, Auth y almacenamiento binario.

## Estado actual (reutilizar, no reescribir)

- **Cero EF Core hoy**: `backend/Vectorify.Api/Vectorify.Api.csproj` no referencia ningún paquete de Entity Framework/Npgsql (verificado). Toda persistencia existente es archivo-por-versión bajo `App_Data/<subcarpeta>` (un patrón "registry" repetido ~12 veces: `PersistentProjectRegistry`, `PersistentThresholdConfigRegistry`, `PersistentVectorVersionRegistry`, `PersistentLayerLayoutVersionRegistry`, etc.) — **ninguno de esos registries se toca en esta tarjeta** (son responsabilidad de tarjetas posteriores de MVP2.2 migrarlos o no; esta tarjeta solo prepara la infraestructura de EF Core/PostgreSQL en paralelo, sin reemplazar nada todavía).
- **Patrón de configuración establecido**: todas las opciones tipadas del proyecto usan `IOptions<XOptions>` enlazadas a una sección de `appsettings.json` (ver `PythonEngineOptions`, `FrontendCorsOptions`, `LayerLayoutRegistryOptions`) con override por variables de entorno (`Section__Key`, ya usado en `docker-compose.yml` para `PythonEngine__BaseUrl`). La connection string de PostgreSQL debe seguir el MISMO criterio: nunca hardcodeada, nunca commiteada con credenciales reales.
- **`docker-compose.yml`** ya tiene 3 servicios (`python-engine`, `backend`, `frontend`) con un volumen nombrado (`vectorify_backend_data`) para `App_Data`. Agregar un 4to servicio `postgres` (imagen oficial `postgres:17-alpine` o similar) con su propio volumen nombrado nuevo, y que `backend` dependa de él (`depends_on` con `condition: service_healthy`, ya que Postgres tarda en aceptar conexiones).
- **Health check existente**: `GET /api/v1/system/health` (`Program.cs` línea ~531, `SystemHealthResponse` en `Contracts/SystemHealthResponse.cs`) ya compone el estado de la API + el motor Python (`ApiHealthInfo`/`PythonHealthInfo`) en una única respuesta que el frontend (`useSystemHealth`, banner de diagnóstico ya visible en `App.tsx`) consume y muestra. Extender ESTE mismo endpoint con un `DatabaseHealthInfo` (mismo criterio de discriminated status: `"online"`/`"unavailable"`/`"error"`) es más consistente que crear un endpoint de salud paralelo y desconectado — el frontend ya tiene el lugar donde mostrarlo.
- **Test project**: `backend/Vectorify.Api.Tests` ya usa `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) + xunit — no hay ningún paquete de testing de base de datos todavía.
- **`.env.example`** ya existe en la raíz del repo, documentando variables sin credenciales reales (mismo patrón que `docker-compose.yml` usa con `${VAR:-default}`) — agregar ahí las variables nuevas de PostgreSQL (host/puerto/db/usuario/password de ejemplo, nunca reales).

## Alcance de esta tarjeta

- [ ] **Paquetes NuGet**: `Npgsql.EntityFrameworkCore.PostgreSQL` + `Microsoft.EntityFrameworkCore.Design` (para el tooling de migraciones, `dotnet ef`) en `Vectorify.Api.csproj`.
- [ ] **`VectorizationDbContext`** (nombre literal pedido por la tarjeta): un `DbContext` mínimo. Dado que "Modelo completo de dominio" está explícitamente Fuera de Alcance (es `M2.2-S02 · Modelo de datos y ERD del dominio`, la tarjeta siguiente), este `DbContext` debe ser lo más chico posible que aún permita probar el pipeline completo (conectar, migrar, persistir, reiniciar) — ver "Ambigüedades detectadas" para la recomendación concreta de qué debe contener la migración inicial.
- [ ] **Configuración tipada** (`PostgresOptions` o similar, `IOptions<T>` enlazado a una sección `Postgres`/`ConnectionStrings`) con override por variables de entorno, MISMO patrón que `PythonEngineOptions`. Connection pooling: Npgsql lo trae activado por defecto (no requiere configuración extra salvo documentar el comportamiento).
- [ ] **`docker-compose.yml`**: servicio `postgres` nuevo con volumen persistente nombrado (mismo criterio que `vectorify_backend_data`), healthcheck propio (`pg_isready`), y `backend` con `depends_on: postgres: condition: service_healthy`. Variables de entorno para credenciales vía `${VAR:-default}`, documentadas en `.env.example` (sin valores reales).
- [ ] **Migraciones**: migración inicial mínima generada con `dotnet ef migrations add`. Documentar en el propio repo (README o un doc nuevo corto) los 3 comandos: crear, aplicar, rollback (`dotnet ef migrations add`/`dotnet ef database update`/`dotnet ef database update <migración anterior>`). Las migraciones se aplican automáticamente al arrancar la API en Development (patrón común: `dbContext.Database.Migrate()` al inicio de `Program.cs`, NUNCA `EnsureCreated()` — la tarjeta lo prohíbe explícitamente porque `EnsureCreated` no es compatible con un flujo de migraciones versionado).
- [ ] **Health check**: extender `GET /api/v1/system/health` con el estado de la conexión a PostgreSQL (ver "Estado actual" arriba). Logs de conexión/aplicación de migraciones sin exponer la connection string completa (loguear host/puerto/nombre de DB, nunca usuario/password).
- [ ] **Testing con PostgreSQL real**: usar `Testcontainers.PostgreSql` (paquete NuGet estándar para esto en .NET, ya que Docker está disponible en el entorno de ejecución de los tests — ver "Ambigüedades detectadas") para que los tests de integración corran contra una instancia real y efímera de PostgreSQL, no una base falsa `UseInMemoryDatabase`. Al menos un test que confirme: la API se conecta, aplica migraciones, persiste un registro y lo puede releer.
- [ ] **Separación Development/Test/Production**: `appsettings.Development.json` ya existe (vacío salvo logging) — agregar ahí (o vía env vars de `docker-compose.yml`) la connection string de desarrollo. Test usa Testcontainers (una DB efímera propia, no depende de ningún `appsettings`). Production: solo por variables de entorno, nunca un archivo `appsettings.Production.json` con credenciales.

## Fuera de alcance (explícito, respetar)
- Modelo completo de dominio (proyectos/imágenes/paletas/capas como entidades EF reales) — eso es `M2.2-S02`.
- Auth / usuarios — `M2.2-S09 · User Ownership + DevelopmentUserContext`.
- Almacenamiento binario (mover los originales/SVGs de `LocalFileStorage` a algo más robusto) — `M2.2-S04 · Object Storage + gestión de Assets`.
- NO migrar ningún registry de archivos JSON existente a PostgreSQL todavía — coexisten en paralelo hasta que una tarjeta posterior lo decida explícitamente.

## Tests
- Migraciones se aplican limpiamente contra una PostgreSQL real y efímera (Testcontainers) desde cero.
- Health check refleja "online" con Postgres arriba y un estado degradado/error coherente si la conexión falla (mismo criterio que ya existe para el motor Python: no debe tirar una excepción no controlada).
- Al menos un test de round-trip (escribir + releer) contra la DB real de test, para probar que el `DbContext`/connection string/pooling funcionan de punta a punta.
- Verificación manual (documentada en el reporte, no necesariamente automatizada): `docker compose up --build` desde cero, reiniciar el container de `postgres`, confirmar que los datos sobreviven (el volumen nombrado ya lo garantiza, pero hay que demostrarlo).

## Definition of Done
Clone → `docker compose up --build` → API healthy (incluyendo Postgres) → DB persiste tras reiniciar el container → los 5 comandos de verificación (incluido `dotnet test` contra Postgres real vía Testcontainers) pasan.

## Umbrales de calidad
Mismo estándar ya usado en todo el proyecto: `dotnet build`/`dotnet test` (ahora incluyendo tests contra Testcontainers, que requieren Docker disponible — ya lo está en este entorno), `pytest`, `npm test`, `npm run build`, todos en verde.

## Ambigüedades detectadas
- **Qué debe contener la migración inicial "mínima"**: no especificado más allá de "no es el modelo completo de dominio". Recomendación del orquestador: una única tabla marcador mínima (ej. `SchemaProbe` con un `Id`/`CreatedAt`) cuyo único propósito es demostrar el pipeline completo (migración se aplica, se puede escribir y releer, sobrevive a un restart) — explícitamente documentada como DESCARTABLE/reemplazable en cuanto `M2.2-S02` introduzca el modelo real. Alternativa válida: cero `DbSet`s y apoyarse únicamente en que EF Core igual crea la tabla `__EFMigrationsHistory` al aplicar la migración inicial (prueba que el pipeline de migraciones funciona, sin inventar ninguna tabla de dominio) — el implementador elige y justifica cuál de las dos cumple mejor el DoD ("DB persiste tras reiniciar container" es más demostrable con al menos un registro real escrito, lo que favorece la opción de la tabla marcador).
- **Herramienta de testing contra PostgreSQL real**: no especificado qué mecanismo concreto ("container/test database"). Recomendación del orquestador: `Testcontainers.PostgreSql` (paquete NuGet estándar en el ecosistema .NET para este caso exacto) en vez de, por ejemplo, apuntar los tests a la misma instancia de `docker-compose` — Testcontainers da una instancia nueva y aislada por corrida de tests, sin depender de que `docker compose up` esté corriendo de antemano. Requiere Docker disponible en el entorno donde corren los tests (ya confirmado disponible en esta sesión).
- **Nombre/forma exacta de las opciones tipadas de conexión**: no especificado — el implementador elige un nombre coherente con el resto del proyecto (`PostgresOptions` o `ConnectionStrings` con la convención estándar de ASP.NET Core) y lo documenta.
- **Aplicar migraciones automáticamente al arrancar vs. un paso manual/CLI separado**: la tarjeta no lo especifica explícitamente, pero el DoD ("clone → `docker compose up --build` → API healthy") implica que migrar debe pasar SIN un paso manual adicional — recomendación: `dbContext.Database.Migrate()` al arrancar en `Program.cs` (envuelto en manejo de errores + logging, sin exponer la connection string), documentando en el reporte por qué esto no entra en conflicto con "no usar `EnsureCreated` en producción" (son mecanismos distintos: `Migrate()` SÍ es el flujo correcto y versionado que la tarjeta pide, `EnsureCreated()` es el que se prohíbe explícitamente).
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Primera tarjeta de MVP 2.2, corrida en modo autónomo (mismo criterio ya usado en todo MVP2/MVP2.1, confirmado explícitamente por el usuario para las 10 tarjetas de este milestone). Es la tarjeta más fundacional de las 10: las siguientes 9 (modelo de dominio, repositorios, storage, versionado, autosave, Mis Proyectos, ownership, integración E2E) asumen que esta infraestructura ya existe y funciona. Recordatorio: correr `npm run build` además de `npm test` (regla ya establecida), y confirmar explícitamente en el reporte que Docker estuvo disponible para correr los tests de Testcontainers.
