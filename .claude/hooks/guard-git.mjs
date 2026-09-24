#!/usr/bin/env node
/**
 * PreToolUse hook — Bash
 *
 * Impide que el sprint destruya trabajo o publique en la rama principal.
 * El orquestador trabaja siempre en una rama sprint/*: mergear, forzar un push,
 * resetear duro o desplegar a producción por CLI nunca forman parte del
 * procedimiento.
 *
 * Override deliberado: SPRINT_ALLOW_GIT_WRITE=1.
 */

const BLOCKED = [
  {
    re: /\bgit\s+push\b[^\n]*(--force\b|--force-with-lease\b|\s-f\b)/,
    why: "git push forzado puede reescribir historia remota. Hacé un push normal de la rama del sprint.",
  },
  {
    re: /\bgit\s+push\b[^\n]*\b(main|master|develop|production)(\s|$|:)/,
    why: "push directo a una rama protegida. El sprint solo pushea su propia rama sprint/*; el merge lo hace una persona.",
  },
  {
    re: /\bgit\s+(merge|rebase)\b/,
    why: "integrar la rama del sprint en otra rama es una decisión humana. Dejá la rama publicada y avisá.",
  },
  {
    re: /\bgit\s+reset\b[^\n]*--hard\b/,
    why: "reset --hard descarta trabajo sin recuperación. Usá git restore sobre archivos concretos.",
  },
  {
    re: /\bgit\s+clean\b[^\n]*-[a-z]*[fd]/,
    why: "git clean borra archivos no rastreados, incluido el blackboard .sprint/. No es parte del procedimiento.",
  },
  {
    re: /\bgit\s+branch\b[^\n]*\s-D\b/,
    why: "borrado forzado de rama. Si una rama sobra, decilo y que la borre una persona.",
  },
  {
    // Borrado recursivo forzado, salvo sobre artefactos de build reconocibles.
    re: /\brm\s+(-[a-zA-Z]*r[a-zA-Z]*f|-[a-zA-Z]*f[a-zA-Z]*r)\b(?![^\n|;&]*\b(node_modules|\.next|\.nuxt|\.svelte-kit|\.astro|\.turbo|\.cache|\.vite|dist|build|out|coverage)\b)/,
    why: "borrado recursivo forzado fuera de artefactos de build (node_modules, .next, dist, build, out, coverage...). Borrá archivos concretos, uno por uno, y decí cuáles.",
  },
  {
    re: /\bvercel\b[^\n]*(\s--prod\b|\s--target[= ]production\b)/,
    why: "deploy a producción por CLI. Este flujo solo despliega previews: corré vercel sin --prod.",
  },
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

(async () => {
  if (process.env.SPRINT_ALLOW_GIT_WRITE === "1") allow();

  let payload;
  try {
    payload = JSON.parse((await readStdin()) || "{}");
  } catch {
    allow();
  }

  const command = (payload.tool_input || {}).command;
  if (typeof command !== "string") allow();

  for (const rule of BLOCKED) {
    if (rule.re.test(command)) {
      process.stdout.write(
        JSON.stringify({
          hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: `Bloqueado por el hook del sprint: ${rule.why}`,
          },
        })
      );
      process.exit(0);
    }
  }

  allow();
})();
