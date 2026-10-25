# Provoz ToiletLabels na VPS: co je nastavené a jak to udržovat

Stav k **24. 10. 2026** (po přechodu z Djanga na ASP.NET Core 10). Historie původní migrace na VPS je v
`docs/migrace-vps.md`. **Žádné hodnoty tajných údajů v tomto repu nejsou**, jen názvy a místa, kde se nacházejí.

## 1. Server a aplikace

| Co | Hodnota |
|---|---|
| Adresa | https://toiletlabels.austy.cz (healthcheck `GET /healthz`) |
| Server | OVH VPS `57.131.165.173` / `2001:41d0:701:1100::ea6c`, Debian, sdílený s `manarozeniny` a `az-portal-python` |
| Adresář / compose projekt | `/srv/toiletlabels` (vlastník `deploy`), projekt `toiletlabels`, kontejner `toiletlabels-app-1`, compose soubor `/srv/toiletlabels/deploy/docker-compose.yml` |
| Docker | síť `web` (externí, alias `toiletlabels-app`, aplikace poslouchá na `8080`); žádný datový volume |
| Priorita | `cpu_shares: 256`, `oom_score_adj: 500`, `mem_limit: 512m`, `cpus: 1`: nižší priorita, aby nezdržela ostatní projekty |
| Healthcheck | `dotnet ToiletLabels.dll --healthcheck`, interval 30 s, **timeout 15 s** (čerstvý dotnet proces umí na vytíženém VPS trvat přes 5 s) |
| Caddy | sdílený kontejner `deploy-caddy-1` (projekt `manarozeniny`), náš blok `/srv/infra/sites/toiletlabels.caddy` |
| Přístup | `ssh deploy@57.131.165.173` (skupina `docker`, bez sudo; klíč z ssh-agenta nebo `~/.ssh/config`); `debian@...` má sudo bez hesla, ale ne docker |
| Image | `ghcr.io/austy246/toiletlabels-app` (tagy `latest` a plný SHA commitu; balíček je veřejný, server tahá anonymně) |

Užitečné příkazy (jako `deploy` v `/srv/toiletlabels/deploy`):

```bash
docker compose ps
docker compose logs --tail 100 app
docker stats --no-stream
```

## 2. Nasazení a návrat na starší verzi

**Každý push do `main` nasazuje** přes GitHub Actions (`.github/workflows/deploy.yml`, pro veřejný repozitář zdarma):
testy → build a push image `ghcr.io/austy246/toiletlabels-app:<plný sha>` → přes SSH nahrání `deploy/` z commitu,
synchronizace tajných hodnot do `.env` (stdin) a `remote-deploy.sh --selftest` → kontrola `/healthz`. Push, který mění jen
`docs/**` nebo `*.md`, nenasazuje; ručně jde workflow spustit přes „Run workflow“ (`workflow_dispatch`).

GitHub nastavení: secrets `SSH_HOST`, `SSH_USER`, `SSH_KEY` (CI klíč, veřejná část v `~deploy/.ssh/authorized_keys`),
`AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`, `ADMIN_USERNAME`, `ADMIN_PASSWORD` a proměnná `SSH_KNOWN_HOSTS`
(připnutý klíč serveru). Pro tyto čtyři hodnoty aplikace jsou GitHub secrets zdrojem pravdy; prázdný secret hodnotu na
serveru nikdy nesmaže.

**Záloha a návrat na starší verzi** z lokálního stroje skriptem `deploy/deploy.ps1` (PowerShell 7.2+, Docker s buildx).
Dělá na serveru totéž; nespouštět, když zrovna běží nasazení z GitHubu:

```powershell
pwsh ./deploy/deploy.ps1                 # nasazení HEAD
pwsh ./deploy/deploy.ps1 -PrepareOnly    # jen migrace .env na serveru + --check-data; nic nenasadí
pwsh ./deploy/deploy.ps1 -Tag <sha>      # nasazení již existujícího image (návrat na starší verzi), nic nebuildí
pwsh ./deploy/deploy.ps1 -UpdateSecrets  # pošle deploy/secrets.env do update-env-secrets.sh na serveru
pwsh ./deploy/deploy.ps1 -DryRun         # jen vypíše příkazy, nezavolá SSH ani registr
```

Postup bez `-Tag`: kontrola, že nejsou necommitované změny (ani neverzované soubory v `src/`), `dotnet test`,
`docker buildx build --platform linux/amd64`, push `ghcr.io/austy246/toiletlabels-app:<plný sha>` a `latest`, nahrání
`deploy/` z commitu přes `ssh`+`tar`, `remote-deploy.sh` (pull, `--selftest`, `docker compose up -d --wait`, vykreslení
bloku Caddy, `caddy validate`, `caddy reload`, při chybě se starý blok vrátí, úklid starých tagů jen našeho image) a
nakonec kontrola `GET /healthz` (5 pokusů). `remote-deploy.sh` si předchozí image pamatuje v `deploy/.previous-image`.

