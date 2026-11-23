-- The In review column: status 'in_review'. SQLite can't change a CHECK constraint, so this
-- rebuilds the cards table the way SQLite documents: a new table, the rows copied, the old one
-- dropped and the new one renamed, then its indexes made again. Every column stays as it was.
-- Migrations run with foreign keys off (Database.cs), so dropping the old table leaves
-- card_labels and comments alone; they're checked before the migration commits.

CREATE TABLE cards_new (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT    NOT NULL,
    description  TEXT    NOT NULL DEFAULT '',
    status       TEXT    NOT NULL CHECK (status IN ('backlog', 'todo', 'in_progress', 'in_review', 'done')),
    priority     TEXT    NOT NULL DEFAULT 'medium' CHECK (priority IN ('low', 'medium', 'high', 'urgent')),
    due_date     TEXT,
    position     INTEGER NOT NULL,
    created_at   TEXT    NOT NULL,
    updated_at   TEXT    NOT NULL,
    completed_at TEXT,
    project_id   INTEGER REFERENCES projects (id) ON DELETE SET NULL
);

INSERT INTO cards_new (id, title, description, status, priority, due_date, position, created_at, updated_at, completed_at, project_id)
SELECT id, title, description, status, priority, due_date, position, created_at, updated_at, completed_at, project_id
FROM cards;

-- Card numbers are never reused: carry over the AUTOINCREMENT counter, which is past the
-- highest remaining id when the newest cards were deleted.
DELETE FROM sqlite_sequence WHERE name = 'cards_new';
INSERT INTO sqlite_sequence (name, seq) SELECT 'cards_new', seq FROM sqlite_sequence WHERE name = 'cards';

DROP TABLE cards;
ALTER TABLE cards_new RENAME TO cards;

CREATE INDEX cards_status_position ON cards (status, position);
CREATE INDEX cards_project ON cards (project_id);
