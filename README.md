# ToiletLabels

Reincarnation of my university time idea: a gallery of toilet sign images - always a pair of images, one for men and
one for women - with a map.

The admin can upload images (one for men, one for women) along with the place name, coordinates, and description.
Images are stored in Azure Blob Storage, metadata and users in Azure Table Storage.

The app is ASP.NET Core 10 (Razor Pages): `src/ToiletLabels`, tests in `tests/ToiletLabels.Tests`. It started as a
Django app (hence the repository name); the Django code was removed on 24 Oct 2026 after the cutover.

## Local Development

The local stack is the app plus Azurite (the Azure Storage emulator):

```sh
docker compose up -d --build
```

- App: http://localhost:8000 (health check: http://localhost:8000/healthz)
- Azurite: blobs on `localhost:10100`, tables on `localhost:10102`, data in the `toiletlabels_azurite-data` volume
- Admin login: put `Admin__UserName` and `Admin__Password` in a gitignored `.env.local` (see `.env.example`);
  without it the app still starts, just without an admin account
- Map: `MAPY_API_KEY` from the root `.env` is passed through; without it the map is hidden
- Tests: `dotnet test` (starts its own Azurite through Docker)
- Image self-check (Magick.NET with HEIC, WebP, ICU): `docker run --rm toiletlabels-app:local --selftest`

Measured on the local image (chiseled-extra runtime): 303 MB on disk (90.5 MB compressed content) versus 327 MB
(47 MB compressed) for the former Python image `5a76bb39`; about 42 MiB RSS idle.

## Deployment to the VPS

The app runs on a shared OVH VPS (`https://toiletlabels.austy.cz`) behind a shared Caddy. There is no CI: pushing to
`main` does not deploy. Deploy from the local machine (PowerShell 7.2+, Docker with buildx):

```sh
pwsh ./deploy/deploy.ps1                # tests, build linux/amd64, push to GHCR, deploy over ssh, check /healthz
pwsh ./deploy/deploy.ps1 -Tag <sha>     # redeploy / roll back an existing image
pwsh ./deploy/deploy.ps1 -PrepareOnly   # migrate the server .env and run the read-only --check-data
```

Details, secrets handling and server operations: [docs/provoz-vps.md](docs/provoz-vps.md). The notes on the earlier
Django migration are kept in [docs/migrace-vps.md](docs/migrace-vps.md).
