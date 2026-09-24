#!/usr/bin/env node
/**
 * PreToolUse hook — mcp__Notion__notion-update-page
 *
 * Bloquea mecánicamente que el agente mueva una tarjeta a una columna de cierre
 * (Done / Hecho / Completado / ...). Cerrar una tarjeta es una decisión humana:
 * la instrucción de texto en el prompt del orquestador dice que no lo haga, y
 * este hook lo hace imposible aunque la instrucción se pierda por compactación
 * de contexto o por una inyección en el contenido de una tarjeta.
 *
 * Override para casos legítimos: exportar SPRINT_ALLOW_DONE=1 en la sesión.
 * Estados de cierre configurables con SPRINT_DONE_STATES (separados por coma).
 */

const DEFAULT_DONE_STATES = [
  "done", "hecho", "hecha", "completado", "completada", "complete", "completed",
  "terminado", "terminada", "finalizado", "finalizada", "cerrado", "cerrada",
  "closed", "shipped", "publicado", "deployed", "entregado",
];

function readStdin() {
  return new Promise((resolve) => {
    let raw = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (c) => (raw += c));
    process.stdin.on("end", () => resolve(raw));
    setTimeout(() => resolve(raw), 5000);
  });
}

function allow() {
  process.stdout.write(JSON.stringify({ continue: true }));
  process.exit(0);
}

function deny(reason) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "deny",
        permissionDecisionReason: reason,
      },
    })
  );
  process.exit(0);
}

(async () => {
  if (process.env.SPRINT_ALLOW_DONE === "1") allow();

  let payload;
  try {
    payload = JSON.parse((await readStdin()) || "{}");
  } catch {
    allow();
  }

  const input = payload.tool_input || {};
  if (input.command !== "update_properties") allow();

  const props = input.properties;
  if (!props || typeof props !== "object") allow();

  const doneStates = (process.env.SPRINT_DONE_STATES
    ? process.env.SPRINT_DONE_STATES.split(",")
    : DEFAULT_DONE_STATES
  ).map((s) => s.trim().toLowerCase()).filter(Boolean);

  for (const [key, value] of Object.entries(props)) {
    const values = Array.isArray(value) ? value : [value];
    for (const v of values) {
      if (typeof v !== "string") continue;
      if (doneStates.includes(v.trim().toLowerCase())) {
        deny(
          `Bloqueado por el hook del sprint: intentaste poner la propiedad "${key}" en "${v}", ` +
            `que es un estado de cierre del board. Mover una tarjeta a Done es una decisión humana ` +
            `por diseño de este sistema. Dejá la tarjeta en Review con el preview y el reporte de QA, ` +
            `informá al usuario que está lista, y no busques una vía alternativa para cerrarla.`
        );
      }
    }
  }

  allow();
})();
