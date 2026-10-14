# Provoz ToiletLabels na VPS: co je nastavené a jak to udržovat

Stav k **14. 10. 2026**. Postup migrace a jejího zakončení je v `docs/migrace-vps.md`. **Žádné hodnoty tajných údajů
v tomto repu nejsou**, jen názvy a místa, kde se nacházejí.

## 1. Server a aplikace

| Co | Hodnota |
|---|---|
| Adresa | https://toiletlabels.austy.cz (healthcheck `GET /healthz`) |
| Server | OVH VPS `57.131.165.173` / `2001:41d0:701:1100::ea6c`, Debian, sdílený s `manarozeniny` a `az-portal-python` |
| Adresář / compose projekt | `/srv/toiletlabels` (vlastník `deploy`), projekt `toiletlabels`, kontejner `toiletlabels-app-1` |
| Docker | síť `web` (externí, alias `toiletlabels-app`), volume `toiletlabels_app_data` (SQLite `/data/db.sqlite3`) |
| Caddy | sdílený kontejner `deploy-caddy-1` (projekt `manarozeniny`), náš blok `/srv/infra/sites/toiletlabels.caddy` |
| Přístup | `ssh -i ~/.ssh/id_rsa_austy deploy@57.131.165.173` (skupina `docker`, bez sudo); `debian@...` má sudo bez hesla, ale ne docker |
| Image | `ghcr.io/austy246/toiletlabels-app` (tagy `latest` a SHA commitu) |

Užitečné příkazy (jako `deploy` v `/srv/toiletlabels/deploy`):

```bash
docker compose ps
docker compose logs --tail 100 app
docker stats --no-stream
```

## 2. Nasazení

Workflow `Deploy to VPS` (`.github/workflows/deploy-vps.yml`) běží při každém pushi do `main` na kódu aplikace
(změny jen v `docs/**` a `*.md` ho nespouští) a ručně přes **Run workflow**. Postup: testy, build image, push do GHCR,
SSH na server, `docker compose up -d --wait`, vykreslení bloku Caddy (`deploy/toiletlabels.caddy`, `__APP_DOMAIN__` se
nahradí hodnotou z `DJANGO_ALLOWED_HOSTS`), `caddy validate`, `caddy reload` (při chybě se starý blok vrátí).

Návrat na starší verzi: Actions → **Deploy to VPS** → Run workflow na starším commitu.

## 3. Data a databáze

- **Všechna data jsou v Azure**, storage účet `toiletlabelsstorage` (skupina `ToiletLabels`, předplatné „My Own
  Subscription“ `b355f86c-94b4-467b-b643-1206cbf0e24c`): tabulka `ToiletLabels` (entity `PartitionKey=label`,
  `RowKey=<uuid>`) a kontejner `toiletlabels` (obrázky `<uuid>_men.*`, `<uuid>_women.*` a miniatury `*_thumb.webp`).
  **Tuto skupinu nikdy nemazat.** Žádné zálohy dat nejsou nastavené (jen redundance Azure Storage).
- **SQLite na VPS** obsahuje jen superuživatele a sessions. `docker-entrypoint.sh` při startu spustí `migrate` a vytvoří
  superuživatele z `DJANGO_SUPERUSER_USERNAME` / `DJANGO_SUPERUSER_PASSWORD`, pokud neexistuje. Ztráta volume tedy znamená
  jen odhlášení. Heslo existujícího účtu se z env nepřepisuje (změna: `docker compose exec app python manage.py changepassword austy`).

## 4. Smazání záznamu z galerie

Aplikace nemá mazání, jen nahrání a editaci. Záznam s obrázky se maže přímo v Azure Storage: najít entitu podle `Place`
v tabulce `ToiletLabels`, smazat její čtyři blobs (`MenImageUrl`, `WomenImageUrl`, `MenThumbUrl`, `WomenThumbUrl`) a pak
entitu. Mazání je nevratné, takže vždy nejdřív vypsat, co se smaže. Postup použitý 14. 10. 2026 (test „TEST smazat“):
`azure-data-tables` + `azure-storage-blob` s připojovacím řetězcem z GitHub secretu / `deploy/.env`.

## 5. GitHub secrets a `deploy/.env`

Secrets repa `austy246/ToiletLabelsPython`: `SSH_HOST`, `SSH_USER`, `SSH_KEY` (vlastní klíč `toiletlabels-ci`, veřejná část
v `~deploy/.ssh/authorized_keys`, odvolání = smazat ten řádek), `APP_DOMAIN`, `AZURE_STORAGE_CONNECTION_STRING`,
`MAPY_API_KEY`, `DJANGO_SUPERUSER_USERNAME`, `DJANGO_SUPERUSER_PASSWORD`. Původní hodnoty pocházely z nastavení smazané
Azure Web App. Workflow je zapisuje do `/srv/toiletlabels/deploy/.env` skriptem `deploy/update-env-secrets.sh`
(povolené klíče, prázdné hodnoty se přeskočí; práva 600). Soubor `.env` vytvořil jednorázově `deploy/gen-env.sh`
(vygeneroval `DJANGO_SECRET_KEY`, nikdy se nepřepisuje).

## 6. DNS

Zóna `austy.cz` je u Wedos (nameservery `ns.wedos.cz/.eu/.com/.net`). Záznamy: A `toiletlabels` → `57.131.165.173` a
AAAA `toiletlabels` → `2001:41d0:701:1100::ea6c`. Zóna má wildcard `*` (A `185.8.237.22`, AAAA `2a0e:acc0::d22`) na jiný
server; konkrétní záznam má před wildcardem přednost, ale **AAAA záznam musí existovat**, jinak by IPv6 klienti (a Let's
Encrypt) mířili na wildcard. Certifikát vystavuje Caddy přes HTTP-01 (port 80 musí zůstat volný).

## 7. Údržba

| Kdy | Co |
|---|---|
| podle potřeby | rotace klíče storage účtu: po ní aktualizovat secret `AZURE_STORAGE_CONNECTION_STRING` a spustit deploy |
| průběžně | `docker stats --no-stream`, `df -h /`, `docker system df` na serveru (disk je sdílený s dalšími projekty) |
| při potřebě | nový klíč pro CI: vygenerovat ed25519, veřejnou část přidat do `authorized_keys`, privátní nahrát do `SSH_KEY` |

## 8. Historie

Do 14. 10. 2026 běžela aplikace na Azure Web App `toiletlabels` (plán `ToiletLabelsPlan`, F1), nasazovaná přes
`azure-webapp.yml`. Web App i plán jsou smazané, `azure-webapp.yml` odstraněn; `startup.sh` zůstal v repu jako pozůstatek.