- **Přihlášení do GHCR** (jednou; token z `gh` potřebuje scope `write:packages`):
  `gh auth token | docker login ghcr.io -u austy246 --password-stdin`
- **SSH**: skript používá výchozí nastavení ssh (agent, `~/.ssh/config`); konkrétní klíč se vynutí `-SshKey <soubor>`.
- **Návrat**: `pwsh ./deploy/deploy.ps1 -Tag <sha>` s SHA dřívějšího commitu (tag image = plný SHA). Poslední Django
  image `5a76bb39ff751ca37a34d6b0771c5bc9bda143dd` na serveru po úklidu už nemusí být a nové `.env` ho nespustí (viz
  bod 5), takže návrat na něj znamená i obnovu `.env` ze zálohy; reálně vracejte jen na .NET commity.

Další režimy téhož binárního souboru:

- `--selftest`: ověří, že image umí Magick.NET (HEIC, WebP) a ICU; nepotřebuje storage ani síť. `remote-deploy.sh` ho
  spouští před každým nasazením (`docker run --rm IMAGE --selftest`).
- `--check-data`: **jen čte** tabulku `ToiletLabels` v produkčním storage a hlásí souřadnice, náhledy, `Created` a
  nečitelné entity. Nic nezapisuje ani nezakládá. Návratový kód 1 = nečitelná entita, chybějící tabulka nebo konfigurace.
  Spouští ho `-PrepareOnly`; ručně na serveru:
  `docker run --rm --env-file .env ghcr.io/austy246/toiletlabels-app:<sha> --check-data`.

## 3. Data a úložiště

- **Všechna data jsou v Azure**, storage účet `toiletlabelsstorage` (skupina `ToiletLabels`, předplatné „My Own
  Subscription“ `b355f86c-94b4-467b-b643-1206cbf0e24c`):
  - tabulka `ToiletLabels` (entity `PartitionKey=label`, `RowKey=<uuid>`) s metadaty štítků,
  - tabulka `ToiletLabelsUsers` s uživateli (admin se při startu založí z `Admin__UserName` / `Admin__Password`, pokud
    neexistuje; heslo existujícího účtu se z konfigurace nepřepisuje),
  - kontejner `toiletlabels` (veřejný): obrázky `<uuid>_men.*`, `<uuid>_women.*` a miniatury `*_thumb.webp`,
  - kontejner `toiletlabels-keys` (soukromý): klíče ASP.NET Core Data Protection (`keys.xml`).
  **Tuto skupinu ani storage účet nikdy nemazat.** Žádné zálohy dat nejsou nastavené (jen redundance Azure Storage).
- **Na VPS není žádný stav.** SQLite databáze z Djanga a její volume `toiletlabels_app_data` byly **24. 10. 2026
  odstraněny**, stejně jako Python image. Ztráta kontejneru nebo celého serveru tedy neznamená ztrátu dat.
- **Data Protection a důvěra**: klíče pro podpis cookies (přihlášení) a antiforgery tokeny jsou v kontejneru
  `toiletlabels-keys` jako **nešifrovaný** `keys.xml`. Kdo má přístup ke storage účtu (připojovací řetězec, klíč účtu),
  může přečíst klíče a **podvrhnout přihlašovací cookie**. Je to stejná úroveň důvěry jako u připojovacího řetězce
  samotného, který dává plný přístup k datům; kontejner proto musí zůstat soukromý. Po rotaci klíče storage účtu klíčový
  kontejner zůstává, jen je třeba aktualizovat `ConnectionStrings__Storage`. Smazání `keys.xml` jen odhlásí všechny
  uživatele (vygeneruje se nový klíč).

## 4. Smazání záznamu z galerie

Aplikace nemá mazání, jen nahrání a editaci, takže se mazání **stále dělá ručně v Azure** (portál nebo Storage Explorer):
najít entitu podle `Place` v tabulce `ToiletLabels`, smazat její čtyři blobs (`MenImageUrl`, `WomenImageUrl`,
`MenThumbUrl`, `WomenThumbUrl`) v kontejneru `toiletlabels` a pak entitu. Mazání je nevratné, takže vždy nejdřív vypsat,
co se smaže. Po smazání lze pustit `--check-data` jako kontrolu, že zbylá data jsou čitelná.

## 5. Konfigurace: `deploy/.env` a `migrate-env.sh`

`/srv/toiletlabels/deploy/.env` (práva 600, nikdy v gitu) obsahuje klíče:

