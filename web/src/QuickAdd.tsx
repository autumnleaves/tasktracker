import { useEffect, useRef, useState } from "react";

/** "Add a card" at the bottom of a column: type a title, Enter adds it and keeps the box open. */
export function QuickAdd({ onAdd }: { onAdd: (title: string) => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState("");
  const input = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (open) input.current?.focus();
  }, [open]);

  if (!open) {
    return (
      <button type="button" className="quick-add-open" onClick={() => setOpen(true)}>
        + Add a card
      </button>
    );
  }

  // Clears the box straight away so the next title can be typed while this one saves.
  const submit = () => {
    const trimmed = title.trim();
    if (!trimmed) return;
    setTitle("");
    onAdd(trimmed).catch(() => setTitle((current) => current || trimmed));
  };

  const close = () => {
    setOpen(false);
    setTitle("");
  };

  return (
    <div className="quick-add">
      <textarea
        ref={input}
        value={title}
        rows={2}
        placeholder="What needs doing?"
        aria-label="New card title"
        onChange={(e) => setTitle(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            submit();
          } else if (e.key === "Escape") {
            close();
          }
        }}
        onBlur={() => {
          if (!title.trim()) close();
        }}
      />
      <div className="quick-add-actions">
        <button type="button" className="primary" disabled={!title.trim()} onMouseDown={(e) => e.preventDefault()} onClick={submit}>
          Add card
        </button>
        <button type="button" className="quiet" onClick={close}>
          Cancel
        </button>
      </div>
    </div>
  );
}
