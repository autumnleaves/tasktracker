import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  MouseSensor,
  TouchSensor,
  closestCorners,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
  type UniqueIdentifier,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useRef, useState, type KeyboardEvent } from "react";
import type { Card, Column, Label, Project, Status } from "./api";
import { CardTile } from "./CardTile";
import { QuickAdd } from "./QuickAdd";

type Lanes = Record<Status, number[]>;

interface Props {
  columns: Column[];
  /** The cards to show in each column, in order (after filtering). */
  lanes: Record<Status, Card[]>;
  /** How many cards each column has before filtering. */
  totals: Record<Status, number>;
  labels: Map<number, Label>;
  projects: Map<number, Project>;
  filtering: boolean;
  onOpen: (id: number) => void;
  /** A card was dropped: `visibleOrder` is the target column's shown cards, with it in place. */
  onMove: (id: number, status: Status, visibleOrder: number[]) => void;
  onQuickAdd: (status: Status, title: string) => Promise<void>;
}

const COLUMN_PREFIX = "column:";

export function Board({ columns, lanes, totals, labels, projects, filtering, onOpen, onMove, onQuickAdd }: Props) {
  const cards = new Map(Object.values(lanes).flat().map((c) => [c.id, c]));
  const fromProps = Object.fromEntries(
    columns.map((col) => [col.id, lanes[col.id].map((c) => c.id)]),
  ) as Lanes;

  // While dragging, the lanes as the drag has rearranged them; otherwise the props' order.
  const [dragLanes, setDragLanes] = useState<Lanes | null>(null);
  const [activeId, setActiveId] = useState<number | null>(null);
  const current = dragLanes ?? fromProps;

  // A drag ends with a click on the card under the pointer: don't treat that as opening it.
  const lastDragEnd = useRef(0);
  const open = (id: number) => {
    if (Date.now() - lastDragEnd.current > 250) onOpen(id);
  };

  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 6 } }),
    // Space picks a card up (arrows move it, Space drops it, Esc cancels); Enter opens it.
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
      keyboardCodes: { start: ["Space"], cancel: ["Escape"], end: ["Space"] },
    }),
  );

  const laneOf = (id: UniqueIdentifier, lanesNow: Lanes): Status | undefined => {
    if (typeof id === "string" && id.startsWith(COLUMN_PREFIX)) return id.slice(COLUMN_PREFIX.length) as Status;
    return columns.find((col) => lanesNow[col.id].includes(id as number))?.id;
  };

  const handleStart = ({ active }: DragStartEvent) => {
    setActiveId(active.id as number);
    setDragLanes(fromProps);
  };

  // Moving between columns happens while dragging, so the target column opens a gap.
  const handleOver = ({ active, over }: DragOverEvent) => {
    if (!over) return;
    setDragLanes((prev) => {
      const lanesNow = prev ?? fromProps;
      const from = laneOf(active.id, lanesNow);
      const to = laneOf(over.id, lanesNow);
      if (!from || !to || from === to) return prev;

      const id = active.id as number;
      const target = lanesNow[to].filter((x) => x !== id);
      const overIndex = target.indexOf(over.id as number);
      let insertAt = target.length;
      if (overIndex >= 0) {
        const activeRect = active.rect.current.translated;
        const below = activeRect && activeRect.top > over.rect.top + over.rect.height / 2;
        insertAt = overIndex + (below ? 1 : 0);
      }
      target.splice(insertAt, 0, id);
      return { ...lanesNow, [from]: lanesNow[from].filter((x) => x !== id), [to]: target };
    });
  };

  const handleEnd = ({ active, over }: DragEndEvent) => {
    const lanesNow = dragLanes ?? fromProps;
    const id = active.id as number;
    const to = over ? laneOf(over.id, lanesNow) : undefined;
    setDragLanes(null);
    setActiveId(null);
    lastDragEnd.current = Date.now();
    if (!over || !to) return;

    let order = lanesNow[to];
    const oldIndex = order.indexOf(id);
    const overIndex = order.indexOf(over.id as number);
    if (overIndex >= 0 && oldIndex !== overIndex) order = arrayMove(order, oldIndex, overIndex);
    onMove(id, to, order);
  };

  const handleCancel = () => {
    lastDragEnd.current = Date.now();
    setDragLanes(null);
    setActiveId(null);
  };

  const active = activeId === null ? undefined : cards.get(activeId);

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCorners}
      onDragStart={handleStart}
      onDragOver={handleOver}
      onDragEnd={handleEnd}
      onDragCancel={handleCancel}
    >
      <div className="board">
        {columns.map((col) => (
          <Lane
            key={col.id}
            column={col}
            ids={current[col.id]}
            cards={cards}
            labels={labels}
            projects={projects}
            total={totals[col.id]}
            filtering={filtering}
            dragging={activeId !== null}
            onOpen={open}
            onQuickAdd={onQuickAdd}
          />
        ))}
      </div>
      <DragOverlay dropAnimation={{ duration: 150, easing: "ease-out" }}>
        {active ? <CardTile card={active} labels={labels} projects={projects} overlay /> : null}
      </DragOverlay>
    </DndContext>
  );
}

