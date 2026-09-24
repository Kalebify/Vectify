import type { ReactNode } from "react";
import { StatusPill, type StatusTone } from "./StatusPill";

interface ServiceCardProps {
  title: string;
  tone: StatusTone;
  statusLabel: string;
  children?: ReactNode;
}

export function ServiceCard({ title, tone, statusLabel, children }: ServiceCardProps) {
  return (
    <article className="service-card" aria-label={title}>
      <header className="service-card__header">
        <h2>{title}</h2>
        <StatusPill label={statusLabel} tone={tone} />
      </header>
      <div className="service-card__body">{children}</div>
    </article>
  );
}
