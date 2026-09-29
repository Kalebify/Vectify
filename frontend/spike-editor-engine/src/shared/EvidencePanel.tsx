import { SPIKE_POINTS, type SpikePoint } from "./useEvidenceLog";

export function EvidencePanel(props: { done: Set<SpikePoint>; logs: string[] }) {
  const { done, logs } = props;
  return (
    <div className="spike__log">
      <strong>Puntos cubiertos en esta sesión</strong>
      <ul className="checklist">
        {SPIKE_POINTS.map((p) => (
          <li key={p} className={done.has(p) ? "ok" : "pending"}>
            {p}
          </li>
        ))}
      </ul>
      <strong>Log</strong>
      {logs.map((l, i) => (
        <div className="log-entry" key={i}>
          <code>{l}</code>
        </div>
      ))}
    </div>
  );
}
