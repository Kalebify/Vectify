#!/usr/bin/env node
/**
 * PreToolUse hook — herramientas de deploy del conector de Vercel.
 *
 * El sprint solo despliega previews. Publicar en producción es una decisión
 * humana y no puede depender de que el orquestador recuerde una instrucción.
 *
 * El conector de Vercel renombra herramientas cada tanto (deploy_to_vercel,
 * create_deployment, ...), así que este hook no confía en un nombre concreto:
 * inspecciona el payload y bloquea cualquier indicio de target production.
 *
 * Override para un deploy de producción deliberado: SPRINT_ALLOW_PROD=1.
 */

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

(async () => {
  if (process.env.SPRINT_ALLOW_PROD === "1") allow();

  let payload;
  try {
    payload = JSON.parse((await readStdin()) || "{}");
  } catch {
    allow();
  }

  const input = payload.tool_input || {};

  const productionish =
    (typeof input.target === "string" && input.target.toLowerCase() === "production") ||
    input.production === true ||
    (typeof input.environment === "string" &&
      input.environment.toLowerCase() === "production");

  if (productionish) {
    process.stdout.write(
      JSON.stringify({
        hookSpecificOutput: {
          hookEventName: "PreToolUse",
          permissionDecision: "deny",
          permissionDecisionReason:
            "Bloqueado por el hook del sprint: este flujo solo despliega previews. " +
            'Volvé a llamar la herramienta con target "preview" y poné esa URL en el comentario ' +
            "de la tarjeta de Notion. Publicar en producción lo decide una persona, no el agente.",
        },
      })
    );
    process.exit(0);
  }

  allow();
})();
