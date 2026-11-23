# Agent notes: tasktracker

Tom's personal task board: a .NET 10 API with SQLite and a React web app, in one container on `cargoship`. Start with [README.md](README.md); deploys are in [docs/deploy.md](docs/deploy.md).

- **Docs describe what exists now**, like homelab-config's: no progress notes. Open work and ideas go in homelab-config's [backlog](https://github.com/autumnleaves/homelab-config/blob/main/docs/backlog.md).
- **No secrets** in the repo. The deploy secrets live in GitHub.
- **Schema changes** are a new numbered script in `src/TaskTracker.Api/Data/Migrations/` (never edit an applied one). Prefer additive ones, but one the previous release can't fully read (a table rebuild, a new status) is fine: Tom fixes forward rather than rolling back. The previous release must still start on the newer schema, though, since a failed deploy rolls back to it automatically; so keep the columns it reads, and say in the pull request what a rollback would break.
- **The web app only calls same-origin `/api`** (`web/src/api.ts`). The container serves both, at home on port 5280 and from anywhere through the Cloudflare Tunnel, where it asks for a basic-auth login (`BasicAuth.cs`).
- **The API is also for agents:** Claude manages the board through it (filters, `PATCH`, labels and projects by name, comments signed `Claude`). Keep `/api/openapi.json` accurate, give new endpoints a `WithSummary`/`WithDescription`, and test them in `AgentApiTests`.
- **Checks:** `dotnet test TaskTracker.slnx`, `cd web && npm run build`. CI runs them plus an image smoke test on every pull request.
- **Merging:** this repo is low-risk. Merge your own pull requests once CI is green, without asking (a merge commit, like the others); merging to `main` deploys. If something breaks, fix it forward.
- **Deploys** use pie-delivery's Compose action and `pie.json`; nothing on `cargoship` is specific to this app, so there's no setup script here.
- **Where things run:** Tom works from a Windows laptop; commands for `cargoship` are for him to run over SSH (`ssh cargoship`). `tasktracker.autumnleaves.net` is the Cloudflare Tunnel (set up in the Cloudflare dashboard, [docs/deploy.md](docs/deploy.md#cloudflare-tunnel-and-login)).
- **Git:** link to other repos' files by full GitHub URL (`https://github.com/autumnleaves/<repo>/blob/main/<path>`).
