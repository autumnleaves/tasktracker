import { useEffect, useState, type CSSProperties } from "react";
import type { Label } from "./api";
import { errorMessage } from "./api";
import { LABEL_COLORS, nextLabelColor } from "./LabelChip";

interface Props {
  /** Labels, or projects: both are a name and a colour. */
  kind: "label" | "project";
  labels: Label[];
  /** How many cards have each one, by id. */
  usage: ReadonlyMap<number | null, number>;
  onClose: () => void;
  onCreate: (name: string, color: string) => Promise<unknown>;
  onUpdate: (id: number, name: string, color: string) => Promise<unknown>;
  onDelete: (id: number) => Promise<unknown>;
}

const EMPTY = {
  label: "No labels yet. Labels group cards across columns, like “homelab” or “5Stream”.",
  project: "No projects yet. A project is the bigger piece of work a card is part of, like “Move house”. Each card has at most one.",
};

/** Rename, recolour and delete labels (or projects), or add new ones. */
export function LabelsDialog({ kind, labels, usage, onClose, onCreate, onUpdate, onDelete }: Props) {
  const title = kind === "label" ? "Labels" : "Projects";
  const Kind = kind === "label" ? "Label" : "Project";
  const [newName, setNewName] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !e.defaultPrevented) onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const attempt = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
      return true;
    } catch (e) {
      setError(errorMessage(e));
      return false;
    }
  };

  const add = async () => {
    const name = newName.trim();
    if (name && (await attempt(() => onCreate(name, nextLabelColor(labels))))) setNewName("");
  };

  return (
    <div className="panel-backdrop centered" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="labels-title">
        <header className="panel-head">
          <h2 id="labels-title">{title}</h2>
          <button type="button" className="quiet" onClick={onClose}>
            Close
          </button>
        </header>
        <ul className="label-rows">
          {labels.map((label) => (
            <LabelRow
              key={label.id}
              label={label}
              kind={Kind}
              uses={usage.get(label.id) ?? 0}
              onUpdate={(name, color) => attempt(() => onUpdate(label.id, name, color))}
              onDelete={() => attempt(() => onDelete(label.id))}
            />
          ))}
          {labels.length === 0 && <li className="hint">{EMPTY[kind]}</li>}
        </ul>
        <form
          className="label-new"
          onSubmit={(e) => {
            e.preventDefault();
            void add();
          }}
        >
          <input
            value={newName}
            placeholder={`New ${kind} name`}
            aria-label={`New ${kind} name`}
            onChange={(e) => setNewName(e.target.value)}
          />
          <button type="submit" className="primary" disabled={!newName.trim()}>
            Add {kind}
          </button>
        </form>
        {error && <p className="field-error">{error}</p>}
      </div>
    </div>
  );
}

function LabelRow({
  label,
  kind,
  uses,
  onUpdate,
  onDelete,
}: {
  label: Label;
  kind: string;
  uses: number;
  onUpdate: (name: string, color: string) => Promise<boolean>;
  onDelete: () => Promise<boolean>;
}) {
  const [name, setName] = useState(label.name);
  const [confirming, setConfirming] = useState(false);

  const commitName = async () => {
    const trimmed = name.trim();
    if (!trimmed) setName(label.name);
    else if (trimmed !== label.name && !(await onUpdate(trimmed, label.color))) setName(label.name);
  };

  return (
    <li className="label-row" style={{ "--label": label.color } as CSSProperties}>
      <div className="swatches" role="radiogroup" aria-label={`Colour for ${label.name}`}>
        {LABEL_COLORS.map((color) => (
          <button
            key={color}
            type="button"
            role="radio"
            aria-checked={color === label.color}
            aria-label={color}
            className="swatch"
            style={{ background: color }}
            onClick={() => void onUpdate(label.name, color)}
          />
        ))}
      </div>
      <input
        value={name}
        aria-label={`${kind} name`}
        onChange={(e) => setName(e.target.value)}
        onBlur={() => void commitName()}
        onKeyDown={(e) => e.key === "Enter" && (e.currentTarget as HTMLInputElement).blur()}
      />
      <span className="label-uses">{uses === 1 ? "1 card" : `${uses} cards`}</span>
      {confirming ? (
        <span className="confirm">
          <button type="button" className="danger small" onClick={() => void onDelete()}>
            Delete{uses > 0 ? `, and take it off ${uses === 1 ? "1 card" : `${uses} cards`}` : ""}
          </button>
          <button type="button" className="quiet small" onClick={() => setConfirming(false)}>
            Keep
          </button>
        </span>
      ) : (
        <button type="button" className="quiet small danger-text" onClick={() => setConfirming(true)}>
          Delete
        </button>
      )}
    </li>
  );
}
