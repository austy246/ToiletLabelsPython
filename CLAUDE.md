# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ASP.NET Core 10 (Razor Pages) web app displaying a gallery of paired toilet sign images (men/women) with a map. Uses Azure Blob Storage for images and Azure Table Storage for metadata and users (no relational database). Runs on the shared OVH VPS and is deployed from the local machine (see Deployment and [docs/provoz-vps.md](docs/provoz-vps.md)). The repo name still says "Python" for historical reasons: the original Django app was removed after the .NET cutover (24 Oct 2026); its migration notes are kept in [docs/migrace-vps.md](docs/migrace-vps.md).

## Commands

```powershell
# Tests (xUnit; starts its own Azurite through Docker, so Docker must be running)
dotnet test

# Local stack: app + Azurite (Azure Storage emulator)
docker compose up -d --build
```

- App: http://localhost:8000 (health check: http://localhost:8000/healthz)
- Azurite: blobs on `localhost:10100`, tables on `localhost:10102`, data in the `toiletlabels_azurite-data` volume
- Local admin: put `Admin__UserName` and `Admin__Password` in a gitignored `.env.local` (see `.env.example`); without it the app starts, just without an admin account
- Map: `MAPY_API_KEY` is interpolated into the compose file from the root `.env` (`Mapy__ApiKey`); without it the map is hidden
- Image self-check: `docker run --rm toiletlabels-app:local --selftest`

No linting or separate build steps are configured.

## Development Workflow

**Work directly on `main` — do NOT create feature branches.** Commit changes straight to `main` and push. (This overrides any default "branch first" behavior.) Pushing to `main` deploys to production (GitHub Actions, see Deployment); pushes that touch only `docs/**` or `*.md` do not.

## Architecture

### Layout

- [src/ToiletLabels](src/ToiletLabels) - the Razor Pages app; [tests/ToiletLabels.Tests](tests/ToiletLabels.Tests) - unit and integration tests (Azurite via Docker)
- `Pages/` - Razor Pages (gallery, upload, edit, login, logout, error); Tailwind CSS via CDN in `Pages/Shared/_Layout.cshtml`, no frontend build tooling
- `Labels/` - label model, entity mapping, `LabelRepository`, editing rules, map points
- `Images/` - image upload/processing (Magick.NET, HEIC and WebP thumbnails), blob names, `BlobImageStore`
- `Geo/` - coordinate resolution (manual coordinates, Nominatim geocoder)
- `Users/` - admin seeding, cookie auth setup, user repository
- `Storage/` - options and bootstrapper that creates tables/containers at startup
- `Cli/` - non-web modes of the same binary (see below)

### Storage

