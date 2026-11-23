import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import type { Project } from "./api";
import { errorMessage } from "./api";

/** A project's name with its colour, as on a card. */
export function ProjectTag({ project }: { project: Project }) {
  return (
    <span className="project-tag" style={{ "--project": project.color } as CSSProperties}>
      {project.name}
    </span>
  );
}

/** Which projects the board shows; null stands for cards without one. Empty shows them all. */
export type ProjectSelection = (number | null)[];

interface FilterProps {
  projects: Project[];
  selected: ProjectSelection;
  /** How many cards each project has (null: cards without one). */
  counts: Map<number | null, number>;
  onChange: (selected: ProjectSelection) => void;
}

/** The top bar's project filter: a button that opens a list of projects to tick. */
export function ProjectFilter({ projects, selected, counts, onChange }: FilterProps) {
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    window.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      window.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const toggle = (id: number | null) =>
    onChange(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);

  const only = selected.length === 1 ? selected[0] : undefined;
  const onlyProject = only == null ? undefined : projects.find((p) => p.id === only);
  const text =
    selected.length === 0
      ? "All projects"
      : only === null
        ? "No project"
        : onlyProject
          ? onlyProject.name
          : `${selected.length} projects`;

  const row = (id: number | null, name: ReactNode) => (
    <li key={id ?? "none"}>
      <label>
        <input type="checkbox" checked={selected.includes(id)} onChange={() => toggle(id)} />
        {name}
        <span className="popover-count">{counts.get(id) ?? 0}</span>
      </label>
    </li>
  );

  return (
    <div className="project-filter" ref={box}>
      <button
        type="button"
        className="filter-chip project-button"
        aria-pressed={selected.length > 0}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={`Project: ${text}`}
        style={onlyProject ? ({ "--project": onlyProject.color } as CSSProperties) : undefined}
        onClick={() => setOpen(!open)}
      >
        {onlyProject && <span className="project-dot" aria-hidden="true" />}
        {text}
        <span className="caret" aria-hidden="true">
          ▾
        </span>
      </button>
      {open && (
        <div className="popover" role="dialog" aria-label="Filter by project">
          <ul className="popover-list">
            {projects.map((p) => row(p.id, <ProjectTag project={p} />))}
            {row(null, <span className="no-project">No project</span>)}
          </ul>
          {selected.length > 0 && (
            <button type="button" className="quiet small" onClick={() => onChange([])}>
              Show all projects
            </button>
          )}
        </div>
      )}
    </div>
  );
}

interface SelectProps {
  projects: Project[];
  value: number | null;
  onChange: (id: number | null) => void;
  /** Makes a new project; resolves to it once saved. */
  onCreate: (name: string) => Promise<Project>;
}

const NEW = "new";

/** The card editor's project: pick one, none, or "New project…" to name a new one. */
export function ProjectSelect({ projects, value, onChange, onCreate }: SelectProps) {
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const cancel = () => {
    setNaming(false);
    setName("");
    setError(null);
  };

  const create = async () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const existing = projects.find((p) => p.name.toLowerCase() === trimmed.toLowerCase());
    try {
      onChange((existing ?? (await onCreate(trimmed))).id);
      cancel();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  if (naming) {
    return (
      <span className="project-new">
        <span className="project-new-row">
          <input
            autoFocus
            value={name}
            placeholder="Project name"
            aria-label="New project name"
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                void create();
              } else if (e.key === "Escape") {
                e.stopPropagation();
                cancel();
              }
            }}
            onBlur={() => !name.trim() && cancel()}
          />
          <button
            type="button"
            className="primary small"
            disabled={!name.trim()}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => void create()}
          >
            Add
          </button>
        </span>
        {error && <span className="field-error">{error}</span>}
      </span>
    );
  }

  return (
    <select
      value={value ?? ""}
      onChange={(e) => {
        const v = e.target.value;
        if (v === NEW) setNaming(true);
        else onChange(v ? Number(v) : null);
      }}
    >
      <option value="">No project</option>
      {projects.map((p) => (
        <option key={p.id} value={p.id}>
          {p.name}
        </option>
      ))}
      <option value={NEW}>New project…</option>
    </select>
  );
}
