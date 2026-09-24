export type StatusTone = "ok" | "warn" | "error" | "neutral";

interface StatusPillProps {
  label: string;
  tone: StatusTone;
}

const TONE_CLASS: Record<StatusTone, string> = {
  ok: "status-pill status-pill--ok",
  warn: "status-pill status-pill--warn",
  error: "status-pill status-pill--error",
  neutral: "status-pill status-pill--neutral",
};

export function StatusPill({ label, tone }: StatusPillProps) {
  return (
    <span className={TONE_CLASS[tone]} role="status">
      <span className="status-pill__dot" aria-hidden="true" />
      {label}
    </span>
  );
}
