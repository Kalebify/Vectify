/**
 * Convierte un string de SVG (ya sanitizado del lado de la Web API/Python) en
 * una data URL utilizable como `src` de un <img>. Mismo criterio de defensa
 * en profundidad que VectorCanvas (M1-S06): nunca se inyecta el SVG inline en
 * el DOM vía dangerouslySetInnerHTML -- se renderiza siempre como imagen, el
 * navegador nunca ejecuta script embebido dentro de un <img>. Se usa para el
 * preview de simplificación (M1-S07), que viaja como texto plano en la
 * respuesta de la Web API en vez de una URL descargable (no se persiste en
 * storage hasta que el usuario confirma "Aplicar").
 */
export function svgToDataUrl(svg: string): string {
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}
