import { useId, useRef, useState } from "react";
import type { DragEvent, KeyboardEvent } from "react";
import { ALLOWED_CONTENT_TYPES } from "../../lib/validateImageFile";

interface DropzoneProps {
  onFileSelected: (file: File) => void;
  disabled?: boolean;
}

const ACCEPT_ATTRIBUTE = [...ALLOWED_CONTENT_TYPES, ".png", ".jpg", ".jpeg", ".webp"].join(",");

/**
 * Área de arrastrar/soltar o seleccionar un archivo. Estados: idle, hover (drag
 * activo), focus (teclado) y disabled (mientras hay una carga en curso). El error
 * de validación se muestra fuera de este componente (UploadPanel), que además
 * controla el estado "selected"/"invalid" real del flujo.
 */
export function Dropzone({ onFileSelected, disabled = false }: DropzoneProps) {
  const [isDraggingOver, setIsDraggingOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const inputId = useId();

  const openFileBrowser = () => {
    if (!disabled) {
      inputRef.current?.click();
    }
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setIsDraggingOver(false);
    if (disabled) return;

    const droppedFile = event.dataTransfer.files?.[0];
    if (droppedFile) {
      onFileSelected(droppedFile);
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      openFileBrowser();
    }
  };

  return (
    <div
      className={`dropzone${isDraggingOver ? " dropzone--active" : ""}${disabled ? " dropzone--disabled" : ""}`}
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-disabled={disabled}
      aria-labelledby={`${inputId}-label`}
      aria-describedby={`${inputId}-hint`}
      onClick={openFileBrowser}
      onKeyDown={handleKeyDown}
      onDragOver={(event) => {
        event.preventDefault();
        if (!disabled) setIsDraggingOver(true);
      }}
      onDragLeave={() => setIsDraggingOver(false)}
      onDrop={handleDrop}
    >
      <input
        ref={inputRef}
        id={inputId}
        type="file"
        accept={ACCEPT_ATTRIBUTE}
        disabled={disabled}
        className="dropzone__input"
        onChange={(event) => {
          const selected = event.target.files?.[0];
          if (selected) {
            onFileSelected(selected);
          }
          // Permite volver a elegir el mismo archivo dos veces seguidas.
          event.target.value = "";
        }}
      />
      <p id={`${inputId}-label`} className="dropzone__label">
        Arrastrá una imagen acá o hacé clic para elegirla
      </p>
      <p id={`${inputId}-hint`} className="dropzone__hint">
        PNG, JPG o WEBP
      </p>
    </div>
  );
}
