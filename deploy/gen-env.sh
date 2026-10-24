#!/usr/bin/env bash
# Creates deploy/.env on a clean server. Never overwrites an existing file.
# Needs APP_DOMAIN (e.g. toiletlabels.austy.cz) in the environment. The secrets (ConnectionStrings__Storage,
# Mapy__ApiKey, Admin__UserName, Admin__Password) are added afterwards by update-env-secrets.sh
# (deploy.ps1 -UpdateSecrets). An existing server with the old Django .env is converted by migrate-env.sh.
set -euo pipefail
cd "$(dirname "$0")"

if [ -f .env ]; then
  echo ".env already exists, leaving it alone"
  exit 0
fi

: "${APP_DOMAIN:?APP_DOMAIN is required for the first deploy}"

umask 077
cat > .env <<EOF2
COMPOSE_PROJECT_NAME=toiletlabels
APP_DOMAIN=${APP_DOMAIN}
AllowedHosts=${APP_DOMAIN}
ASPNETCORE_ENVIRONMENT=Production
Storage__PublicBlobBaseUrl=https://toiletlabelsstorage.blob.core.windows.net/toiletlabels/
APP_IMAGE=
EOF2
echo "created deploy/.env"
