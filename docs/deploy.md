# Deploys

Pushing to `main` deploys to `cargoship` with [pie-delivery](https://github.com/autumnleaves/pie-delivery)'s Compose action (`pie-delivery/compose`, [how it works](https://github.com/autumnleaves/pie-delivery/blob/main/README.md#compose-apps)). [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) checks every push and pull request: the API tests, the web app's type check and build, the Compose file, and the Docker image, which it starts and smoke-tests. On `main` it then saves the image and bundles it with [`stack/`](../stack/compose.yaml) and [`pie.json`](../pie.json).

The deploy job joins the tailnet through Tailscale's workload identity federation and connects to `cargoship` as the shared `deploy` user over Tailscale SSH. It rsyncs the bundle and runs `pie-deploy` there, which:
- loads the image (`tasktracker:<sha>`);
- puts the release in `/opt/apps/tasktracker/releases/<sha>` with its `.env`;
- brings the `tasktracker` Compose project up and waits for `/api/health`.

If the health check answers, `current` points at the new release. If not, the previous release comes back and the job fails. The newest three releases and their images are kept. `cargoship` never builds anything or pulls the app from a registry: what runs is the image CI tested.

There are no secrets, in GitHub or anywhere else. The Tailscale client ID and audience in the workflow aren't secret: the federated identity only accepts GitHub's tokens for `autumnleaves` repos on `main`. The database is `/opt/apps/tasktracker/data/tasks.db`, owned by UID 1654 (the chiseled .NET image's `app` user); `pie-deploy` creates the folder from `pie.json`.

Nothing on `cargoship` is specific to this app. The host's one-time setup for pie-delivery is in homelab-config.

## Checking on it

On `cargoship`:

```bash
sudo pie-deploy status tasktracker          # releases; * marks the live one
sudo docker compose -p tasktracker logs --tail 50
curl -s localhost:5280/api/health
```

**Rolling back:** `sudo pie-deploy rollback tasktracker` goes back to the newest release that isn't live; `sudo pie-deploy rollback tasktracker <sha>` to a given one.
