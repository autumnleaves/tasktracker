#!/bin/bash
# Prepares cargoship for CI deploys of the task tracker. Idempotent. Run once by hand, as root,
# from a copy of the repo on cargoship (docs/deploy.md):
#
#   sudo bash deploy/setup.sh
#
# After that, every deploy (.github/workflows/deploy.yml) runs it again from the bundle, as root,
# with --deploy, before activate.sh, so changes here reach cargoship with a push. It sets up:
#   - rsync and sudo (pie-delivery needs them)
#   - user tasktracker-deploy, SSH-able only with deploy_key.pub and only from Tailscale
#     addresses. It owns $ROOT/incoming, where CI rsyncs each bundle, and may sudo activate.sh
#     and this script from there. It writes the scripts it runs as root, so CI has root on
#     cargoship (as with the other repos' deploy users).
#   - $ROOT/releases, where activate.sh keeps releases, and $ROOT/data, the SQLite database,
#     owned by the container's user. It's on cargoship's own disk, so the weekly VM backup
#     covers it.
#
# Docker must already be installed (cargoship's base setup).
set -euo pipefail

DEPLOYING=false
[ "${1:-}" = --deploy ] && DEPLOYING=true
[ "$(id -u)" -eq 0 ] || { echo "run as root: sudo bash $0" >&2; exit 1; }
HERE=$(cd "$(dirname "$0")" && pwd)
KEY=$HERE/deploy_key.pub
USER_NAME=tasktracker-deploy
ROOT=/opt/tasktracker
INCOMING=$ROOT/incoming
APP_UID=1654                # the `app` user in the chiseled .NET image (Dockerfile)
[ -s "$KEY" ] || { echo "missing $KEY: generate the deploy key first (docs/deploy.md)" >&2; exit 1; }
step() { echo "== $*"; }

step "Packages"
docker compose version >/dev/null 2>&1 \
    || { echo "Docker with the Compose plugin isn't installed" >&2; exit 1; }
missing=()
for p in rsync sudo; do dpkg -s "$p" >/dev/null 2>&1 || missing+=("$p"); done
if [ ${#missing[@]} -gt 0 ]; then
    apt-get update -q && apt-get install -yq "${missing[@]}"
fi

step "User $USER_NAME"
id "$USER_NAME" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$USER_NAME"
# The key works only from Tailscale addresses, with no tty or forwarding.
home=$(getent passwd "$USER_NAME" | cut -d: -f6)
install -d -m 700 -o "$USER_NAME" -g "$USER_NAME" "$home/.ssh"
printf 'restrict,from="100.64.0.0/10,fd7a:115c:a1e0::/48" %s\n' "$(head -n1 "$KEY")" \
    >"$home/.ssh/authorized_keys"
chown "$USER_NAME:$USER_NAME" "$home/.ssh/authorized_keys"
chmod 600 "$home/.ssh/authorized_keys"

step "Sudo: activate.sh and this script, from a bundle in $INCOMING"
rule="$USER_NAME ALL=(root) NOPASSWD: /usr/bin/bash $INCOMING/*/deploy/activate.sh *,"
rule+=" /usr/bin/bash $INCOMING/*/deploy/setup.sh --deploy"
tmp=$(mktemp)
echo "$rule" >"$tmp"
visudo -cqf "$tmp"
install -m 440 "$tmp" /etc/sudoers.d/tasktracker
rm -f "$tmp"

step "Directories"
install -d -m 755 "$ROOT" "$ROOT/releases"
install -d -m 700 -o "$USER_NAME" -g "$USER_NAME" "$INCOMING"
install -d -m 750 -o "$APP_UID" -g "$APP_UID" "$ROOT/data"

if ! $DEPLOYING; then
    echo
    echo "Done. DEPLOY_KNOWN_HOSTS must be ssh-keyscan output for the exact name in DEPLOY_HOST"
    echo "(cargoship's Tailscale name, as for the other repos)."
fi
