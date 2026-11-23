# Agent notes: tasktracker

Tom's personal task board: a .NET 10 API with SQLite and a React web app, in one container on `cargoship`. Start with [README.md](README.md); deploys are in [docs/deploy.md](docs/deploy.md).

- **Docs describe what exists now**, like homelab-config's: no progress notes. Open work and ideas go in homelab-config's [backlog](https://github.com/autumnleaves/homelab-config/blob/main/docs/backlog.md).
- **No secrets** in the repo. The deploy secrets live in GitHub.
- **Schema changes** are a new numbered script in `src/TaskTracker.Api/Data/Migrations/` (never edit an applied one), additive only, since a failed deploy rolls back to the previous release on the newer schema.
- **The web app only calls same-origin `/api`** (`web/src/api.ts`). The container serves both, at home on port 5280 and from anywhere through the Cloudflare Tunnel behind Access.
- **The API is also for agents:** Claude manages the board through it (filters, `PATCH`, labels by name, comments signed `Claude`). Keep `/api/openapi.json` accurate, give new endpoints a `WithSummary`/`WithDescription`, and test them in `AgentApiTests`.
- **Checks:** `dotnet test TaskTracker.slnx`, `cd web && npm run build`. CI runs them plus an image smoke test on every pull request.
- **Deploys** use pie-delivery's Compose action and `pie.json`; nothing on `cargoship` is specific to this app, so there's no setup script here.
- **Where things run:** Tom works from a Windows laptop; commands for `cargoship` are for him to run over SSH (`ssh cargoship`). `tasktracker.autumnleaves.net` is the Cloudflare Tunnel (set up in the Cloudflare dashboard, [docs/deploy.md](docs/deploy.md#cloudflare-tunnel-and-access)).
- **Git:** link to other repos' files by full GitHub URL (`https://github.com/autumnleaves/<repo>/blob/main/<path>`).
