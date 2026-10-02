import { afterEach, describe, expect, it } from "vitest";
import { buildWorkspaceSearch, clearWorkspaceLocation, pushWorkspaceLocation, readWorkspaceLocation } from "./workspaceLocation";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const SAVED_PROJECT_ID = "44444444-4444-4444-4444-444444444444";

afterEach(() => {
  window.history.pushState({}, "", "/");
});

describe("readWorkspaceLocation", () => {
  it("devuelve null si no hay query string", () => {
    expect(readWorkspaceLocation("")).toBeNull();
  });

  it("devuelve null si falta cualquiera de los tres ids (deep-link parcial)", () => {
    expect(readWorkspaceLocation(`?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}`)).toBeNull();
    expect(readWorkspaceLocation(`?projectId=${PROJECT_ID}&paletteId=${PALETTE_ID}`)).toBeNull();
    expect(readWorkspaceLocation(`?imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}`)).toBeNull();
  });

  it("lee projectId/imageId/paletteId cuando los tres están presentes", () => {
    expect(readWorkspaceLocation(`?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}`)).toEqual({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      paletteId: PALETTE_ID,
    });
  });

  it("omite savedProjectId del resultado cuando no está presente en la URL (M2.2-S05)", () => {
    const location = readWorkspaceLocation(`?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}`);
    expect(location).not.toHaveProperty("savedProjectId");
  });

  it("lee savedProjectId además del triple clásico cuando está presente (M2.2-S05, reapertura)", () => {
    const location = readWorkspaceLocation(
      `?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}&savedProjectId=${SAVED_PROJECT_ID}`,
    );
    expect(location).toEqual({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      paletteId: PALETTE_ID,
      savedProjectId: SAVED_PROJECT_ID,
    });
  });

  it("savedProjectId solo no alcanza para reconstruir el Workspace (el triple clásico sigue siendo obligatorio)", () => {
    expect(readWorkspaceLocation(`?savedProjectId=${SAVED_PROJECT_ID}`)).toBeNull();
  });
});

describe("buildWorkspaceSearch", () => {
  it("construye una query string con los tres ids", () => {
    const search = buildWorkspaceSearch({ projectId: PROJECT_ID, imageId: IMAGE_ID, paletteId: PALETTE_ID });
    expect(search).toBe(`?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}`);
  });

  it("agrega savedProjectId a la query string cuando está presente (M2.2-S05)", () => {
    const search = buildWorkspaceSearch({
      projectId: PROJECT_ID, imageId: IMAGE_ID, paletteId: PALETTE_ID, savedProjectId: SAVED_PROJECT_ID,
    });
    expect(search).toBe(`?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}&savedProjectId=${SAVED_PROJECT_ID}`);
  });
});

describe("pushWorkspaceLocation / clearWorkspaceLocation", () => {
  it("pushWorkspaceLocation actualiza window.location.search y es releíble con readWorkspaceLocation", () => {
    pushWorkspaceLocation({ projectId: PROJECT_ID, imageId: IMAGE_ID, paletteId: PALETTE_ID });

    expect(window.location.search).toBe(`?projectId=${PROJECT_ID}&imageId=${IMAGE_ID}&paletteId=${PALETTE_ID}`);
    expect(readWorkspaceLocation()).toEqual({ projectId: PROJECT_ID, imageId: IMAGE_ID, paletteId: PALETTE_ID });
  });

  it("clearWorkspaceLocation quita la query string (vuelve al flujo clásico)", () => {
    pushWorkspaceLocation({ projectId: PROJECT_ID, imageId: IMAGE_ID, paletteId: PALETTE_ID });
    clearWorkspaceLocation();

    expect(window.location.search).toBe("");
    expect(readWorkspaceLocation()).toBeNull();
  });

  it("pushWorkspaceLocation con savedProjectId queda releíble (M2.2-S05, primer Save exitoso sin recargar la página)", () => {
    pushWorkspaceLocation({ projectId: PROJECT_ID, imageId: IMAGE_ID, paletteId: PALETTE_ID, savedProjectId: SAVED_PROJECT_ID });

    expect(readWorkspaceLocation()).toEqual({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      paletteId: PALETTE_ID,
      savedProjectId: SAVED_PROJECT_ID,
    });
  });
});
