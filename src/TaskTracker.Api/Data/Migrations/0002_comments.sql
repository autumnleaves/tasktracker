-- Comments on cards: notes from Tom, and progress reports from Claude working through the API.
-- author is free text ("Tom", "Claude"); created_at is a UTC ISO 8601 timestamp.
CREATE TABLE comments (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    card_id    INTEGER NOT NULL REFERENCES cards (id) ON DELETE CASCADE,
    author     TEXT    NOT NULL,
    body       TEXT    NOT NULL,
    created_at TEXT    NOT NULL
);
CREATE INDEX comments_card ON comments (card_id, id);
