#!/usr/bin/env bash
# Migrates deploy/.env from the Django app's keys to the ASP.NET Core ones. Idempotent: it only fills
# new keys that are missing or empty, never overwrites a value, and leaves the file (and the backups) alone
# when nothing changes. Prints key names only, never values.
#
# Usage: migrate-env.sh [--drop-legacy] [ENV_FILE]      (ENV_FILE defaults to .env next to this script)
#   --drop-legacy  after a successful migration, remove AZURE_STORAGE_CONNECTION_STRING, MAPY_API_KEY,
#                  DJANGO_* and CSRF_TRUSTED_ORIGINS. A rollback to the Python image then needs the backup.
set -euo pipefail
umask 077

drop_legacy=0
ENV_FILE=""
for arg in "$@"; do
  case "$arg" in
    --drop-legacy) drop_legacy=1 ;;
    -*) echo "unknown option: $arg" >&2; exit 2 ;;
    *) ENV_FILE="$arg" ;;
  esac
done
ENV_FILE="${ENV_FILE:-$(dirname "$0")/.env}"
[ -f "$ENV_FILE" ] || { echo "missing $ENV_FILE" >&2; exit 1; }

tmp="$(mktemp "${ENV_FILE}.XXXXXX")"
trap 'rm -f "$tmp"' EXIT
tr -d '\r' < "$ENV_FILE" > "$tmp"

# Value of KEY in the working copy (empty when absent). Splits on the first '=' only.
get() { local line; line="$(grep -m1 "^$1=" "$tmp" || true)"; printf '%s' "${line#*=}"; }
# Sets KEY unless it already has a non-empty value; $2 is the value. Logs only the key name.
changed=0
fill() {
  local key="$1" value="$2"
  [ -n "$(get "$key")" ] && return 0
  [ -n "$value" ] || return 0
  { grep -v "^${key}=" "$tmp" || true; } > "$tmp.new"
  if [ -s "$tmp.new" ] && [ -n "$(tail -c1 "$tmp.new")" ]; then printf '\n' >> "$tmp.new"; fi
  printf '%s=%s\n' "$key" "$value" >> "$tmp.new"
  mv "$tmp.new" "$tmp"
  changed=1
  echo "set $key"
}

first_host="$(get DJANGO_ALLOWED_HOSTS | cut -d, -f1 | tr -d '[:space:]')"
fill ConnectionStrings__Storage "$(get AZURE_STORAGE_CONNECTION_STRING)"
fill Admin__UserName "$(get DJANGO_SUPERUSER_USERNAME)"
[ -n "$(get Admin__UserName)" ] || fill Admin__UserName austy
fill Admin__Password "$(get DJANGO_SUPERUSER_PASSWORD)"
fill Mapy__ApiKey "$(get MAPY_API_KEY)"
fill APP_DOMAIN "$first_host"
# ";localhost" keeps host filtering from rejecting the container healthcheck (Host: localhost:8080).
fill AllowedHosts "${first_host:+$first_host;localhost}"

account="$(get ConnectionStrings__Storage | sed -n 's/.*AccountName=\([^;]*\).*/\1/p')"
[ -n "$account" ] && fill Storage__PublicBlobBaseUrl "https://${account}.blob.core.windows.net/toiletlabels/"
fill ASPNETCORE_ENVIRONMENT Production

# Keys the app cannot run (or be routed) without. Admin__Password and Mapy__ApiKey only warn.
missing=0
for key in ConnectionStrings__Storage Storage__PublicBlobBaseUrl APP_DOMAIN AllowedHosts; do
  if [ -z "$(get "$key")" ]; then echo "MISSING $key (no source value found)" >&2; missing=1; fi
done
for key in Admin__Password Mapy__ApiKey; do
  [ -n "$(get "$key")" ] || echo "warning: $key is empty" >&2
done
if [ "$missing" -ne 0 ]; then
  echo "migration incomplete, $ENV_FILE left unchanged" >&2
  exit 1
fi

if [ "$drop_legacy" -eq 1 ]; then
  before="$(wc -l < "$tmp")"
  { grep -Ev '^(AZURE_STORAGE_CONNECTION_STRING|MAPY_API_KEY|DJANGO_[A-Za-z0-9_]*|CSRF_TRUSTED_ORIGINS)=' "$tmp" || true; } > "$tmp.new"
  mv "$tmp.new" "$tmp"
  if [ "$before" -ne "$(wc -l < "$tmp")" ]; then changed=1; echo "dropped legacy keys"; fi
fi

# Also rewritten (and backed up) when only CRLF line endings were normalised.
if [ "$changed" -eq 0 ] && cmp -s "$tmp" "$ENV_FILE"; then
  echo "nothing to change"
  exit 0
fi

backup="${ENV_FILE%/*}"
[ "$backup" = "$ENV_FILE" ] && backup="."
backup="$backup/$(basename "$ENV_FILE").bak-$(date +%Y%m%d%H%M%S)"
cp -p "$ENV_FILE" "$backup"
chmod 600 "$backup" "$tmp"
mv "$tmp" "$ENV_FILE"
echo "updated $ENV_FILE (backup: $(basename "$backup"))"
