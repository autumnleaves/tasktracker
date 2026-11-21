# tasktracker

Tom's own task board, like a one-person Jira: cards in four columns (Backlog, To do, In progress, Done), dragged between them. Cards have a title, a Markdown description (with `- [ ]` checklists you can tick on the card), a priority, a due date and labels. One small container on `cargoship`, deployed by GitHub Actions on every push to `main`, like [homelab-media](https://github.com/autumnleaves/homelab-media). The rest of the homelab is described in [homelab-config](https://github.com/autumnleaves/homelab-config/blob/main/README.md).

At [`https://tasktracker.autumnleaves.net`](https://tasktracker.autumnleaves.net) through homelab-media's Caddy proxy, or `http://cargoship:5280` (`http://192.168.1.11:5280` at home). Both work at home and over Tailscale; nothing is exposed to the internet, and there's no login.

```
 browser ── https ──▶ Caddy (homelab-media) ──▶ cargoship:5280 ──▶ tasktracker container
                                                                    ├─ /api/*  the API (.NET 10)
                                                                    └─ /*      the web app (React)
                                                                         │
                                                        /opt/tasktracker/data/tasks.db (SQLite)
```

## Using it

- **Add a card:** "+ Add a card" under a column (Enter adds it and leaves the box open for the next), or **New card** (shortcut **C**) for the full editor.
- **Move a card:** drag it, or change its column in the editor. With the keyboard: Tab to a card, Space to pick it up, arrows to move, Space to drop, Esc to cancel.
- **Edit a card:** click it (or Enter). Changes save as you type. The address gets `?card=<number>`, so a card can be bookmarked or linked.
- **Find cards:** search (shortcut **/**) matches titles, descriptions and `#<number>`; the priority and label chips filter the board. Dragging while filtered places the card next to the visible card it lands by.
- **Labels:** made on the fly from a card's label picker, or managed (renamed, recoloured, deleted) under **Labels**.

## Layout

| Path | What |
| --- | --- |
| [`src/TaskTracker.Api/`](src/TaskTracker.Api) | The API: minimal APIs, Dapper and SQLite. [`Board/BoardApi.cs`](src/TaskTracker.Api/Board/BoardApi.cs) has the endpoints and validation, [`Board/BoardStore.cs`](src/TaskTracker.Api/Board/BoardStore.cs) the queries |
| [`src/TaskTracker.Api/Data/Migrations/`](src/TaskTracker.Api/Data/Migrations) | The schema, as numbered SQL scripts applied at startup ([`Database.cs`](src/TaskTracker.Api/Data/Database.cs)) |
| [`tests/`](tests/TaskTracker.Api.Tests) | API tests (xUnit, `WebApplicationFactory`, a real SQLite file per test) |
| [`web/`](web) | The web app: React, TypeScript, Vite, dnd-kit for drag and drop |
| [`Dockerfile`](Dockerfile) | One image: the API serving the built web app |
| [`stack/`](stack/compose.yaml) | The Compose project on `cargoship` and its settings |
| [`deploy/`](deploy/activate.sh), [`docs/deploy.md`](docs/deploy.md) | CI deploys to `cargoship`: one-time setup, per-release activation, rollback |

## API

JSON under `/api`. Statuses are `backlog`, `todo`, `in_progress`, `done`; priorities `low`, `medium`, `high`, `urgent`; due dates `yyyy-MM-dd`. Validation errors are `400` problem details with an `errors` object per field.

| Method and path | What |
| --- | --- |
| `GET /api/health` | `200` when the database answers |
| `GET /api/board` | Columns, every card (by column, then position) and labels |
| `POST /api/cards` | New card at the bottom of its column. `title` required; `description`, `status` (default `backlog`), `priority` (default `medium`), `dueDate`, `labelIds` optional |
| `GET`, `PUT`, `DELETE /api/cards/{id}` | One card. `PUT` replaces the editable fields (`title`, `description`, `priority`, `dueDate`, `labelIds`) |
| `POST /api/cards/{id}/move` | `{ "status": "...", "index": n }`: to position `n` (0 is the top) in that column, the bottom if `index` is left out. Into Done sets `completedAt`; out of Done clears it |
| `GET`, `POST /api/labels`, `PUT`, `DELETE /api/labels/{id}` | Labels: `name` (unique, ignoring case) and `color` (`#rrggbb`). Deleting one takes it off its cards |

Card numbers are never reused, even after a delete.

## Data

One SQLite file, `/opt/tasktracker/data/tasks.db` on `cargoship`, mounted into the container at `/data`. It's on `cargoship`'s own disk, so the weekly VM backup covers it. Before the app applies a schema migration to an existing database, it copies it to `data/backups/tasks-v<old version>-<time>.db`.

Migrations should only add things (tables, nullable columns): a failed deploy rolls back to the previous release, which then runs against the newer schema.

## Developing

Needs the .NET 10 SDK and Node 24.

```bash
dotnet run --project src/TaskTracker.Api     # API on http://localhost:5280, tasks.db in that folder
cd web && npm install && npm run dev         # web app on http://localhost:5173, /api proxied to 5280
dotnet test TaskTracker.slnx                 # API tests
```

CI ([`deploy.yml`](.github/workflows/deploy.yml)) runs the tests, type-checks and builds the web app, builds the image and smoke-tests it on every push and pull request.

## Status

Built, not deployed yet: the one-time setup in [docs/deploy.md](docs/deploy.md) is still to do (deploy key, secrets, `setup.sh` on `cargoship`). The proxy name comes from homelab-media's Caddyfile and its wildcard DNS record.

**Later:** the web app on Cloudflare Pages at `tasktracker.autumnleaves.net`, behind Cloudflare Access, with the API reached through a Cloudflare Tunnel behind the same Access application. The web app already calls only same-origin `/api`, so that's routing plus the API checking Access's token (`Cf-Access-Jwt-Assertion`), not a rewrite.