- **Azure Table `ToiletLabels`**: label metadata (`PartitionKey='label'`, `RowKey=<uuid>`); entities store only blob filenames, never full URLs (pages build URLs from `Storage__PublicBlobBaseUrl`)
- **Azure Table `ToiletLabelsUsers`**: users (the admin is created at startup from `Admin__*` if missing; an existing user's password is never changed from config)
- **Blob container `toiletlabels`** (public): uploaded images and thumbnails
- **Blob container `toiletlabels-keys`** (private): ASP.NET Core Data Protection key ring (`keys.xml`), so cookies survive restarts and image updates
- No SQLite and no EF Core

### CLI modes (same binary, no web server)

| Flag | Purpose |
|------|---------|
| `--healthcheck` | Container healthcheck: GET `http://localhost:8080/healthz`, exit code 0 on HTTP 200 |
| `--selftest` | Verifies the runtime image (Magick.NET native libs, HEIC, WebP, ICU); needs no storage or network |
| `--check-data` | **Read-only**: reads every label entity in production storage and reports what it finds (coordinates, thumbnails, `Created`, unreadable entities); exit code 1 if an entity is unreadable |

### URL Routes

| Path | Page | Access |
|------|------|--------|
| `/` | gallery + map | Public |
| `/upload/` | upload | Admin only |
| `/pair/{id}/edit/` | edit | Admin only |
| `/login/` | login | Public |
| `/logout/` | logout (POST only) | Signed in |
| `/healthz` | health endpoint | Public |

## Environment Variables

ASP.NET Core configuration (double underscore = nested section). Locally via compose / `.env.local`, in production via `/srv/toiletlabels/deploy/.env`.

| Variable | Purpose |
|----------|---------|
| `ConnectionStrings__Storage` | Required. Storage connection string for tables and blobs |
| `Storage__PublicBlobBaseUrl` | Public base URL of the `toiletlabels` container (e.g. `https://toiletlabelsstorage.blob.core.windows.net/toiletlabels/`) |
| `Mapy__ApiKey` | API key for Mapy.com map tiles; without it the map is hidden |
| `Admin__UserName` / `Admin__Password` | Admin account created on startup if missing |
| `AllowedHosts` | Host filter, `<domain>;localhost` in production (`localhost` keeps the container healthcheck working) |
| `ASPNETCORE_ENVIRONMENT` | `Production` on the VPS, `Development` in local compose |
| `APP_DOMAIN` | Public domain (production `.env` only): used to render the Caddy block |
| `APP_IMAGE` | Image reference to run (production `.env` only): written by `remote-deploy.sh` |

## Deployment

### VPS (shared with manarozeniny and az-portal-python)

Production runs on the shared OVH VPS; operations (server, secrets, DNS, deleting a gallery entry) are in [docs/provoz-vps.md](docs/provoz-vps.md). The former Azure Web App was deleted on 14 Oct 2026. Only the storage account `toiletlabelsstorage` (resource group `ToiletLabels`) remains and holds all data, **so never delete that resource group or storage account**.

**Every push to `main` deploys** via [.github/workflows/deploy.yml](.github/workflows/deploy.yml) (free for this public repo): `dotnet test` → build + push `ghcr.io/austy246/toiletlabels-app:<full sha>` (+ `latest`, gha cache) → over SSH: upload `deploy/` from the commit, sync secrets into the server `.env` via stdin (`update-env-secrets.sh`), run `remote-deploy.sh <image> --selftest`, check `/healthz`. Docs/markdown-only pushes are skipped; `workflow_dispatch` re-runs it manually. GitHub configuration: secrets `SSH_HOST`, `SSH_USER`, `SSH_KEY` (CI key, public part in `~deploy/.ssh/authorized_keys`), `AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`, `ADMIN_USERNAME`, `ADMIN_PASSWORD`; variable `SSH_KNOWN_HOSTS` (pinned host key). GitHub secrets are the source of truth for those four app values (empty secrets never blank a server value).

**Fallback / rollback from the local machine** with [deploy/deploy.ps1](deploy/deploy.ps1) (PowerShell 7.2+, Docker with buildx) — runs the same server-side steps; do not run it while a GitHub deploy is in progress:

```powershell
pwsh ./deploy/deploy.ps1                 # tests, buildx linux/amd64, push :<full sha>, upload deploy/, remote-deploy.sh, /healthz check
pwsh ./deploy/deploy.ps1 -PrepareOnly    # migrate deploy/.env on the server + read-only --check-data; deploys nothing
pwsh ./deploy/deploy.ps1 -Tag <sha>      # redeploy / rollback an existing image (builds nothing)
pwsh ./deploy/deploy.ps1 -UpdateSecrets  # streams gitignored deploy/secrets.env into update-env-secrets.sh
pwsh ./deploy/deploy.ps1 -DryRun         # print the commands only
```

- Without `-Tag` it refuses to run with uncommitted tracked changes or untracked build inputs, tags the image `ghcr.io/austy246/toiletlabels-app:<full sha>` (plus `latest`), runs `dotnet test` first. `deploy/` is uploaded from the commit of the tag; `remote-deploy.sh` is streamed from the working copy.
- GHCR login (once, the gh token needs `write:packages`): `gh auth token | docker login ghcr.io -u austy246 --password-stdin`
- ssh uses its defaults (ssh-agent, `~/.ssh/config`) unless `-SshKey <file>` is given; target `deploy@57.131.165.173`.
- Server: VPS `57.131.165.173` (Debian), project dir `/srv/toiletlabels/deploy`, compose project `toiletlabels`, URL `https://toiletlabels.austy.cz`.
- Routing/TLS: the shared Caddy of `manarozeniny` (compose project `deploy`) imports `/srv/infra/sites/*.caddy`. `remote-deploy.sh` renders [deploy/toiletlabels.caddy](deploy/toiletlabels.caddy), runs `caddy validate`, then `caddy reload`, and rolls back an invalid block. The app joins the external Docker network `web` with alias `toiletlabels-app` and listens on port 8080. Certificate via HTTP-01, so the DNS A/AAAA records `toiletlabels` (zone `austy.cz`) must point to the VPS.
- Low priority on the shared VPS: `cpu_shares: 256`, `oom_score_adj: 500`, `mem_limit: 512m`; the healthcheck timeout is 15 s because a fresh dotnet process can be slow when the VPS is busy.
- State: none on the server. Labels, users, images and the Data Protection key ring all live in Azure Storage. No database, no backups beyond Azure Storage redundancy.
- Server access: `ssh deploy@57.131.165.173` (group `docker`, no sudo) or `debian@57.131.165.173` (passwordless sudo, no docker).
- Health endpoint: `GET /healthz`.
