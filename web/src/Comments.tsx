import { useEffect, useState } from "react";
import type { Comment } from "./api";
import { api, errorMessage } from "./api";
import { longDateTime } from "./format";
import { MarkdownView } from "./Markdown";

interface Props {
  cardId: number;
  /** Called after a comment is added or deleted, so the board's counts catch up. */
  onChange: () => void;
}

/** A card's comments, oldest first, with a box to add one. Comments from the API (Claude) show who wrote them. */
export function Comments({ cardId, onChange }: Props) {
  const [comments, setComments] = useState<Comment[] | null>(null);
  const [draft, setDraft] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    api
      .comments(cardId)
      .then((c) => live && setComments(c))
      .catch((e) => live && setError(errorMessage(e)));
    return () => {
      live = false;
    };
  }, [cardId]);

  const add = async () => {
    const body = draft.trim();
    if (!body || busy) return;
    setBusy(true);
    setError(null);
    try {
      const comment = await api.addComment(cardId, body);
      setComments((c) => [...(c ?? []), comment]);
      setDraft("");
      onChange();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (id: number) => {
    setError(null);
    try {
      await api.deleteComment(id);
      setComments((c) => (c ?? []).filter((x) => x.id !== id));
      onChange();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <div className="comments-section">
      {comments === null && !error && <p className="hint">Loading comments…</p>}
      {comments?.length === 0 && <p className="hint">No comments yet.</p>}
      <ol className="comment-list">
        {comments?.map((c) => (
          <li key={c.id} className="comment" data-author={c.author.toLowerCase()}>
            <div className="comment-head">
              <span className="comment-author">{c.author}</span>
              <span className="comment-time">{longDateTime(c.createdAt)}</span>
              <button type="button" className="quiet small danger-text" onClick={() => void remove(c.id)}>
                Delete
              </button>
            </div>
            <MarkdownView source={c.body} />
          </li>
        ))}
      </ol>
      <textarea
        className="comment-input"
        value={draft}
        rows={2}
        placeholder="Add a comment"
        aria-label="Add a comment"
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            e.stopPropagation();
            void add();
          }
        }}
      />
      <div className="comment-actions">
        <button type="button" className="primary small" disabled={!draft.trim() || busy} onClick={() => void add()}>
          Add comment
        </button>
        {error && <span className="field-error">{error}</span>}
      </div>
    </div>
  );
}
