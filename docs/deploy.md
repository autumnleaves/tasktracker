# Deploys

Pushing to `main` deploys to `cargoship`, the same way as [homelab-media](https://github.com/autumnleaves/homelab-media/blob/main/docs/deploy.md). [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) checks every push and pull request: the API tests, the web app's type check and build, the Compose file, shellcheck, and the Docker image, which it starts and smoke-tests. On `main` it then saves the image and uses [pie-delivery](https://github.com/autumnleaves/pie-delivery) to join the tailnet as an ephemeral `tag:ci` node, rsync a bundle (the image, `stack/`, `deploy/`) to `cargoship` as `tasktracker-deploy`, and run [`deploy/setup.sh`](../deploy/setup.sh) and [`deploy/activate.sh`](../deploy/activate.sh) there.

`activate.sh` loads the image (`tasktracker:<sha>`), puts the release in `/opt/tasktracker/releases/<sha>` with its `.env`, and brings the `tasktracker` Compose project up. It waits for `/api/health`. If it answers, `current` points at the new release; if not, the previous release comes back and the job fails. The newest three releases and their images are kept. `cargoship` never builds anything or pulls the app from a registry: what runs is the image CI tested.

There are no app secrets. The database is `/opt/tasktracker/data/tasks.db`, owned by UID 1654 (the chiseled .NET image's `app` user).

## One-time setup

1. **Deploy key:** on the laptop, in the repo:

   ```bash
   ssh-keygen -t ed25519 -f tasktracker_deploy -N "" -C "github-actions tasktracker"
   ```

   Move `tasktracker_deploy.pub` to `deploy/deploy_key.pub` and commit it. The private half, `tasktracker_deploy`, goes into the `DEPLOY_SSH_KEY` secret (below); then delete the local file.
2. **Tailscale:** the tailnet policy must let `tag:ci` reach `tag:server` on `tcp:22`. The other deploys already need this.
3. **`cargoship`:** once, from the laptop's checkout (it needs `deploy/deploy_key.pub`):

   ```bash
   scp -r deploy cargoship:tasktracker-deploy
   ssh -t cargoship "sudo bash tasktracker-deploy/setup.sh && rm -rf tasktracker-deploy"
   ```

   It creates `tasktracker-deploy` (key login from Tailscale addresses only), its sudo rule for this repo's scripts, and `/opt/tasktracker` with the data folder. Every deploy re-runs it from the bundle.
4. **Secrets and variables** in the repo's Settings → Secrets and variables → Actions (below). All four are the same values as homelab-media's and homelab-observability's.
5. **Deploy:** Actions → deploy → Run workflow, or push to `main`.
6. **Proxy:** `tasktracker.autumnleaves.net` is a block in homelab-media's [Caddyfile](https://github.com/autumnleaves/homelab-media/blob/main/stack/caddy/Caddyfile), proxying to `192.168.1.11:5280`. Its wildcard DNS record already covers the name.

**Re-running:** "Re-run jobs" on an old run deploys that run's commit again; Actions → deploy → Run workflow deploys the latest `main`.

## Secrets and variables

| Name | Kind | What |
| --- | --- | --- |
| `DEPLOY_HOST` | Variable | `cargoship.tail4ee3b3.ts.net`. Deploys are skipped until it's set |
| `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET` | Secret | Tailscale OAuth client (Auth Keys: write, tag `tag:ci`), as the other repos' |
| `DEPLOY_SSH_KEY` | Secret | Private half of `deploy/deploy_key.pub` |
| `DEPLOY_KNOWN_HOSTS` | Secret | `ssh-keyscan cargoship.tail4ee3b3.ts.net` output, as homelab-observability's. Empty means deploys trust the host key on first contact |

With the GitHub CLI, from the laptop:

```cmd
gh variable set DEPLOY_HOST -R autumnleaves/tasktracker --body "cargoship.tail4ee3b3.ts.net"
gh secret set DEPLOY_SSH_KEY -R autumnleaves/tasktracker < tasktracker_deploy
gh secret set TS_OAUTH_CLIENT_ID -R autumnleaves/tasktracker
gh secret set TS_OAUTH_SECRET -R autumnleaves/tasktracker
gh secret set DEPLOY_KNOWN_HOSTS -R autumnleaves/tasktracker < known_hosts_cargoship
```

(`gh secret set` without a value prompts for it.)

## Checking on it

On `cargoship`:

```bash
sudo docker compose -p tasktracker ps
sudo docker compose -p tasktracker logs --tail 50
curl -s localhost:5280/api/health
```

**Rolling back by hand:** point `current` at an older release and bring it up: `sudo ln -sfn /opt/tasktracker/releases/<sha> /opt/tasktracker/current && sudo docker compose --project-directory /opt/tasktracker/current/stack up -d`.
