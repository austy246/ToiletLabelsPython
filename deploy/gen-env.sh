#!/usr/bin/env bash
# Creates deploy/.env on the first deploy. Never overwrites an existing file.
# Needs APP_DOMAIN (e.g. toalety.example.cz) in the environment.
set -euo pipefail
cd "$(dirname "$0")"

if [ -f .env ]; then
  echo ".env already exists, leaving it alone"
  exit 0
fi

: "${APP_DOMAIN:?APP_DOMAIN is required for the first deploy}"

umask 077
key="$(openssl rand -hex 32)"
cat > .env <<EOF2
COMPOSE_PROJECT_NAME=toiletlabels
DJANGO_SECRET_KEY=${key}
DJANGO_ALLOWED_HOSTS=${APP_DOMAIN},localhost,127.0.0.1
CSRF_TRUSTED_ORIGINS=https://${APP_DOMAIN}
DJANGO_BEHIND_PROXY=True
APP_IMAGE=
EOF2
echo "created deploy/.env"
