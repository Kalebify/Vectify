// Prueba manual reproducible del recorrido multicolor contra la pila real.
// Requiere Docker Compose en :5173/:5080/:8001 y Chrome local.
import puppeteer from "puppeteer-core";
import { existsSync } from "node:fs";

const imagePath = process.env.QA_IMAGE_PATH;
if (!imagePath || !existsSync(imagePath)) throw new Error("Definí QA_IMAGE_PATH con un PNG multicolor existente.");
const chrome = [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
].find(existsSync);
if (!chrome) throw new Error("Chrome/Edge no está instalado.");

const browser = await puppeteer.launch({ executablePath: chrome, headless: true });
const page = await browser.newPage();
await page.setViewport({ width: 1440, height: 900 });
const errors = [];
const api = [];
page.on("pageerror", (error) => errors.push(error.message));
page.on("response", (response) => {
  if (response.url().includes("/api/v1/")) api.push({ status: response.status(), url: response.url() });
});

async function waitText(text, timeout = 30000) {
  await page.waitForFunction((value) => document.body.innerText.includes(value), { timeout }, text);
}
async function clickButton(text) {
  const buttons = await page.$$("button");
  for (const button of buttons) {
    const label = await page.evaluate((el) => (el.textContent ?? "").trim(), button);
    if (label === text) {
      await button.click();
      return;
    }
  }
  throw new Error(`No se encontró el botón «${text}». Disponibles: ${await page.evaluate(() => [...document.querySelectorAll("button")].map((b) => b.textContent?.trim()).join(" | "))}`);
}
async function count(selector) { return page.$$eval(selector, (nodes) => nodes.length); }

try {
  await page.goto("http://localhost:5173/", { waitUntil: "networkidle2" });
  await waitText("Nuevo proyecto");
  const file = await page.$('input[type="file"]');
  if (!file) throw new Error("No aparece el selector de imagen.");
  await file.uploadFile(imagePath);
  await waitText("Confirmar carga");
  await clickButton("Confirmar carga");
  await waitText("Paleta de colores");
  await clickButton("Detectar paleta");
  await waitText("Paleta detectada", 60000);
  const paletteColors = await page.$$eval(".color-swatch__color", (els) => els.map((el) => el.getAttribute("title")?.toLowerCase()));
  const expected = ["#ff0000", "#00c800", "#ffdc00", "#0000ff"];
  for (const color of expected) if (!paletteColors.includes(color)) throw new Error(`No se detectó ${color}. Paleta: ${paletteColors.join(", ")}`);
  await clickButton("Confirmar paleta");
  await waitText("Paleta confirmada", 30000);
  await clickButton("Generar capas");
  await waitText("Regenerar capas", 60000);
  await clickButton("Abrir en el Workspace");
  await page.waitForSelector('.editor-shell [aria-label="Canvas del documento"] canvas', { timeout: 30000 });
  await page.waitForFunction(() => {
    const images = [...document.querySelectorAll('.preview-navigator__layer')];
    return images.length > 0 && images.every((image) => image.complete && image.naturalWidth > 0);
  }, { timeout: 30000 });
  await page.waitForFunction(() => {
    const canvas = document.querySelector('.vector-canvas-2 canvas');
    if (!canvas) return false;
    const context = canvas.getContext('2d');
    if (!context) return false;
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
    for (let i = 3; i < pixels.length; i += 400) if (pixels[i] > 0) return true;
    return false;
  }, { timeout: 30000 });
  const layers = await count(".editor-layers-panel__row");
  if (layers < 4 || layers > 6) throw new Error(`Se esperaban 4–6 capas, hay ${layers}`);
  const firstName = await page.$eval(".editor-layers-panel__name", (el) => el.textContent?.trim() ?? "");
  const select = await page.$(`button[aria-label="Seleccionar la capa ${firstName}"]`);
  if (!select) throw new Error("No se puede seleccionar una capa.");
  await select.click();
  const pressed = await page.$eval(`button[aria-label="Seleccionar la capa ${firstName}"]`, (el) => el.getAttribute("aria-pressed"));
  if (pressed !== "true") throw new Error("La selección de capa no se refleja en Layers.");
  const hide = await page.$(`button[aria-label="Ocultar la capa ${firstName}"]`);
  await hide.click();
  await page.waitForSelector(`button[aria-label="Mostrar la capa ${firstName}"]`, { timeout: 10000 });
  await page.$eval(`button[aria-label="Mostrar la capa ${firstName}"]`, (el) => el.click());
  await page.waitForSelector(`button[aria-label="Ocultar la capa ${firstName}"]`, { timeout: 10000 });
  const lock = await page.$(`button[aria-label="Bloquear la capa ${firstName}"]`);
  await lock.click();
  await page.waitForSelector(`button[aria-label="Desbloquear la capa ${firstName}"]`, { timeout: 10000 });
  await page.$eval(`button[aria-label="Ocultar la capa ${firstName}"]`, (el) => el.click());
  await page.waitForSelector(`button[aria-label="Mostrar la capa ${firstName}"]`, { timeout: 10000 });
  await clickButton("← Projects");
  await clickButton("Abrir en el Workspace");
  await page.waitForSelector(`button[aria-label="Mostrar la capa ${firstName}"]`, { timeout: 30000 });
  await page.waitForSelector(`button[aria-label="Desbloquear la capa ${firstName}"]`, { timeout: 10000 });
  await page.$eval(`button[aria-label="Mostrar la capa ${firstName}"]`, (el) => el.click());
  await clickButton("FIT");
  const disabledActions = await page.evaluate(() => [...document.querySelectorAll(".editor-header button[disabled]")].map((el) => el.textContent?.trim()));
  if (!disabledActions.includes("SAVE") || !disabledActions.includes("EXPORT")) throw new Error("SAVE/EXPORT no indican honestamente que están pendientes.");
  const screenshot = `${process.env.TEMP ?? "/tmp"}/vectify-mvp21-workspace-qa.png`;
  await page.screenshot({ path: screenshot, fullPage: true });
  const workspaceUrl = page.url();
  await page.reload({ waitUntil: "networkidle2" });
  await page.waitForSelector('.editor-layers-panel__row', { timeout: 30000 });
  const restoredAfterReload = (await page.$('.editor-shell [aria-label="Canvas del documento"]')) !== null;
  const restoredLayers = await count('.editor-layers-panel__row');
  const restoredLock = (await page.$(`button[aria-label="Desbloquear la capa ${firstName}"]`)) !== null;
  if (!restoredAfterReload || restoredLayers !== layers || !restoredLock) throw new Error('La URL no reconstruyó el Workspace y su layout persistido.');
  console.log(JSON.stringify({ result: "ok", layers, firstName, expectedColors: expected, workspaceUrl, restoredAfterReload, restoredLayers, restoredLock, apiFailures: api.filter((r) => r.status >= 400), pageErrors: errors, screenshot }, null, 2));
  if (errors.length) process.exitCode = 1;
} catch (error) {
  console.error(error);
  console.error(`Texto visible: ${(await page.evaluate(() => document.body.innerText).catch(() => "" )).slice(0, 3000)}`);
  process.exitCode = 1;
} finally {
  await browser.close();
}
