import type { Card } from "./api";

const DAY_MS = 24 * 60 * 60 * 1000;

/** A yyyy-mm-dd string as a local date at midnight. */
export function parseDay(day: string): Date {
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d);
}

function startOfToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function shortDate(date: Date): string {
  const sameYear = date.getFullYear() === new Date().getFullYear();
  return date.toLocaleDateString("en-AU", {
    day: "numeric",
    month: "short",
    ...(sameYear ? {} : { year: "numeric" }),
  });
}

export type DueState = "overdue" | "soon" | "later";

export function dueInfo(card: Card): { text: string; state: DueState } | null {
  if (!card.dueDate) return null;
  const days = Math.round((parseDay(card.dueDate).getTime() - startOfToday().getTime()) / DAY_MS);
  const state: DueState = card.status === "done" ? "later" : days < 0 ? "overdue" : days <= 2 ? "soon" : "later";
  const text =
    days === 0 ? "Due today" : days === 1 ? "Due tomorrow" : days === -1 ? "Due yesterday" : `Due ${shortDate(parseDay(card.dueDate))}`;
  return { text, state };
}

export function completedText(card: Card): string | null {
  return card.completedAt ? `Done ${shortDate(new Date(card.completedAt))}` : null;
}

export function longDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-AU", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

// "- [ ] thing" / "* [x] thing" lines in a description.
const TASK_LINE = /^(\s*(?:[-*+]|\d+[.)])\s+\[)([ xX])(\])/;

export function checklist(description: string): { done: number; total: number } | null {
  let done = 0;
  let total = 0;
  for (const line of description.split("\n")) {
    const match = TASK_LINE.exec(line);
    if (match) {
      total++;
      if (match[2] !== " ") done++;
    }
  }
  return total > 0 ? { done, total } : null;
}

/** Ticks or unticks the nth (from 0) checklist item in a description. */
export function toggleChecklistItem(description: string, index: number): string {
  let n = 0;
  return description
    .split("\n")
    .map((line) => {
      const match = TASK_LINE.exec(line);
      if (!match) return line;
      if (n++ !== index) return line;
      return line.replace(TASK_LINE, (_, open, mark, close) => `${open}${mark === " " ? "x" : " "}${close}`);
    })
    .join("\n");
}
