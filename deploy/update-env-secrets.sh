#!/usr/bin/env bash
# Merges KEY=VALUE lines read from STDIN into deploy/.env. Secrets arrive on stdin only (never in
# argv). Only allow-listed keys are written; empty values are skipped, so a missing GitHub secret
# never blanks a value that works. Other lines in the file are left untouched.
set -euo pipefail
umask 077

ENV_FILE="${ENV_FILE:-$(dirname "$0")/.env}"
ALLOWED_KEYS="${ALLOWED_KEYS:-ConnectionStrings__Storage Mapy__ApiKey Admin__UserName Admin__Password}"

[ -f "$ENV_FILE" ] || { echo "missing $ENV_FILE" >&2; exit 1; }

tmp="$(mktemp "${ENV_FILE}.XXXXXX")"
trap 'rm -f "$tmp" "$tmp.new"' EXIT
cp "$ENV_FILE" "$tmp"

while IFS= read -r line || [ -n "$line" ]; do
  line="${line%$'\r'}"   # tolerate CRLF in a secrets.env written on Windows
  [ -z "$line" ] && continue
  case "$line" in *=*) ;; *) echo "skipping malformed line" >&2; continue ;; esac   # never echo a line without a key
  key="${line%%=*}"
  value="${line#*=}"
  case " $ALLOWED_KEYS " in
    *" $key "*) ;;
    *) echo "skipping key not allowed: $key" >&2; continue ;;
  esac
  [ -z "$value" ] && continue
  grep -v "^${key}=" "$tmp" > "$tmp.new" || true
  printf '%s=%s\n' "$key" "$value" >> "$tmp.new"
  mv "$tmp.new" "$tmp"
done

mv "$tmp" "$ENV_FILE"
