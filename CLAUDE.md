# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Django 5.2 web app displaying a gallery of paired toilet sign images (men/women). Uses Azure Blob Storage for images and Azure Table Storage for metadata (no Django models for domain data). Deployed to the shared OVH VPS via GitHub Actions (see Deployment and [docs/migrace-vps.md](docs/migrace-vps.md)).

## Commands

```powershell
# Install dependencies
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt

# Run dev server (requires AZURE_STORAGE_CONNECTION_STRING env var)
python manage.py migrate
python manage.py runserver

# Create superuser locally
python manage.py createsuperuser
```

Tests live in `gallery/tests.py` and run with `python manage.py test gallery`. No linting or build steps are configured.

## Development Workflow

**Work directly on `main` — do NOT create feature branches.** Commit changes straight to `main` and push; pushing to `main` triggers the Azure deploy. (This overrides any default "branch first" behavior.)

## Architecture

### Storage Pattern (no Django models)

This app does **not** use Django ORM models for domain data. Instead:
- **Azure Table Storage** (`ToiletLabels` table) stores label metadata as entities with `PartitionKey='label'` and `RowKey=<uuid>`
- **Azure Blob Storage** (`toiletlabels` container) stores uploaded images
- Table entities store only blob filenames (not full URLs); templates construct URLs via `AZURE_BLOB_BASE_URL + filename`
- SQLite is only used for Django's auth/session system

### Key Services

- [azure_table.py](gallery/services/azure_table.py) - `AzureTableManager`: CRUD for label entities (upsert, get, list sorted by Created desc)
- [azure_blob.py](gallery/services/azure_blob.py) - `AzureBlobManager`: image upload, base URL derivation from connection string

### URL Routes

| Path | View | Access |
|------|------|--------|
| `/` | `signpair_list` | Public |
| `/upload/` | `upload_label` | Superuser only |
| `/pair/<uuid>/edit/` | `edit_label` | Superuser only |
| `/admin/` | Django admin | Staff |
| `/login/`, `/logout/` | Django auth | Public |

### Templates

Templates live in `gallery/templates/gallery/`. Base template uses Tailwind CSS via CDN. No build tooling for frontend assets; WhiteNoise serves static files.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `AZURE_STORAGE_CONNECTION_STRING` | Required. Used by both blob and table services |
| `MAPY_API_KEY` | API key for Mapy.com map tiles (homepage map). Set in `.env` locally and as an Azure App Setting |
| `DJANGO_SECRET_KEY` | Django secret key (has insecure default for dev) |
| `DJANGO_DEBUG` | Set to `True` for debug mode |
| `DJANGO_ALLOWED_HOSTS` | Comma-separated hosts |
| `CSRF_TRUSTED_ORIGINS` | Comma-separated origins |
| `DJANGO_DB_PATH` | SQLite file path (default `db.sqlite3` in the project; `/data/db.sqlite3` in the container) |
| `DJANGO_BEHIND_PROXY` | `True` behind the VPS Caddy: trusts `X-Forwarded-Proto`, secure cookies |
| `DJANGO_SUPERUSER_USERNAME` / `DJANGO_SUPERUSER_PASSWORD` | Superuser created on startup if missing (default username `austy`) |

## Deployment

### VPS (shared with manarozeniny and az-portal-python)

Production runs on the shared OVH VPS; setup state and operations are in [docs/migrace-vps.md](docs/migrace-vps.md).
The former Azure Web App was deleted on 14 Oct 2026. Only the storage account `toiletlabelsstorage` (resource group `ToiletLabels`) remains and holds all data, so never delete that group. [startup.sh](startup.sh) is a leftover of the Azure deployment (the container uses [docker-entrypoint.sh](docker-entrypoint.sh)).

- URL `https://toiletlabels.austy.cz`, server `57.131.165.173` (Debian), project dir `/srv/toiletlabels`, compose project `toiletlabels`.
- [deploy-vps.yml](.github/workflows/deploy-vps.yml): tests, builds the [Dockerfile](Dockerfile), pushes to `ghcr.io/austy246/toiletlabels-app`, then over SSH pulls and starts [deploy/docker-compose.yml](deploy/docker-compose.yml). It runs on every push to `main` (and manually via `workflow_dispatch`), so pushing to `main` deploys.
- TLS/routing: the shared Caddy of `manarozeniny` (compose project `deploy`) imports `/srv/infra/sites/*.caddy`. The workflow renders [deploy/toiletlabels.caddy](deploy/toiletlabels.caddy), runs `caddy validate`, then `caddy reload`, and rolls back on an invalid config. The app joins the external Docker network `web` with alias `toiletlabels-app`. Certificate via HTTP-01, so the DNS A record `toiletlabels` (zone `austy.cz`) must point to the VPS.
- State: SQLite in volume `app_data` (`/data/db.sqlite3`: only the superuser and sessions, recreated from env by the entrypoint). Labels and images stay in Azure. No Postgres, no backups.
- `deploy/.env` on the server is created once by `deploy/gen-env.sh` and merged with secrets by `deploy/update-env-secrets.sh` (allow-listed keys, never in argv).
- GitHub secrets: `SSH_HOST`, `SSH_USER`, `SSH_KEY` (dedicated key `toiletlabels-ci`, public part in `~deploy/.ssh/authorized_keys`), `APP_DOMAIN`, `AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`, `DJANGO_SUPERUSER_USERNAME`, `DJANGO_SUPERUSER_PASSWORD`. Values came from the App Service settings.
- Server access: `ssh -i ~/.ssh/id_rsa_austy deploy@57.131.165.173` (group `docker`, no sudo) or `debian@57.131.165.173` (passwordless sudo, no docker).
- Health endpoint: `GET /healthz`.
