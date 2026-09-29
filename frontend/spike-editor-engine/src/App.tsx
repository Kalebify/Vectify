import { useEffect, useState } from "react";
import type { SpikeDocument } from "./types";
import { fetchSpikeSvgText, parseSpikeSvg } from "./shared/loadLayersFromSvg";
import { PaperSpike } from "./candidates/paper/PaperSpike";
import { FabricSpike } from "./candidates/fabric/FabricSpike";
import { KonvaSpike } from "./candidates/konva/KonvaSpike";

type Candidate = "paper" | "fabric" | "konva";

const CANDIDATES: { id: Candidate; label: string }[] = [
  { id: "paper", label: "Paper.js" },
  { id: "fabric", label: "Fabric.js" },
  { id: "konva", label: "Konva / react-konva" },
];

export default function App() {
  const [tab, setTab] = useState<Candidate>("paper");
  const [doc, setDoc] = useState<SpikeDocument | null>(null);
  const [svgText, setSvgText] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchSpikeSvgText("/multicolor-input.svg")
      .then((text) => {
        setSvgText(text);
        setDoc(parseSpikeSvg(text));
      })
      .catch((err: unknown) => setError(String(err)));
  }, []);

  return (
    <div className="app">
      <header className="app__header">
        <h1>Spike M2.1-S05 — motor del Editor General</h1>
        <nav className="app__tabs" aria-label="Candidato">
          {CANDIDATES.map((c) => (
            <button
              key={c.id}
              type="button"
              className={`app__tab${tab === c.id ? " is-active" : ""}`}
              aria-pressed={tab === c.id}
              onClick={() => setTab(c.id)}
            >
              {c.label}
            </button>
          ))}
        </nav>
      </header>
      <main className="app__body">
        {error && <p style={{ padding: 16, color: "#ff8a8a" }}>Error cargando fixture: {error}</p>}
        {!error && !doc && <p style={{ padding: 16 }}>Cargando fixture...</p>}
        {doc && svgText && tab === "paper" && <PaperSpike doc={doc} svgText={svgText} />}
        {doc && svgText && tab === "fabric" && <FabricSpike doc={doc} svgText={svgText} />}
        {doc && svgText && tab === "konva" && <KonvaSpike doc={doc} svgText={svgText} />}
      </main>
    </div>
  );
}
