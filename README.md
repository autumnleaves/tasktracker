# tasktracker

Tom's own task board, like a one-person Jira: cards in four columns (Backlog, To do, In progress, Done), dragged between them. Cards have a title, a Markdown description (with `- [ ]` checklists you can tick on the card), a priority, a due date, labels, and a project: the bigger piece of work they're part of. One small container on `cargoship`, deployed by GitHub Actions on every push to `main` with [pie-delivery](https://github.com/autumnleaves/pie-delivery)'s Compose action. The rest of the homelab is described in [homelab-config](https://github.com/autumnleaves/homelab-config/blob/main/README.md).

At [`https://tasktracker.autumnleaves.net`](https://tasktracker.autumnleaves.net), through a Cloudflare Tunnel, with a basic-auth login (the browser asks about once a month; Claude sends it). At home and on the tailnet it's also at `http://cargoship:5280` (`http://192.168.1.11:5280`), with no login.

```
 browser / Claude ── https ──▶ Cloudflare ── tunnel ──▶ cloudflared ─(login)─┐
 at home ─────────── http ──▶ cargoship:5280 ────────────────────────────────┴▶ tasktracker container
                                                                    ├─ /api/*  the API (.NET 10)
                                                                    └─ /*      the web app (React)
                                                                         │
                                                   /opt/apps/tasktracker/data/tasks.db (SQLite)
```

## Using it

- **Add a card:** "+ Add a card" under a column (Enter adds it and leaves the box open for the next), or **New card** (shortcut **C**) for the full editor.
- **Move a card:** drag it, or change its column in the editor. With the keyboard: Tab to a card, Space to pick it up, arrows to move, Space to drop, Esc to cancel.
- **Edit a card:** click it (or Enter). Changes save as you type. The address gets `?card=<number>`, so a card can be bookmarked or linked.
- **Find cards:** search (shortcut **/**) matches titles, descriptions and `#<number>`; the project filter (tick one or more projects, or "No project") and the priority and label chips filter the board. Dragging while filtered places the card next to the visible card it lands by.
- **Projects:** each card belongs to at most one, shown by its number. Pick or make one in the card's editor, or manage them (rename, recolour, delete) under **Projects**. While the board is filtered to a single project, new cards start in it. Deleting a project keeps its cards, without one.
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
| [`pie.json`](pie.json), [`docs/deploy.md`](docs/deploy.md) | How pie-delivery deploys it: the health check, the data folder, rollback |

## API

JSON under `/api`. Statuses are `backlog`, `todo`, `in_progress`, `done`; priorities `low`, `medium`, `high`, `urgent`; due dates `yyyy-MM-dd`. Validation errors are `400` problem details with an `errors` object per field.

| Method and path | What |
| --- | --- |
| `GET /api/health` | `200` when the database answers |
| `GET /api/board` | Columns, every card (by column, then position) and labels |
| `GET /api/cards` | Cards in board order, filtered: `status`, `label` (a name; all must match), `project` (a name; any can match), `priority` (each repeatable), `q` (text, or `#<number>`), `includeDone=false` |
| `POST /api/cards` | New card at the bottom of its column. `title` required; `description`, `status` (default `backlog`), `priority` (default `medium`), `dueDate`, `labels` (names, made if new) or `labelIds`, `project` (a name, made if new) or `projectId` optional |
| `GET`, `PUT`, `DELETE /api/cards/{id}` | One card. `PUT` replaces the editable fields (`title`, `description`, `priority`, `dueDate`, `labelIds`, `projectId`) |
| `PATCH /api/cards/{id}` | Changes only the fields sent: those, plus `labels` (names), `project` (a name), `status` (to the bottom of that column) and `index`. `"dueDate": null` and `"project": null` clear them; unknown fields are a `400` |
| `POST /api/cards/{id}/move` | `{ "status": "...", "index": n }`: to position `n` (0 is the top) in that column, the bottom if `index` is left out. Into Done sets `completedAt`; out of Done clears it |
| `GET`, `POST /api/labels`, `PUT`, `DELETE /api/labels/{id}` | Labels: `name` (unique, ignoring case) and `color` (`#rrggbb`). Deleting one takes it off its cards |
| `GET`, `POST /api/projects`, `PUT`, `DELETE /api/projects/{id}` | Projects, the same shape as labels, but a card has at most one. Deleting one leaves its cards without a project |
| `GET`, `POST /api/cards/{id}/comments`, `DELETE /api/comments/{id}` | Comments, oldest first: `body`, and `author` (default `Tom`; Claude signs as `Claude`) |
| `GET /api/openapi.json` | This API as an OpenAPI document |

Cards also carry their labels' names (`labels`), their project's name (`project`, or `null`) and `commentCount`; `GET /api/board` also lists the `projects`. Card numbers are never reused, even after a delete.

From outside, every request but `/api/health` needs the basic-auth login: a browser asks for it and then keeps a 30-day login cookie, and scripts send it (`curl -u tom:<password>`). The app tells outside requests by Cloudflare's `CF-Connecting-IP` header, so `http://cargoship:5280` needs none ([docs/deploy.md](docs/deploy.md#cloudflare-tunnel-and-login)).

## Data

One SQLite file, `/opt/apps/tasktracker/data/tasks.db` on `cargoship`, mounted into the container at `/data`. It's on `cargoship`'s own disk, so the weekly VM backup covers it. Before the app applies a schema migration to an existing database, it copies it to `data/backups/tasks-v<old version>-<time>.db`.

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

Running on `cargoship`, deployed with pie-delivery v2. The Cloudflare Tunnel starts once its token is set ([docs/deploy.md](docs/deploy.md#cloudflare-tunnel-and-login)); until then it's reachable at home and on the tailnet only.

