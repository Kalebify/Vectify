import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { VectorComparison } from "./VectorComparison";

const realResizeObserver = globalThis.ResizeObserver;

/**
 * Stub de ResizeObserver que dispara la callback sincrónicamente en
 * `observe()` con un tamaño fijo -- simula la primera medición real del
 * contenedor que en el navegador llega async, para poder probar el
 * auto-ajuste a pantalla al montar sin esperar un microtask real.
 */
class ImmediateResizeObserver {
  private readonly callback: ResizeObserverCallback;

  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }

  observe(target: Element) {
    this.callback(
      [{ target, contentRect: { width: 400, height: 300 } } as ResizeObserverEntry],
      this as unknown as ResizeObserver,
    );
  }
  unobserve() {}
  disconnect() {}
}

beforeEach(() => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = ImmediateResizeObserver;
});

afterEach(() => {
  globalThis.ResizeObserver = realResizeObserver;
});

function renderComparison(overrides: Partial<React.ComponentProps<typeof VectorComparison>> = {}) {
  return render(
    <VectorComparison
      originalUrl="/original.png"
      originalAlt="Original de logo.png"
      originalWidth={800}
      originalHeight={600}
      vectorUrl="/vector.svg"
      vectorAlt="SVG vectorizado de logo.png"
      vectorWidth={800}
      vectorHeight={600}
      maxScale={8}
      largeDesignNote={null}
      {...overrides}
    />,
  );
}

describe("VectorComparison — render", () => {
  it("muestra ambos paneles (original y vector) y la barra de herramientas", () => {
    renderComparison();
    expect(screen.getByText("Original")).toBeInTheDocument();
    expect(screen.getByText("SVG vectorizado")).toBeInTheDocument();
    expect(screen.getByRole("toolbar", { name: "Controles de zoom y desplazamiento" })).toBeInTheDocument();
  });

  it("muestra la nota de diseño grande cuando se provee", () => {
    renderComparison({ largeDesignNote: "Diseño grande: el zoom máximo se limitó para mantener la fluidez de la visualización." });
    expect(
      screen.getByText("Diseño grande: el zoom máximo se limitó para mantener la fluidez de la visualización."),
    ).toBeInTheDocument();
  });
});

describe("VectorComparison — ajuste automático a pantalla al montar", () => {
  it("calcula el zoom inicial en base al contenedor medido (contain) en vez de arrancar en 100%", () => {
    // Contenedor medido 400x300 (stub), recurso 800x600 -> ratio 0.5 en ambos ejes.
    renderComparison();
    expect(screen.getByText("50%")).toBeInTheDocument();
  });
});

describe("VectorComparison — barra de herramientas", () => {
  it("Acercar/Alejar cambian el porcentaje mostrado, compartido por ambos paneles", () => {
    renderComparison();
    expect(screen.getByText("50%")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Acercar" }));
    expect(screen.getByText("63%")).toBeInTheDocument(); // round(50 * 1.25)

    fireEvent.click(screen.getByRole("button", { name: "Alejar" }));
    fireEvent.click(screen.getByRole("button", { name: "Alejar" }));
    // Vuelve a bajar respecto del 63% anterior.
    expect(screen.getByText("40%")).toBeInTheDocument();
  });

  it("Restablecer vuelve a escala 1:1 (100%), Ajustar a pantalla vuelve al fit calculado", () => {
    renderComparison();

    fireEvent.click(screen.getByRole("button", { name: "Restablecer" }));
    expect(screen.getByText("100%")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Ajustar a pantalla" }));
    expect(screen.getByText("50%")).toBeInTheDocument();
  });

  it("deshabilita Alejar/Acercar al llegar a los límites de escala", () => {
    renderComparison({ maxScale: 1 });

    // fit inicial (50%) es menor a maxScale=1; varios clics de Acercar deben
    // clampear en el techo (100%) sin superarlo, y deshabilitar el botón.
    const zoomInButton = screen.getByRole("button", { name: "Acercar" });
    fireEvent.click(zoomInButton);
    fireEvent.click(zoomInButton);
    fireEvent.click(zoomInButton);
    fireEvent.click(zoomInButton);

    expect(screen.getByText("100%")).toBeInTheDocument();
    expect(zoomInButton).toBeDisabled();
  });
});
