import { ServiceCard } from "./components/ServiceCard";
import type { StatusTone } from "./components/StatusPill";
import { useSystemHealth } from "./hooks/useSystemHealth";
import type { PythonStatus } from "./types/system";
import "./App.css";

const PYTHON_STATUS_LABEL: Record<PythonStatus, string> = {
  online: "En línea",
  unavailable: "No disponible",
  timeout: "Tiempo de espera agotado",
  invalid_response: "Respuesta inválida",
  error: "Error",
};

const PYTHON_STATUS_TONE: Record<PythonStatus, StatusTone> = {
  online: "ok",
  unavailable: "error",
  timeout: "warn",
  invalid_response: "warn",
  error: "error",
};

const BANNER_COPY: Record<"loading" | "online" | "degraded" | "error", string> = {
  loading: "Consultando el estado del sistema…",
  online: "Todos los servicios están en línea.",
  degraded: "La Web API está en línea, pero el motor Python presenta problemas.",
  error: "No se pudo contactar a la Web API.",
};

function App() {
  const { status, response, errorMessage, lastCheckedAt } = useSystemHealth();

  const apiTone: StatusTone =
    status === "loading" ? "neutral" : status === "error" ? "error" : "ok";
  const apiLabel =
    status === "loading" ? "Consultando…" : status === "error" ? "Sin conexión" : "En línea";

  const pythonTone: StatusTone =
    status === "loading" || status === "error"
      ? "neutral"
      : PYTHON_STATUS_TONE[response!.python.status];
  const pythonLabel =
    status === "loading"
      ? "Consultando…"
      : status === "error"
        ? "Desconocido"
        : PYTHON_STATUS_LABEL[response!.python.status];

  return (
    <>
      <header className="app-header">
        <h1>Vectify — Diagnóstico del sistema</h1>
        <p className="app-header__subtitle">
          Sprint fundacional: verifica que React, ASP.NET Core y el motor Python estén
          comunicados correctamente.
        </p>
      </header>

      <main>
        <section aria-label="Resumen general" className={`status-banner status-banner--${status}`}>
          <p>{BANNER_COPY[status]}</p>
        </section>

        <section aria-label="Estado de los servicios" className="service-grid">
          <ServiceCard title="Web API (ASP.NET Core)" tone={apiTone} statusLabel={apiLabel}>
            {status === "error" ? (
              <p className="service-card__message">
                {errorMessage ?? "No se pudo establecer conexión con la Web API."}
              </p>
            ) : status === "loading" ? (
              <p className="service-card__message">Esperando la respuesta de la Web API.</p>
            ) : (
              <p className="service-card__message">
                La Web API respondió correctamente a la última consulta de salud.
              </p>
            )}
          </ServiceCard>

          <ServiceCard title="Motor Python (FastAPI)" tone={pythonTone} statusLabel={pythonLabel}>
            {status === "loading" || status === "error" ? (
              <p className="service-card__message">
                El estado del motor Python depende de la Web API; todavía no hay datos.
              </p>
            ) : (
              <dl className="service-card__details">
                <div>
                  <dt>Servicio</dt>
                  <dd>{response!.python.service ?? "—"}</dd>
                </div>
                <div>
                  <dt>Versión</dt>
                  <dd>{response!.python.version ?? "—"}</dd>
                </div>
                {response!.python.message && (
                  <div>
                    <dt>Detalle</dt>
                    <dd>{response!.python.message}</dd>
                  </div>
                )}
              </dl>
            )}
          </ServiceCard>
        </section>

        <p className="last-checked">
          {lastCheckedAt
            ? `Última verificación: ${lastCheckedAt.toLocaleTimeString()}`
            : "Aún no se realizó ninguna verificación."}
        </p>
      </main>

      <footer className="app-footer">
        <p>Vectify · M1-S01 · Skeleton y comunicación .NET ↔ Python</p>
      </footer>
    </>
  );
}

export default App;
