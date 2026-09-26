import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useExplodedView } from "./useExplodedView";

describe("useExplodedView — estado inicial", () => {
  it("arranca en vista ensamblada con una separación por defecto razonable", () => {
    const { result } = renderHook(() => useExplodedView());

    expect(result.current.viewMode).toBe("assembled");
    expect(result.current.separationPercent).toBeGreaterThan(0);
  });
});

describe("useExplodedView — toggle de vista", () => {
  it("toggleViewMode alterna entre ensamblada y explotada", () => {
    const { result } = renderHook(() => useExplodedView());

    act(() => result.current.toggleViewMode());
    expect(result.current.viewMode).toBe("exploded");

    act(() => result.current.toggleViewMode());
    expect(result.current.viewMode).toBe("assembled");
  });

  it("setViewMode fija el modo explícitamente", () => {
    const { result } = renderHook(() => useExplodedView());

    act(() => result.current.setViewMode("exploded"));
    expect(result.current.viewMode).toBe("exploded");

    act(() => result.current.setViewMode("exploded"));
    expect(result.current.viewMode).toBe("exploded");
  });
});

describe("useExplodedView — separación", () => {
  it("setSeparationPercent acepta cualquier valor no negativo, sin límite superior", () => {
    const { result } = renderHook(() => useExplodedView());

    act(() => result.current.setSeparationPercent(0));
    expect(result.current.separationPercent).toBe(0);

    act(() => result.current.setSeparationPercent(5000));
    expect(result.current.separationPercent).toBe(5000);
  });

  it("ignora valores negativos o no finitos, sin romper el estado", () => {
    const { result } = renderHook(() => useExplodedView());
    act(() => result.current.setSeparationPercent(40));

    act(() => result.current.setSeparationPercent(-10));
    expect(result.current.separationPercent).toBe(40);

    act(() => result.current.setSeparationPercent(Number.NaN));
    expect(result.current.separationPercent).toBe(40);

    act(() => result.current.setSeparationPercent(Number.POSITIVE_INFINITY));
    expect(result.current.separationPercent).toBe(40);
  });
});