| Klíč | Význam |
|---|---|
| `COMPOSE_PROJECT_NAME` | `toiletlabels` |
| `APP_DOMAIN` | veřejná doména (dosadí se do bloku Caddy) |
| `AllowedHosts` | `<doména>;localhost` (`localhost` kvůli healthchecku kontejneru) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Storage__PublicBlobBaseUrl` | `https://toiletlabelsstorage.blob.core.windows.net/toiletlabels/` |
| `ConnectionStrings__Storage` | tajné: připojovací řetězec storage účtu |
| `Mapy__ApiKey` | tajné: klíč Mapy.com |
| `Admin__UserName`, `Admin__Password` | tajné: admin účet zakládaný při startu |
| `APP_IMAGE` | image, který běží (přepisuje `remote-deploy.sh`) |

- **Tajné hodnoty** se na server dostávají při každém nasazení z GitHub secrets (viz kap. 2), nebo ručně přes
  `pwsh ./deploy/deploy.ps1 -UpdateSecrets`: lokální gitignorovaný
  `deploy/secrets.env` (řádky `KEY=VALUE`) se proudem pošle na stdin skriptu `deploy/update-env-secrets.sh`, nikdy v
  argumentech. Zapisují se jen povolené klíče a prázdné hodnoty se přeskočí.
- **Čistý server**: `APP_DOMAIN=<doména> bash deploy/gen-env.sh` vytvoří `.env` (nikdy nepřepíše existující), pak
  `-UpdateSecrets`.
- **`deploy/migrate-env.sh`** převádí `.env` ve starém formátu Djanga (`AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`,
  `DJANGO_*`, `CSRF_TRUSTED_ORIGINS`) na klíče ASP.NET Core. Je idempotentní, doplňuje jen chybějící nebo prázdné klíče,
  nikdy nepřepisuje hodnoty a vypisuje jen názvy klíčů. Spouští ho `remote-deploy.sh --prepare-only` (tj. `-PrepareOnly`).
  Před každou změnou souboru vytvoří zálohu `deploy/.env.bak-<datum a čas>` (práva 600); záloha může obsahovat tajné
  hodnoty, proto ji po ověření funkčního nasazení smazat.
- **`--drop-legacy`**: po úspěšné migraci odstraní zastaralé klíče (`AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`,
  `DJANGO_*`, `CSRF_TRUSTED_ORIGINS`). Spouští se ručně na serveru: `bash migrate-env.sh --drop-legacy`. Návrat na Django
  by pak vyžadoval zálohu `.env.bak-*`.

## 6. DNS

Zóna `austy.cz` je u Wedos (nameservery `ns.wedos.cz/.eu/.com/.net`). Záznamy: A `toiletlabels` → `57.131.165.173` a
AAAA `toiletlabels` → `2001:41d0:701:1100::ea6c`. Zóna má wildcard `*` (A `185.8.237.22`, AAAA `2a0e:acc0::d22`) na jiný
server; konkrétní záznam má před wildcardem přednost, ale **AAAA záznam musí existovat**, jinak by IPv6 klienti (a Let's
Encrypt) mířili na wildcard. Certifikát vystavuje Caddy přes HTTP-01 (port 80 musí zůstat volný).

## 7. Údržba

| Kdy | Co |
|---|---|
| podle potřeby | rotace klíče storage účtu: aktualizovat GitHub secret `AZURE_STORAGE_CONNECTION_STRING` a spustit workflow Deploy (nebo lokálně `deploy/secrets.env` + `deploy.ps1 -UpdateSecrets` a nasazení) |
| průběžně | `docker stats --no-stream`, `df -h /`, `docker system df` na serveru (disk je sdílený s dalšími projekty); na serveru nikdy `docker system prune` ani `docker image prune -af` |
| po nasazení | smazat staré `deploy/.env.bak-*` |
| při potřebě | jiný přístup přes SSH: veřejnou část klíče přidat do `~deploy/.ssh/authorized_keys`, odvolání = smazat ten řádek |

## 8. Historie

- Do 14. 10. 2026 běžela aplikace na Azure Web App `toiletlabels` (plán `ToiletLabelsPlan`, F1), nasazovaná přes
  `azure-webapp.yml`. Web App i plán jsou smazané.
- 14. 10. 2026 přechod na sdílený VPS jako Django aplikace (SQLite ve volume, nasazení GitHub Actions), viz
  `docs/migrace-vps.md`.
- Aplikace byla přepsána do ASP.NET Core 10 a produkce na ni byla přepnuta; **24. 10. 2026** byl odstraněn Django kód,
  GitHub workflow, SQLite volume a Python image na serveru.
- **25. 10. 2026** nasazení vráceno do GitHub Actions (každý push do `main`); `deploy.ps1` zůstává jako záloha.
