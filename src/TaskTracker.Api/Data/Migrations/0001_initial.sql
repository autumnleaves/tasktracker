-- Cards, labels, and which labels each card has. Dates are ISO 8601 text: due_date is a
-- yyyy-MM-dd day, the *_at columns are UTC timestamps.

-- AUTOINCREMENT so a deleted card's number is never reused.
CREATE TABLE cards (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT    NOT NULL,
    description  TEXT    NOT NULL DEFAULT '',
    status       TEXT    NOT NULL CHECK (status IN ('backlog', 'todo', 'in_progress', 'done')),
    priority     TEXT    NOT NULL DEFAULT 'medium' CHECK (priority IN ('low', 'medium', 'high', 'urgent')),
    due_date     TEXT,
    position     INTEGER NOT NULL,
    created_at   TEXT    NOT NULL,
    updated_at   TEXT    NOT NULL,
    completed_at TEXT
);
CREATE INDEX cards_status_position ON cards (status, position);

CREATE TABLE labels (
    id    INTEGER PRIMARY KEY,
    name  TEXT NOT NULL UNIQUE COLLATE NOCASE,
    color TEXT NOT NULL
);

CREATE TABLE card_labels (
    card_id  INTEGER NOT NULL REFERENCES cards (id) ON DELETE CASCADE,
    label_id INTEGER NOT NULL REFERENCES labels (id) ON DELETE CASCADE,
    PRIMARY KEY (card_id, label_id)
) WITHOUT ROWID;
CREATE INDEX card_labels_label ON card_labels (label_id);
