import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { Card, CardFields, Column, Label, Project, Status } from "./api";
import { PRIORITIES, PRIORITY_NAMES, errorMessage } from "./api";
import { longDateTime, toggleChecklistItem } from "./format";
import { Comments } from "./Comments";
import { LabelPicker } from "./LabelPicker";
import { MarkdownView } from "./Markdown";
import { ProjectSelect } from "./Projects";

interface Props {
  /** The card to edit, or null to make a new one in `newStatus`. */
  card: Card | null;
  newStatus: Status;
  /** The project a new card starts in. */
  newProjectId: number | null;
  columns: Column[];
  labels: Label[];
  projects: Project[];
  onClose: () => void;
  onCreate: (fields: CardFields & { status: Status }) => Promise<void>;
  onSave: (id: number, fields: CardFields) => Promise<Card>;
  onMove: (id: number, status: Status) => void;
  onDelete: (id: number) => Promise<void>;
  onCreateLabel: (name: string) => Promise<Label>;
  onCreateProject: (name: string) => Promise<Project>;
  /** A comment was added or deleted. */
  onCommentsChange: () => void;
}

type SaveState = { kind: "idle" } | { kind: "saving" } | { kind: "saved" } | { kind: "error"; message: string };

const AUTOSAVE_MS = 700;

function fieldsOf(card: Card | null, newProjectId: number | null = null): CardFields {
  return card
    ? {
        title: card.title,
        description: card.description,
        priority: card.priority,
        dueDate: card.dueDate,
        labelIds: card.labelIds,
        projectId: card.projectId,
      }
    : { title: "", description: "", priority: "medium", dueDate: null, labelIds: [], projectId: newProjectId };
}

/**
 * The card editor, a panel over the right of the board. An existing card saves itself as you
 * type; a new one is made with "Create card".
 */
