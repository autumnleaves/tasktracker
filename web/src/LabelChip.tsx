import type { CSSProperties, ReactNode } from "react";
import type { Label } from "./api";

export const LABEL_COLORS = ["#3d6fa8", "#2f8f6b", "#b4690e", "#8a4fb5", "#c8323c", "#3f8a9c", "#7d6b22", "#66707a"];

export function nextLabelColor(existing: Label[]): string {
  const used = new Set(existing.map((l) => l.color));
  return LABEL_COLORS.find((c) => !used.has(c)) ?? LABEL_COLORS[existing.length % LABEL_COLORS.length];
}

export function LabelChip({ label, children }: { label: Label; children?: ReactNode }) {
  return (
    <span className="label-chip" style={{ "--label": label.color } as CSSProperties}>
      {label.name}
      {children}
    </span>
  );
}