interface LaneProps {
  column: Column;
  ids: number[];
  cards: Map<number, Card>;
  labels: Map<number, Label>;
  projects: Map<number, Project>;
  total: number;
  filtering: boolean;
  dragging: boolean;
  onOpen: (id: number) => void;
  onQuickAdd: (status: Status, title: string) => Promise<void>;
}

function Lane({ column, ids, cards, labels, projects, total, filtering, dragging, onOpen, onQuickAdd }: LaneProps) {
  const { setNodeRef, isOver } = useDroppable({ id: COLUMN_PREFIX + column.id });
  const count = filtering && ids.length !== total ? `${ids.length} of ${total}` : `${total}`;

  return (
    <section className={`lane lane-${column.id}${isOver ? " over" : ""}`} aria-labelledby={`lane-${column.id}`}>
      <header className="lane-head">
        <h2 id={`lane-${column.id}`}>{column.name}</h2>
        <span className="lane-count">{count}</span>
      </header>
      <div className="lane-body" ref={setNodeRef}>
        <SortableContext items={ids} strategy={verticalListSortingStrategy}>
          {ids.map((id) => {
            const card = cards.get(id);
            return card ? <SortableCard key={id} card={card} labels={labels} projects={projects} onOpen={onOpen} /> : null;
          })}
        </SortableContext>
        {ids.length === 0 && (
          <p className="lane-empty">
            {dragging ? "Drop here" : filtering && total > 0 ? "No matching cards" : column.id === "done" ? "Finished cards land here" : "Nothing here"}
          </p>
        )}
      </div>
      {column.id !== "done" && <QuickAdd onAdd={(title) => onQuickAdd(column.id, title)} />}
    </section>
  );
}

interface SortableCardProps {
  card: Card;
  labels: Map<number, Label>;
  projects: Map<number, Project>;
  onOpen: (id: number) => void;
}

function SortableCard({ card, labels, projects, onOpen }: SortableCardProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: card.id });

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      onOpen(card.id);
      return;
    }
    listeners?.onKeyDown?.(e);
  };

  return (
    <div
      ref={setNodeRef}
      className={`sortable${isDragging ? " dragging" : ""}`}
      style={{ transform: CSS.Translate.toString(transform), transition }}
      {...attributes}
      {...listeners}
      onKeyDown={onKeyDown}
      onClick={() => onOpen(card.id)}
      aria-label={`#${card.id} ${card.title}${card.project ? ` (${card.project})` : ""}. Enter to open, Space to move.`}
      aria-roledescription="card"
    >
      <CardTile card={card} labels={labels} projects={projects} />
    </div>
  );
}
