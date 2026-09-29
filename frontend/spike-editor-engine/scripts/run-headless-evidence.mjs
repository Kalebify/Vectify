// run-headless-evidence.mjs
//
// Script de VERIFICACIÓN (no forma parte de la demo ni se importa desde
// src/): usa puppeteer-core contra un Chrome/Edge ya instalado en la
// máquina (sin descargar un Chromium propio) para manejar la demo real en
// un navegador de verdad, click a click, y capturar los console.log()
// reales -- en particular los números de rendimiento del punto 10, que el
// spec exige que sean "reales, no estimaciones". Corre contra el dev
// server (`npm run dev`) ya levantado en localhost:5183.
//
// Uso: node scripts/run-headless-evidence.mjs
import puppeteer from "puppeteer-core";
import fs from "node:fs";

const CHROME_CANDIDATES = [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
];

function findExecutable() {
  for (const p of CHROME_CANDIDATES) {
    if (fs.existsSync(p)) return p;
  }
  throw new Error("No se encontró Chrome ni Edge instalados.");
}

const CANDIDATES = [
  { tab: "Paper.js", index: 0 },
  { tab: "Fabric.js", index: 1 },
  { tab: "Konva / react-konva", index: 2 },
];

async function run() {
  const executablePath = findExecutable();
  const browser = await puppeteer.launch({ executablePath, headless: true });
  const page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 900 });

  const allLogs = [];
  page.on("console", (msg) => {
    allLogs.push(msg.text());
  });
  page.on("pageerror", (err) => {
    allLogs.push(`[pageerror] ${err.message}`);
  });

  await page.goto("http://localhost:5183/", { waitUntil: "networkidle0" });
  await new Promise((r) => setTimeout(r, 500));

  const summary = {};

  for (const c of CANDIDATES) {
    allLogs.length = 0;
    const tabs = await page.$$(".app__tab");
    await tabs[c.index].click();
    await new Promise((r) => setTimeout(r, 800));

    const buttons = await page.$$(".spike__toolbar button");
    const labels = [];
    for (const b of buttons) {
      labels.push((await page.evaluate((el) => el.textContent, b)) ?? "");
    }

    async function clickByLabel(prefix) {
      const idx = labels.findIndex((l) => l.startsWith(prefix));
      if (idx === -1) throw new Error(`No se encontró botón "${prefix}" en ${c.tab}: ${labels.join(" | ")}`);
      await buttons[idx].click();
      await new Promise((r) => setTimeout(r, 150));
    }

    await clickByLabel("Zoom +");
    await clickByLabel("Zoom -");
    await clickByLabel("Reset vista");
    await clickByLabel("Hit-test @ centro");
    await clickByLabel("Export + round-trip");

    const canvasWrap = await page.$(".spike__canvas-wrap");
    const canvasBox = canvasWrap ? await canvasWrap.boundingBox() : null;
    if (canvasBox) {
      await page.mouse.click(canvasBox.x + canvasBox.width * 0.2, canvasBox.y + canvasBox.height * 0.2);
      await new Promise((r) => setTimeout(r, 200));
    }

    await clickByLabel("Run perf benchmark");
    const start = Date.now();
    let perfLine = null;
    while (Date.now() - start < 20000) {
      perfLine = allLogs.find((l) => l.includes("pan avg frame"));
      if (perfLine) break;
      await new Promise((r) => setTimeout(r, 300));
    }

    summary[c.tab] = {
      buttonLabels: labels,
      perfLine: perfLine ?? "TIMEOUT: no se recibió el log de performance en 20s",
      totalLogs: allLogs.length,
      allLogsForThisTab: allLogs.slice(),
    };
  }

  console.log(JSON.stringify(summary, null, 2));
  await browser.close();
}

run().catch((err) => {
  console.error(err);
  process.exit(1);
});
