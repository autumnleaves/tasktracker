#!/bin/bash
# Deploys one release of the task tracker on cargoship. Run as root by
# .github/workflows/deploy.yml, from the unpacked bundle:
#
#   sudo bash deploy/activate.sh <git-sha>
#
# The bundle holds the image CI built and tested (image.tar.gz, tagged tasktracker:<sha>) and
# stack/. There are no secrets.
#
# Releases live in /opt/tasktracker/releases/<sha>, with `current` pointing at the live one. The
# Compose project name is fixed (`tasktracker`) and the database lives in /opt/tasktracker/data,
# so each release takes over the same container and data. The app migrates the database at
# startup, copying it to data/backups/ first. If the new release doesn't answer its health
# check, the previous one is brought back and this exits 1.
set -euo pipefail

SHA=${1:?usage: activate.sh <git-sha>}
[[ $SHA =~ ^[0-9a-f]{7,40}$ ]] || { echo "not a git SHA: $SHA" >&2; exit 1; }
BUNDLE=$(cd "$(dirname "$0")/.." && pwd)
ROOT=/opt/tasktracker
REL=$ROOT/releases/$SHA
IMAGE=tasktracker
KEEP=3
READY_TIMEOUT_S=60
CURL=curlimages/curl:8.23.0
NET=tasktracker_default

compose() {
    local dir=$1; shift
    docker compose --project-directory "$dir" -f "$dir/compose.yaml" "$@"
}

# Polls the health endpoint over the project's network until it answers 200.
ready() {
    local deadline=$((SECONDS + READY_TIMEOUT_S))
    until docker run --rm --network "$NET" "$CURL" -fsS -o /dev/null --max-time 5 \
            http://tasktracker:8080/api/health 2>/dev/null; do
        [ $SECONDS -lt $deadline ] || { echo "not healthy after ${READY_TIMEOUT_S}s"; return 1; }
        sleep 2
    done
}

healthy() {
    compose "$1/stack" up -d --remove-orphans --wait --wait-timeout 60 && ready
}

# The image.
gunzip -c "$BUNDLE/image.tar.gz" | docker load -q
docker image inspect "$IMAGE:$SHA" >/dev/null \
    || { echo "the bundle's image isn't tagged $IMAGE:$SHA" >&2; exit 1; }

# Release directory.
install -d -m 755 "$REL"
rm -rf -- "${REL:?}/stack"
cp -r "$BUNDLE/stack" "$REL/"
{
    cat "$REL/stack/.env.example"
    echo "IMAGE_TAG=$SHA"
} >"$REL/stack/.env"
chmod -R a+rX,go-w "$REL/stack"
docker pull -q "$CURL" >/dev/null

PREV=$(readlink -e "$ROOT/current" 2>/dev/null || true)  # empty on a first deploy
echo "deploying $SHA${PREV:+, replacing $(basename "$PREV")}"

if healthy "$REL"; then
    ln -sfn "$REL" "$ROOT/current"
    compose "$REL/stack" ps
    echo "deployed $SHA"
else
    echo "deploy of $SHA failed. Container state and recent logs:"
    compose "$REL/stack" ps -a || true
    compose "$REL/stack" logs --no-color --tail 60 || true
    if [ -n "$PREV" ] && [ "$PREV" != "$REL" ] && [ -d "$PREV" ]; then
        echo "rolling back to $(basename "$PREV")"
        if healthy "$PREV"; then echo "rolled back"; else echo "ROLLBACK ALSO UNHEALTHY"; fi
    else
        echo "no previous release to roll back to"
    fi
    exit 1
fi

# Keep the newest $KEEP releases and their images, for manual rollback.
current=$(readlink -f "$ROOT/current")
# shellcheck disable=SC2012 # release names are git SHAs
ls -1t "$ROOT/releases" | tail -n +$((KEEP + 1)) | while read -r old; do
    [ "$ROOT/releases/$old" = "$current" ] && continue
    rm -rf -- "${ROOT:?}/releases/$old"
    docker image rm "$IMAGE:$old" >/dev/null 2>&1 || true
done
docker image prune -f >/dev/null
