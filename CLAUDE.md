# Agent notes: tasktracker

Tom's personal task board: a .NET 10 API with SQLite and a React web app, in one container on `cargoship`. Start with [README.md](README.md); deploys are in [docs/deploy.md](docs/deploy.md).

- **Docs describe what exists now**, like homelab-config's: no progress notes. Open work and ideas go in homelab-config's [backlog](https://github.com/autumnleaves/homelab-config/blob/main/docs/backlog.md).
- **No secrets** in the repo. The deploy secrets live in GitHub.
- **Schema changes** are a new numbered script in `src/TaskTracker.Api/Data/Migrations/` (never edit an applied one), additive only, since a failed deploy rolls back to the previous release on the newer schema.
- **The web app only calls same-origin `/api`** (`web/src/api.ts`), so it can later move to Cloudflare Pages behind Cloudflare Access without code changes.
- **Checks:** `dotnet test TaskTracker.slnx`, `cd web && npm run build`, `shellcheck deploy/*.sh`. CI runs them plus an image smoke test on every pull request.
- **Where things run:** Tom works from a Windows laptop; commands for `cargoship` are for him to run over SSH (`ssh cargoship`). The proxy name is a block in homelab-media's Caddyfile.
- **Git:** link to other repos' files by full GitHub URL (`https://github.com/autumnleaves/<repo>/blob/main/<path>`).
