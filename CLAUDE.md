# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Django 5.2 web app displaying a gallery of paired toilet sign images (men/women). Uses Azure Blob Storage for images and Azure Table Storage for metadata (no Django models for domain data). Deployed to Azure Web App via GitHub Actions.

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

## Deployment

GitHub Actions ([azure-webapp.yml](.github/workflows/azure-webapp.yml)) deploys on push to `main`. Azure builds with Oryx. [startup.sh](startup.sh) runs migrations, creates superuser from env vars, and starts Gunicorn on port 8000.
