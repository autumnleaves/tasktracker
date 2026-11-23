// The task tracker API (src/TaskTracker.Api). Always same-origin under /api: the API serves this
// app in production, and Vite's dev server proxies /api to it in development.

export type Status = "backlog" | "todo" | "in_progress" | "done";
export type Priority = "low" | "medium" | "high" | "urgent";

export const STATUSES: Status[] = ["backlog", "todo", "in_progress", "done"];
export const PRIORITIES: Priority[] = ["urgent", "high", "medium", "low"];
export const PRIORITY_NAMES: Record<Priority, string> = {
  urgent: "Urgent",
  high: "High",
  medium: "Medium",
  low: "Low",
};

export interface Column {
  id: Status;
  name: string;
}

export interface Label {
  id: number;
  name: string;
  color: string;
}

export interface Card {
  id: number;
  title: string;
  description: string;
  status: Status;
  priority: Priority;
  /** yyyy-mm-dd, or null */
  dueDate: string | null;
  position: number;
  labelIds: number[];
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  /** The labels' names, in labelIds order. */
  labels: string[];
  commentCount: number;
}

export interface Comment {
  id: number;
  cardId: number;
  author: string;
  body: string;
  createdAt: string;
}

export interface Board {
  columns: Column[];
  cards: Card[];
  labels: Label[];
}

/** The fields a card's editor changes. Its column and order change through a move. */
export interface CardFields {
  title: string;
  description: string;
  priority: Priority;
  dueDate: string | null;
  labelIds: number[];
}

export class ApiError extends Error {
  readonly status: number;
  readonly fields: Record<string, string[]>;

  constructor(message: string, status: number, fields: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.fields = fields;
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError("Can't reach the task tracker. Check your connection and try again.", 0);
  }

  if (!response.ok) {
    let message = `The server answered ${response.status}.`;
    let fields: Record<string, string[]> = {};
    try {
      const problem = await response.json();
      if (problem.errors) {
        fields = problem.errors;
        message = Object.values(fields).flat().join(" ");
      } else if (problem.detail) {
        message = problem.detail;
      } else if (response.status === 404) {
        message = "That card no longer exists.";
      }
    } catch {
      // Not JSON: keep the generic message.
    }
    throw new ApiError(message, response.status, fields);
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  board: () => request<Board>("GET", "/board"),
  createCard: (fields: CardFields & { status: Status }) => request<Card>("POST", "/cards", fields),
  updateCard: (id: number, fields: CardFields) => request<Card>("PUT", `/cards/${id}`, fields),
  moveCard: (id: number, status: Status, index?: number) =>
    request<Card>("POST", `/cards/${id}/move`, { status, index }),
  deleteCard: (id: number) => request<void>("DELETE", `/cards/${id}`),
  createLabel: (name: string, color: string) => request<Label>("POST", "/labels", { name, color }),
  updateLabel: (id: number, name: string, color: string) =>
    request<Label>("PUT", `/labels/${id}`, { name, color }),
  deleteLabel: (id: number) => request<void>("DELETE", `/labels/${id}`),
  comments: (cardId: number) => request<Comment[]>("GET", `/cards/${cardId}/comments`),
  addComment: (cardId: number, body: string) =>
    request<Comment>("POST", `/cards/${cardId}/comments`, { body, author: "Tom" }),
  deleteComment: (id: number) => request<void>("DELETE", `/comments/${id}`),
};

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
