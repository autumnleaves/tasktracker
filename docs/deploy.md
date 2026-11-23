# Deploys

Pushing to `main` deploys to `cargoship` with [pie-delivery](https://github.com/autumnleaves/pie-delivery)'s Compose action (`pie-delivery/compose`, [how it works](https://github.com/autumnleaves/pie-delivery/blob/main/README.md#compose-apps)). [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) checks every push and pull request: the API tests, the web app's type check and build, the Compose file, and the Docker image, which it starts and smoke-tests. On `main` it then saves the image and bundles it with [`stack/`](../stack/compose.yaml) and [`pie.json`](../pie.json).

The deploy job joins the tailnet through Tailscale's workload identity federation and connects to `cargoship` as the shared `deploy` user over Tailscale SSH. It rsyncs the bundle and runs `pie-deploy` there, which:
- loads the image (`tasktracker:<sha>`);
- puts the release in `/opt/apps/tasktracker/releases/<sha>` with its `.env`;
- brings the `tasktracker` Compose project up and waits for `/api/health`.

If the health check answers, `current` points at the new release. If not, the previous release comes back and the job fails. The newest three releases and their images are kept. `cargoship` never builds anything or pulls the app from a registry: what runs is the image CI tested.

The secrets are `CLOUDFLARE_TUNNEL_TOKEN` and `TASKTRACKER_PASSWORD` (below). The Tailscale client ID and audience in the workflow aren't secret: the federated identity only accepts GitHub's tokens for `autumnleaves` repos on `main`. The database is `/opt/apps/tasktracker/data/tasks.db`, owned by UID 1654 (the chiseled .NET image's `app` user); `pie-deploy` creates the folder from `pie.json`.

Nothing on `cargoship` is specific to this app. The host's one-time setup for pie-delivery is in homelab-config.

## Cloudflare Tunnel and login

`https://tasktracker.autumnleaves.net` reaches the app from anywhere through a Cloudflare Tunnel: the `cloudflared` service in [`stack/compose.yaml`](../stack/compose.yaml) connects out to Cloudflare, so nothing on the home network is opened. The app asks requests that come through it for a basic-auth login ([`BasicAuth.cs`](../src/TaskTracker.Api/BasicAuth.cs)): user `TASKTRACKER_USER` (`tom`, in [`stack/.env.example`](../stack/.env.example)) and the `TASKTRACKER_PASSWORD` secret. A browser asks for it, then keeps a login cookie for 30 days, so it asks again about once a month (sooner if the password changes, which signs everyone out). Claude and scripts send the login with each request (`curl -u`). It knows those requests by the `CF-Connecting-IP` header Cloudflare adds to everything it proxies, so `http://cargoship:5280` at home and on the tailnet needs no login. `/api/health` is always open. Without the password set, there's no login at all (the app logs a warning at startup).

`cloudflared` runs only when the repo has the `CLOUDFLARE_TUNNEL_TOKEN` secret: the workflow then passes the token and `COMPOSE_PROFILES=tunnel` to the deploy. Removing the secret later stops new deploys from starting it, but leaves a running one in place (`sudo docker compose -p tasktracker rm -sf cloudflared` stops it).

Set up once:

1. **Tunnel** (Cloudflare dashboard, Zero Trust → Networks → Tunnels; menu names may have moved): create a `cloudflared` tunnel (e.g. `cargoship-tasktracker`). Copy its token (the long value after `--token` in the install command it shows) into the repo's `CLOUDFLARE_TUNNEL_TOKEN` secret.
2. **Public hostname** on the tunnel: `tasktracker` . `autumnleaves.net`, service **HTTP** `tasktracker:8080`. This adds a proxied DNS record for the name, which takes over from the wildcard record that pointed it at Caddy.
3. **Password:** a long random one (letters and digits), kept in the password manager and in the repo's `TASKTRACKER_PASSWORD` secret. Changing it is the same: update the secret and deploy.
4. **Deploy:** Actions → deploy → Run workflow (or any push to `main`), so the deploy picks up the secrets.

No Cloudflare Access application is needed; if there is one for the hostname, it asks for its own login first.

## Checking on it

On `cargoship`:

```bash
sudo pie-deploy status tasktracker          # releases; * marks the live one
sudo docker compose -p tasktracker logs --tail 50      # add `cloudflared` for the tunnel's
curl -s localhost:5280/api/health
```

**Rolling back:** `sudo pie-deploy rollback tasktracker` goes back to the newest release that isn't live; `sudo pie-deploy rollback tasktracker <sha>` to a given one.
