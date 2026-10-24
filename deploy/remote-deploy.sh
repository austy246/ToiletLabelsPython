#!/usr/bin/env bash
# Runs ON THE VPS, fed over SSH from the local machine by deploy/deploy.ps1:
#   ssh ... deploy@host bash -s -- IMAGE_REF [--selftest] [--prepare-only] < remote-deploy.sh
#
#   IMAGE_REF        ghcr.io/austy246/toiletlabels-app:<tag>
#   --selftest       first run `IMAGE_REF --selftest` (native dependencies of the image); failure aborts
#   --prepare-only   do not deploy: migrate deploy/.env (migrate-env.sh) and run the read-only
#                    `IMAGE_REF --check-data` against production storage, then stop
#
# The VPS is shared with other projects: this script only ever touches toiletlabels. It must NEVER run
# `docker image prune -af` / `docker system prune`; it removes only old tags of ghcr.io/austy246/toiletlabels-app.
# Secrets are never printed and never passed in argv (the env file is only handed to docker via --env-file).
#
# The whole body is a function so bash has parsed it completely before running anything (the script
# arrives on stdin, and nothing below may read stdin: docker commands get </dev/null).
set -euo pipefail

main() {
  local APP_DIR="${APP_DIR:-/srv/toiletlabels}"
  local IMAGE_REPO="ghcr.io/austy246/toiletlabels-app"
  local SITES="${SITES:-/srv/infra/sites}"

  local image_ref="" selftest=0 prepare_only=0 arg
  for arg in "$@"; do
    case "$arg" in
      --selftest) selftest=1 ;;
      --prepare-only) prepare_only=1 ;;
      -*) echo "ERROR: unknown option $arg" >&2; exit 2 ;;
      *) [ -z "$image_ref" ] || { echo "ERROR: more than one IMAGE_REF" >&2; exit 2; }; image_ref="$arg" ;;
    esac
  done
  [ -n "$image_ref" ] || { echo "usage: remote-deploy.sh IMAGE_REF [--selftest] [--prepare-only]" >&2; exit 2; }
  # The ref ends up in .env and in docker commands, so accept nothing but our own repository and a plain tag.
  case "$image_ref" in
    "$IMAGE_REPO":*) ;;
    *) echo "ERROR: IMAGE_REF must be $IMAGE_REPO:<tag>" >&2; exit 2 ;;
  esac
  [[ "${image_ref#"$IMAGE_REPO":}" =~ ^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$ ]] \
    || { echo "ERROR: invalid image tag" >&2; exit 2; }

  # 1. .env must exist (clean server: APP_DOMAIN=... bash deploy/gen-env.sh, then deploy.ps1 -UpdateSecrets).
  cd "$APP_DIR/deploy"
  [ -f .env ] || {
    echo "ERROR: $APP_DIR/deploy/.env is missing. On a clean server run 'APP_DOMAIN=<domain> bash gen-env.sh' there" >&2
    echo "       and then deploy.ps1 -UpdateSecrets (secrets.env) before deploying." >&2
    exit 1
  }

  # 2. Anonymous pull (the GHCR package is public).
  echo "==> docker pull $image_ref"
  docker pull "$image_ref" </dev/null

  # 3. Native dependencies of the image (Magick.NET, HEIC, WebP, ICU); needs neither storage nor network.
  if [ "$selftest" -eq 1 ]; then
    echo "==> selftest"
    docker run --rm --memory 512m --cpus 1 "$image_ref" --selftest </dev/null || { echo "ERROR: selftest failed" >&2; exit 1; }
  fi

  # 4. Prepare only: convert .env and prove the new image can read every production entity (read-only).
  if [ "$prepare_only" -eq 1 ]; then
    echo "==> migrate-env"
    bash ./migrate-env.sh
    echo "==> check-data (read-only)"
    docker run --rm --memory 512m --cpus 1 --env-file .env "$image_ref" --check-data </dev/null
    echo "prepare-only finished, nothing was deployed"
    return 0
  fi

  # 5. Deploy. APP_IMAGE is rewritten without sed so the value can never be interpreted as a pattern.
  local previous current_ref
  current_ref="$(grep -m1 '^APP_IMAGE=' .env | cut -d= -f2- || true)"
  if [ -n "$current_ref" ] && [ "$current_ref" != "$image_ref" ]; then
    printf '%s\n' "$current_ref" > .previous-image
  fi
  previous="$(cat .previous-image 2>/dev/null || true)"
  umask 077
  local tmp
  tmp="$(mktemp .env.XXXXXX)"
  { grep -v '^APP_IMAGE=' .env || true; } > "$tmp"
  printf 'APP_IMAGE=%s\n' "$image_ref" >> "$tmp"
  mv "$tmp" .env

  echo "==> docker compose up"
  docker compose pull app </dev/null
  if ! docker compose up -d --wait --remove-orphans </dev/null; then
    echo "ERROR: the app did not become healthy. Previous image: ${previous:-unknown}" >&2
    docker compose ps </dev/null >&2 || true
    exit 1
  fi

  # 6. Shared Caddy (the one of manarozeniny imports $SITES/*.caddy): drop our block in, validate the whole
  # config, reload only if valid. An invalid block is rolled back, so it can never break the other
  # projects' next reload. The domain is APP_DOMAIN; DJANGO_ALLOWED_HOSTS is the fallback for a .env that
  # is still in the Python format (rollback to a Python image).
  echo "==> caddy"
  local domain
  domain="$(grep -m1 '^APP_DOMAIN=' .env | cut -d= -f2- || true)"
  [ -n "$domain" ] || domain="$(grep -m1 '^DJANGO_ALLOWED_HOSTS=' .env | cut -d= -f2- | cut -d, -f1 || true)"
  [[ "$domain" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] \
    || { echo "ERROR: APP_DOMAIN (or DJANGO_ALLOWED_HOSTS) in .env is missing or invalid" >&2; exit 1; }
  [ -d "$SITES" ] || { echo "ERROR: missing $SITES" >&2; exit 1; }
  local caddy
  caddy="$(docker ps -q --filter label=com.docker.compose.project=deploy --filter label=com.docker.compose.service=caddy | head -1)"
  [ -n "$caddy" ] || { echo "ERROR: Caddy container (project deploy) not found" >&2; exit 1; }
  if [ -f "$SITES/toiletlabels.caddy" ]; then cp -p "$SITES/toiletlabels.caddy" "$SITES/toiletlabels.caddy.bak"; fi
  sed "s#__APP_DOMAIN__#${domain}#g" "$APP_DIR/deploy/toiletlabels.caddy" > "$SITES/toiletlabels.caddy"
  chmod 644 "$SITES/toiletlabels.caddy"
  if docker exec "$caddy" caddy validate --config /etc/caddy/Caddyfile >/dev/null 2>&1 </dev/null; then
    docker exec "$caddy" caddy reload --config /etc/caddy/Caddyfile </dev/null
    rm -f "$SITES/toiletlabels.caddy.bak"
  else
    echo "ERROR: invalid Caddy configuration, restoring the previous file" >&2
    docker exec "$caddy" caddy validate --config /etc/caddy/Caddyfile </dev/null 2>&1 | tail -5 || true
    if [ -f "$SITES/toiletlabels.caddy.bak" ]; then
      mv "$SITES/toiletlabels.caddy.bak" "$SITES/toiletlabels.caddy"
    else
      rm -f "$SITES/toiletlabels.caddy"
    fi
    exit 1
  fi

  # 7. Remove old tags of OUR image only; keep the current and the previous one for a quick rollback.
  echo "==> cleaning old $IMAGE_REPO tags"
  local ref
  while IFS= read -r ref; do
    [ -n "$ref" ] || continue
    case "$ref" in "$IMAGE_REPO":*) ;; *) continue ;; esac
    [ "$ref" = "$image_ref" ] && continue
    [ -n "$previous" ] && [ "$ref" = "$previous" ] && continue
    docker rmi "$ref" </dev/null >/dev/null 2>&1 || true
  done < <(docker images "$IMAGE_REPO" --format '{{.Repository}}:{{.Tag}}' </dev/null | grep -v ':<none>$' || true)

  echo "deployed $image_ref"
}

main "$@"