export function CardPanel(props: Props) {
  const { card, columns, labels, onClose } = props;
  const isNew = card === null;
  const [draft, setDraft] = useState<CardFields>(() => fieldsOf(card, props.newProjectId));
  const [status, setStatus] = useState<Status>(card?.status ?? props.newStatus);
  const [editingDescription, setEditingDescription] = useState(isNew || !card.description.trim());
  const [save, setSave] = useState<SaveState>({ kind: "idle" });
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const title = useRef<HTMLTextAreaElement>(null);

  const saved = useRef(JSON.stringify(fieldsOf(card)));
  const queue = useRef<Promise<void>>(Promise.resolve());
  const onSave = useRef(props.onSave);
  onSave.current = props.onSave;
  const cardId = card?.id;

  const set = <K extends keyof CardFields>(key: K, value: CardFields[K]) =>
    setDraft((d) => ({ ...d, [key]: value }));

  // Saves run one after another, so an older draft never lands after a newer one.
  const saveNow = useCallback(
    (fields: CardFields): Promise<void> => {
      if (cardId === undefined) return Promise.resolve();
      queue.current = queue.current.then(async () => {
        const json = JSON.stringify(fields);
        if (json === saved.current || !fields.title.trim()) return;
        setSave({ kind: "saving" });
        try {
          await onSave.current(cardId, fields);
          saved.current = json;
          setSave({ kind: "saved" });
        } catch (e) {
          setSave({ kind: "error", message: errorMessage(e) });
        }
      });
      return queue.current;
    },
    [cardId],
  );

  useEffect(() => {
    if (isNew || JSON.stringify(draft) === saved.current) return;
    const timer = setTimeout(() => void saveNow(draft), AUTOSAVE_MS);
    return () => clearTimeout(timer);
  }, [draft, isNew, saveNow]);

  useEffect(() => {
    if (isNew) title.current?.focus();
  }, [isNew]);

  useLayoutEffect(() => {
    if (title.current) autosize(title.current);
  }, [draft.title]);

  const close = useCallback(async () => {
    if (!isNew && draft.title.trim()) await saveNow(draft);
    onClose();
  }, [draft, isNew, onClose, saveNow]);

  const create = async () => {
    if (!draft.title.trim() || creating) return;
    setCreating(true);
    setCreateError(null);
    try {
      await props.onCreate({ ...draft, status });
      onClose();
    } catch (e) {
      setCreateError(errorMessage(e));
      setCreating(false);
    }
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !e.defaultPrevented) {
        void close();
      } else if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        if (isNew) void create();
        else void saveNow(draft);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  const changeStatus = (next: Status) => {
    setStatus(next);
    if (card && next !== card.status) props.onMove(card.id, next);
  };

  const titleMissing = !draft.title.trim();

  return (
    <div className="panel-backdrop" onMouseDown={(e) => e.target === e.currentTarget && void close()}>
      <aside className="panel" data-priority={draft.priority} role="dialog" aria-modal="true" aria-label={isNew ? "New card" : `Card #${card.id}`}>
        <header className="panel-head">
          <span className="panel-number">{isNew ? "New card" : `#${card.id}`}</span>
          {!isNew && <SaveIndicator state={save} />}
          <button type="button" className="quiet" onClick={() => void close()}>
            Close
          </button>
        </header>

        <div className="panel-body">
          <textarea
            ref={title}
            className="panel-title"
            value={draft.title}
            rows={1}
            placeholder="Card title"
            aria-label="Title"
            aria-invalid={titleMissing && !isNew}
            onChange={(e) => set("title", e.target.value.replace(/\n/g, " "))}
          />
          {titleMissing && !isNew && <p className="field-error">A card needs a title. Changes aren't saved until it has one.</p>}

          <div className="panel-fields">
            <label className="project-field">
              <span>Project</span>
              <ProjectSelect
                projects={props.projects}
                value={draft.projectId}
                onChange={(id) => set("projectId", id)}
                onCreate={props.onCreateProject}
              />
            </label>
            <label>
              <span>Column</span>
              <select value={status} onChange={(e) => changeStatus(e.target.value as Status)}>
                {columns.map((col) => (
                  <option key={col.id} value={col.id}>
                    {col.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              <span>Priority</span>
              <select value={draft.priority} onChange={(e) => set("priority", e.target.value as CardFields["priority"])}>
                {PRIORITIES.map((p) => (
                  <option key={p} value={p}>
                    {PRIORITY_NAMES[p]}
                  </option>
                ))}
              </select>
            </label>
            <label>
              <span>Due</span>
              <span className="due-input">
                <input type="date" value={draft.dueDate ?? ""} onChange={(e) => set("dueDate", e.target.value || null)} />
                {draft.dueDate && (
                  <button type="button" className="quiet small" onClick={() => set("dueDate", null)}>
                    Clear
                  </button>
                )}
              </span>
            </label>
          </div>

          <div className="panel-section">
            <h3>Labels</h3>
            <LabelPicker
              labels={labels}
              selected={draft.labelIds}
              onChange={(ids) => set("labelIds", ids)}
              onCreate={props.onCreateLabel}
            />
          </div>

          <div className="panel-section">
            <div className="section-head">
              <h3>Description</h3>
              {draft.description.trim() && (
                <button type="button" className="quiet small" onClick={() => setEditingDescription(!editingDescription)}>
                  {editingDescription ? "Preview" : "Edit"}
                </button>
              )}
            </div>
            {editingDescription ? (
              <textarea
                className="panel-description"
                value={draft.description}
                rows={10}
                placeholder={"Notes, links, a checklist…\n\n- [ ] first step\n- [ ] second step"}
                aria-label="Description (Markdown)"
                onChange={(e) => set("description", e.target.value)}
              />
            ) : (
              <div
                className="description-view"
                title="Click to edit"
                onClick={(e) => {
                  if (!(e.target as HTMLElement).closest("a, input")) setEditingDescription(true);
                }}
              >
                <MarkdownView
                  source={draft.description}
                  onToggleTask={(i) => set("description", toggleChecklistItem(draft.description, i))}
                />
              </div>
            )}
            <p className="hint">Markdown: **bold**, [links](https://…), and “- [ ]” for checklist items.</p>
          </div>

          {!isNew && (
            <div className="panel-section">
              <h3>Comments</h3>
              <Comments cardId={card.id} onChange={props.onCommentsChange} />
            </div>
          )}
        </div>

        <footer className="panel-foot">
          {isNew ? (
            <>
              {createError && <p className="field-error">{createError}</p>}
              <button type="button" className="primary" disabled={titleMissing || creating} onClick={() => void create()}>
                Create card
              </button>
              <button type="button" className="quiet" onClick={onClose}>
                Cancel
              </button>
            </>
          ) : confirmDelete ? (
            <div className="confirm">
              <span>Delete #{card.id} for good?</span>
              <button type="button" className="danger" onClick={() => void props.onDelete(card.id)}>
                Delete card
              </button>
              <button type="button" className="quiet" onClick={() => setConfirmDelete(false)}>
                Keep it
              </button>
            </div>
          ) : (
            <>
              <span className="panel-meta">
                <span>Created {longDateTime(card.createdAt)}</span>
                {card.completedAt && <span>Finished {longDateTime(card.completedAt)}</span>}
              </span>
              <button type="button" className="quiet danger-text" onClick={() => setConfirmDelete(true)}>
                Delete
              </button>
            </>
          )}
        </footer>
      </aside>
    </div>
  );
}

function SaveIndicator({ state }: { state: SaveState }) {
  const text =
    state.kind === "saving" ? "Saving…" : state.kind === "saved" ? "Saved" : state.kind === "error" ? `Not saved: ${state.message}` : "";
  return (
    <span className={`save-state ${state.kind}`} role="status">
      {text}
    </span>
  );
}

function autosize(el: HTMLTextAreaElement) {
  el.style.height = "auto";
  el.style.height = `${el.scrollHeight}px`;
}
