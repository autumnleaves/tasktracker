# Deploys

Pushing to `main` deploys to `cargoship` with [pie-delivery](https://github.com/autumnleaves/pie-delivery)'s Compose action (`pie-delivery/compose`, [how it works](https://github.com/autumnleaves/pie-delivery/blob/main/README.md#compose-apps)). [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) checks every push and pull request: the API tests, the web app's type check and build, the Compose file, and the Docker image, which it starts and smoke-tests. On `main` it then saves the image and bundles it with [`stack/`](../stack/compose.yaml) and [`pie.json`](../pie.json).

The deploy job joins the tailnet through Tailscale's workload identity federation and connects to `cargoship` as the shared `deploy` user over Tailscale SSH. It rsyncs the bundle and runs `pie-deploy` there, which:
- loads the image (`tasktracker:<sha>`);
- puts the release in `/opt/apps/tasktracker/releases/<sha>` with its `.env`;
- brings the `tasktracker` Compose project up and waits for `/api/health`.

If the health check answers, `current` points at the new release. If not, the previous release comes back and the job fails. The newest three releases and their images are kept. `cargoship` never builds anything or pulls the app from a registry: what runs is the image CI tested.

The only secret is `CLOUDFLARE_TUNNEL_TOKEN` (below). The Tailscale client ID and audience in the workflow aren't secret: the federated identity only accepts GitHub's tokens for `autumnleaves` repos on `main`. The database is `/opt/apps/tasktracker/data/tasks.db`, owned by UID 1654 (the chiseled .NET image's `app` user); `pie-deploy` creates the folder from `pie.json`.

Nothing on `cargoship` is specific to this app. The host's one-time setup for pie-delivery is in homelab-config.

## Cloudflare Tunnel and Access

`https://tasktracker.autumnleaves.net` reaches the app from anywhere through a Cloudflare Tunnel: the `cloudflared` service in [`stack/compose.yaml`](../stack/compose.yaml) connects out to Cloudflare, so nothing on the home network is opened. Cloudflare Access in front of it lets in Tom's login (in a browser) and a service token (for Claude, as `CF-Access-Client-Id` and `CF-Access-Client-Secret` headers). The app itself has no login: at home, `http://cargoship:5280` still works without one.

`cloudflared` runs only when the repo has the `CLOUDFLARE_TUNNEL_TOKEN` secret: the workflow then passes the token and `COMPOSE_PROFILES=tunnel` to the deploy. Removing the secret later stops new deploys from starting it, but leaves a running one in place (`sudo docker compose -p tasktracker rm -sf cloudflared` stops it).

Set up once, in the Cloudflare dashboard (Zero Trust; menu names may have moved):

1. **Tunnel:** Networks → Tunnels → create a `cloudflared` tunnel (e.g. `cargoship-tasktracker`). Copy its token (the long value after `--token` in the install command it shows) into the repo's `CLOUDFLARE_TUNNEL_TOKEN` secret.
2. **Public hostname** on the tunnel: `tasktracker` . `autumnleaves.net`, service **HTTP** `tasktracker:8080`. This adds a proxied DNS record for the name, which takes over from the wildcard record that pointed it at Caddy.
3. **Service token:** Access → Service credentials → Service tokens → create one called `claude`. Its client ID and secret are shown once: keep them in the password manager. For Claude's cloud sessions, add them as a **network secret** on the cloud environment (claude.ai/code → edit the environment → Network secrets → Add secret, type Bearer): allowed website `tasktracker.autumnleaves.net`, and two custom headers with no prefix, `CF-Access-Client-Id` (the client ID) and `CF-Access-Client-Secret` (the secret). Claude's network proxy then adds them to its requests without the session ever seeing them; sessions started before the secret was added don't get it. Elsewhere (a script on the laptop away from home), send the same two headers; at home or on the tailnet, `http://cargoship:5280` needs none.
4. **Access application:** Access → Applications → self-hosted, domain `tasktracker.autumnleaves.net`, with two policies: **Allow**, including Tom's email; and **Service Auth**, including the `claude` service token.
5. **Deploy:** Actions → deploy → Run workflow (or any push to `main`), so the deploy picks up the secret.

## Checking on it

On `cargoship`:

```bash
sudo pie-deploy status tasktracker          # releases; * marks the live one
sudo docker compose -p tasktracker logs --tail 50      # add `cloudflared` for the tunnel's
curl -s localhost:5280/api/health
```

**Rolling back:** `sudo pie-deploy rollback tasktracker` goes back to the newest release that isn't live; `sudo pie-deploy rollback tasktracker <sha>` to a given one.
