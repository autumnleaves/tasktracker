-- Projects: the overarching piece of work a card belongs to, at most one per card. Like labels,
-- a name (unique, ignoring case) and a #rrggbb colour. Deleting a project leaves its cards
-- without one.
CREATE TABLE projects (
    id    INTEGER PRIMARY KEY,
    name  TEXT NOT NULL UNIQUE COLLATE NOCASE,
    color TEXT NOT NULL
);

ALTER TABLE cards ADD COLUMN project_id INTEGER REFERENCES projects (id) ON DELETE SET NULL;
CREATE INDEX cards_project ON cards (project_id);
