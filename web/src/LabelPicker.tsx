import { useEffect, useRef, useState } from "react";
import type { Label } from "./api";
import { LabelChip } from "./LabelChip";

interface Props {
  labels: Label[];
  selected: number[];
  onChange: (ids: number[]) => void;
  /** Makes a new label; resolves to it once saved. */
  onCreate: (name: string) => Promise<Label>;
}

/** The card's labels, with a pop-up to add existing ones or make a new one by typing its name. */
export function LabelPicker({ labels, selected, onChange, onCreate }: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const box = useRef<HTMLDivElement>(null);
  const byId = new Map(labels.map((l) => [l.id, l]));

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const toggle = (id: number) =>
    onChange(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);

  const q = query.trim().toLowerCase();
  const matches = labels.filter((l) => l.name.toLowerCase().includes(q));
  const exact = labels.find((l) => l.name.toLowerCase() === q);

  const create = async () => {
    const name = query.trim();
    if (!name) return;
    if (exact) {
      if (!selected.includes(exact.id)) onChange([...selected, exact.id]);
    } else {
      const label = await onCreate(name);
      onChange([...selected, label.id]);
    }
    setQuery("");
  };

  return (
    <div className="label-picker" ref={box}>
      <div className="label-picker-chips">
        {selected.map((id) => {
          const label = byId.get(id);
          return label ? (
            <LabelChip key={id} label={label}>
              <button type="button" className="chip-remove" aria-label={`Remove ${label.name}`} onClick={() => toggle(id)}>
                ×
              </button>
            </LabelChip>
          ) : null;
        })}
        <button type="button" className="quiet small" aria-expanded={open} onClick={() => setOpen(!open)}>
          {selected.length ? "Change" : "Add labels"}
        </button>
      </div>
      {open && (
        <div className="popover" role="dialog" aria-label="Labels">
          <input
            autoFocus
            value={query}
            placeholder="Find or make a label"
            aria-label="Find or make a label"
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                void create();
              } else if (e.key === "Escape") {
                e.stopPropagation();
                setOpen(false);
              }
            }}
          />
          <ul className="popover-list">
            {matches.map((label) => (
              <li key={label.id}>
                <label>
                  <input type="checkbox" checked={selected.includes(label.id)} onChange={() => toggle(label.id)} />
                  <LabelChip label={label} />
                </label>
              </li>
            ))}
            {q && !exact && (
              <li>
                <button type="button" className="quiet small" onClick={() => void create()}>
                  Make label “{query.trim()}”
                </button>
              </li>
            )}
            {!q && labels.length === 0 && <li className="hint">Type a name to make your first label.</li>}
          </ul>
        </div>
      )}
    </div>
  );
}
