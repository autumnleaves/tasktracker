import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { Board as BoardData, Card, CardFields, Label, Priority, Status } from "./api";
import { PRIORITIES, PRIORITY_NAMES, STATUSES, api, errorMessage } from "./api";
import { Board } from "./Board";
import { CardPanel } from "./CardPanel";
import { LabelChip, nextLabelColor } from "./LabelChip";
import { LabelsDialog } from "./LabelsDialog";

type Panel = { kind: "edit"; id: number } | { kind: "new"; status: Status } | null;

interface Toast {
  id: number;
  text: string;
}

const byPosition = (a: Card, b: Card) => a.position - b.position || a.id - b.id;

// The open card is in the address (?card=12), so it survives a reload and can be bookmarked.
function cardFromUrl(): number | null {
  const value = new URLSearchParams(window.location.search).get("card");
  return value && /^\d+$/.test(value) ? Number(value) : null;
}

function setCardInUrl(id: number | null) {
  const url = new URL(window.location.href);
  if (id === null) url.searchParams.delete("card");
  else url.searchParams.set("card", String(id));
  window.history.replaceState(null, "", url);
}

export function App() {
  const [board, setBoard] = useState<BoardData | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [panel, setPanel] = useState<Panel>(() => {
    const id = cardFromUrl();
    return id === null ? null : { kind: "edit", id };
  });
  const [labelsOpen, setLabelsOpen] = useState(false);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [query, setQuery] = useState("");
  const [labelFilter, setLabelFilter] = useState<number[]>([]);
  const [priorityFilter, setPriorityFilter] = useState<Priority[]>([]);
  const search = useRef<HTMLInputElement>(null);

  const toast = useCallback((text: string) => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, text }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 6000);
  }, []);

  const refresh = useCallback(async () => {
    try {
      setBoard(await api.board());
      setLoadError(null);
    } catch (e) {
      setLoadError(errorMessage(e));
    }
  }, []);

  useEffect(() => {
    void refresh();
    // Pick up changes made in another tab or on another device.
    const onVisible = () => document.visibilityState === "visible" && void refresh();
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [refresh]);

  useEffect(() => setCardInUrl(panel?.kind === "edit" ? panel.id : null), [panel]);

  // Runs a change, reports a failure, and reloads the board either way so it matches the server.
  const mutate = useCallback(
    async <T,>(action: () => Promise<T>, rethrow = false): Promise<T | undefined> => {
      try {
        return await action();
      } catch (e) {
        if (rethrow) throw e;
        toast(errorMessage(e));
        return undefined;
      } finally {
        void refresh();
      }
    },
    [refresh, toast],
  );

  const replaceCard = (card: Card) =>
    setBoard((b) => b && { ...b, cards: b.cards.map((c) => (c.id === card.id ? card : c)) });

  // Puts the card at `index` in `status` locally, then tells the server.
  const moveTo = (id: number, status: Status, index?: number) => {
    if (!board) return;
    const others = board.cards.filter((c) => c.status === status && c.id !== id).sort(byPosition).map((c) => c.id);
    const at = index === undefined ? others.length : Math.max(0, Math.min(index, others.length));
    const card = board.cards.find((c) => c.id === id);
    if (!card) return;
    const before = board.cards.filter((c) => c.status === status).sort(byPosition).map((c) => c.id);
    if (card.status === status && before.indexOf(id) === at) return; // dropped where it was

    others.splice(at, 0, id);
    const positions = new Map(others.map((cid, i) => [cid, i + 1]));
    setBoard({
      ...board,
      cards: board.cards.map((c) =>
        positions.has(c.id) ? { ...c, status: c.id === id ? status : c.status, position: positions.get(c.id)! } : c,
      ),
    });
    void mutate(() => api.moveCard(id, status, at));
  };

  // A drop in a filtered column: place the card next to the visible neighbour it landed by.
  const onDrop = (id: number, status: Status, visibleOrder: number[]) => {
    if (!board) return;
    const full = board.cards.filter((c) => c.status === status && c.id !== id).sort(byPosition).map((c) => c.id);
    const i = visibleOrder.indexOf(id);
    let index = full.length;
    if (i >= 0 && i < visibleOrder.length - 1) index = full.indexOf(visibleOrder[i + 1]);
    else if (i > 0) index = full.indexOf(visibleOrder[i - 1]) + 1;
    moveTo(id, status, index < 0 ? full.length : index);
  };

  const createLabel = async (name: string, color?: string): Promise<Label> => {
    const label = await api.createLabel(name, color ?? nextLabelColor(board?.labels ?? []));
    setBoard((b) => b && { ...b, labels: [...b.labels, label].sort((x, y) => x.name.localeCompare(y.name)) });
    return label;
  };

  // Keyboard: "c" makes a card, "/" jumps to search.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (panel || labelsOpen || e.ctrlKey || e.metaKey || e.altKey) return;
      if (target.closest("input, textarea, select, [contenteditable]")) return;
      if (e.key === "c") {
        e.preventDefault();
        setPanel({ kind: "new", status: "backlog" });
      } else if (e.key === "/") {
        e.preventDefault();
        search.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [panel, labelsOpen]);

  const labelMap = useMemo(() => new Map((board?.labels ?? []).map((l) => [l.id, l])), [board]);
  const filtering = query.trim() !== "" || labelFilter.length > 0 || priorityFilter.length > 0;

  const { lanes, totals } = useMemo(() => {
    const q = query.trim().toLowerCase();
    const number = q.startsWith("#") ? q.slice(1) : q;
    const matches = (c: Card) =>
      (!q || c.title.toLowerCase().includes(q) || c.description.toLowerCase().includes(q) || String(c.id) === number) &&
      (labelFilter.length === 0 || labelFilter.every((id) => c.labelIds.includes(id))) &&
      (priorityFilter.length === 0 || priorityFilter.includes(c.priority));
    const lanes = {} as Record<Status, Card[]>;
    const totals = {} as Record<Status, number>;
    for (const status of STATUSES) {
      const all = (board?.cards ?? []).filter((c) => c.status === status).sort(byPosition);
      lanes[status] = all.filter(matches);
      totals[status] = all.length;
    }
    return { lanes, totals };
  }, [board, query, labelFilter, priorityFilter]);

  const usage = useMemo(() => {
    const counts = new Map<number, number>();
    for (const c of board?.cards ?? []) for (const id of c.labelIds) counts.set(id, (counts.get(id) ?? 0) + 1);
    return counts;
  }, [board]);

  if (!board) {
    return (
      <main className="status-page">
        {loadError ? (
          <>
            <h1>The board didn't load</h1>
            <p>{loadError}</p>
            <p className="hint">On cargoship: sudo docker compose -p tasktracker ps</p>
            <button type="button" className="primary" onClick={() => void refresh()}>
              Try again
            </button>
          </>
        ) : (
          <p className="hint">Loading the board…</p>
        )}
      </main>
    );
  }

  const openCard = panel?.kind === "edit" ? board.cards.find((c) => c.id === panel.id) : undefined;
  const toggle = <T,>(list: T[], value: T) => (list.includes(value) ? list.filter((x) => x !== value) : [...list, value]);

  return (
    <div className="app">
      <header className="topbar">
        <h1>Tasks</h1>
        <div className="filters">
          <input
            ref={search}
            type="search"
            className="search"
            value={query}
            placeholder="Search cards  ( / )"
            aria-label="Search cards"
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => e.key === "Escape" && (setQuery(""), e.currentTarget.blur())}
          />
          <div className="filter-group" role="group" aria-label="Filter by priority">
            {PRIORITIES.filter((p) => p !== "medium").map((p) => (
              <button
                key={p}
                type="button"
                className="filter-chip"
                data-priority={p}
                aria-pressed={priorityFilter.includes(p)}
                onClick={() => setPriorityFilter(toggle(priorityFilter, p))}
              >
                {PRIORITY_NAMES[p]}
              </button>
            ))}
          </div>
          {board.labels.length > 0 && (
            <div className="filter-group" role="group" aria-label="Filter by label">
              {board.labels.map((label) => (
                <button
                  key={label.id}
                  type="button"
                  className="filter-chip label-filter"
                  aria-pressed={labelFilter.includes(label.id)}
                  onClick={() => setLabelFilter(toggle(labelFilter, label.id))}
                >
                  <LabelChip label={label} />
                </button>
              ))}
            </div>
          )}
          {filtering && (
            <button
              type="button"
              className="quiet small"
              onClick={() => {
                setQuery("");
                setLabelFilter([]);
                setPriorityFilter([]);
              }}
            >
              Clear filters
            </button>
          )}
        </div>
        <div className="topbar-actions">
          <button type="button" className="quiet" onClick={() => setLabelsOpen(true)}>
            Labels
          </button>
          <button type="button" className="primary" title="Shortcut: C" onClick={() => setPanel({ kind: "new", status: "backlog" })}>
            New card
          </button>
        </div>
      </header>

      <Board
        columns={board.columns}
        lanes={lanes}
        totals={totals}
        labels={labelMap}
        filtering={filtering}
        onOpen={(id) => setPanel({ kind: "edit", id })}
        onMove={onDrop}
        onQuickAdd={async (status, title) => {
          await mutate(() => api.createCard({ title, description: "", priority: "medium", dueDate: null, labelIds: [], status }));
        }}
      />

      {panel?.kind === "edit" && !openCard && (
        <NotFound id={panel.id} onClose={() => setPanel(null)} />
      )}
      {(panel?.kind === "new" || openCard) && (
        <CardPanel
          key={openCard ? openCard.id : "new"}
          card={openCard ?? null}
          newStatus={panel?.kind === "new" ? panel.status : "backlog"}
          columns={board.columns}
          labels={board.labels}
          onClose={() => setPanel(null)}
          onCreate={async (fields) => {
            await mutate(() => api.createCard(fields), true);
          }}
          onSave={async (id: number, fields: CardFields) => {
            const card = await api.updateCard(id, fields);
            replaceCard(card);
            return card;
          }}
          onMove={(id, status) => moveTo(id, status)}
          onDelete={async (id) => {
            await mutate(() => api.deleteCard(id));
            setPanel(null);
          }}
          onCreateLabel={(name) => createLabel(name)}
        />
      )}
      {labelsOpen && (
        <LabelsDialog
          labels={board.labels}
          usage={usage}
          onClose={() => setLabelsOpen(false)}
          onCreate={(name, color) => mutate(() => createLabel(name, color), true)}
          onUpdate={(id, name, color) => mutate(() => api.updateLabel(id, name, color), true)}
          onDelete={async (id) => {
            await mutate(() => api.deleteLabel(id), true);
            setLabelFilter((f) => f.filter((x) => x !== id));
          }}
        />
      )}

      <div className="toasts" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className="toast">
            {t.text}
          </div>
        ))}
      </div>
    </div>
  );
}

function NotFound({ id, onClose }: { id: number; onClose: () => void }) {
  return (
    <div className="panel-backdrop centered" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog" role="dialog" aria-modal="true">
        <p>There's no card #{id}. It may have been deleted.</p>
        <button type="button" className="primary" onClick={onClose}>
          Back to the board
        </button>
      </div>
    </div>
  );
}
