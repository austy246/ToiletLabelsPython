# Migrace ToiletLabels na sdílený VPS: runbook

Cíl: aplikace běží na stejném OVH VPS jako `manarozeniny` a `az-portal-python`, za jejich sdíleným Caddy.
Vzor: `az-portal-python/docs/provoz-vps.md`, `docs/migrace-vps.md`, `docs/navrh-sdileny-caddy.md`.

## Co se mění a co ne

- **Data se nestěhují.** Obrázky jsou v Azure Blob a metadata v Azure Table. VPS se připojí ke stejnému storage účtu
  přes `AZURE_STORAGE_CONNECTION_STRING`.
- **Databáze je SQLite ve volume `app_data` (`/data/db.sqlite3`).** Obsahuje jen účet superuživatele a sessions, takže
  není Postgres ani restic záloha. Superuživatele vytvoří `docker-entrypoint.sh` z `DJANGO_SUPERUSER_*`. Po přepnutí se
  musíte jednou znovu přihlásit (sessions se nekopírují).
- Azure Web App (`azure-webapp.yml`) dál nasazuje při pushi do `main`, dokud nepřepnete provoz. VPS workflow
  (`deploy-vps.yml`) je zatím jen ruční.

## Stav k 14. 10. 2026

- **Hotovo:** adresář `/srv/toiletlabels` na serveru (vlastník `deploy`), všechny GitHub secrets včetně `SSH_KEY`
  (vlastní klíč `toiletlabels-ci`, veřejná část v `~deploy/.ssh/authorized_keys`; odvolání = smazat ten řádek),
  A záznam `toiletlabels` → `57.131.165.173` v DNS zóně `austy.cz`, image se lokálně sestaví a odpovídá na `/healthz`.
- **Pozor:** v tu chvíli `austy.cz` nevracely veřejné DNS servery (`Non-existent domain` i pro apex), pravděpodobně
  nedokončená delegace nameserverů u registrátora. Dokud DNS nefunguje, deploy proběhne, ale Caddy nezíská
  certifikát. Po opravě ověřit `nslookup -type=A toiletlabels.austy.cz` (musí vrátit `57.131.165.173`) a
  `nslookup -type=AAAA toiletlabels.austy.cz` (nesmí vrátit wildcard `2a0e:acc0::d22`, jinak přidat AAAA záznam
  VPS), pak znovu spustit workflow nebo `caddy reload` v kontejneru `deploy-caddy-1`.
- **Zbývá:** spustit **Deploy to VPS**, ověřit krok B, po osvědčení krok E.
- **Přístup na server:** `ssh -i ~/.ssh/id_rsa_austy deploy@57.131.165.173` (skupina `docker`, bez sudo);
  `debian@57.131.165.173` má sudo bez hesla (pro věci vyžadující root, např. adresář v `/srv`).

## Prerekvizity na serveru (už hotové díky ostatním projektům)

Síť `web`, adresář `/srv/infra/sites` (vlastník `deploy`) a sdílený Caddy s `import /etc/caddy/sites.d/*.caddy`.
Ověřit: `docker network ls | grep web` a `ls -ld /srv/infra/sites`.
Jednou (hotovo, jako `debian`): `sudo mkdir -p /srv/toiletlabels && sudo chown deploy:deploy /srv/toiletlabels`.

## A. Příprava

1. **Doména:** `toiletlabels.austy.cz` (hodnota secretu `APP_DOMAIN`). V DNS přidat A záznam
   na `57.131.165.173` (TTL 300 s) **před prvním deployem**, jinak Caddy opakovaně selhává při HTTP-01.
   Ověřit `nslookup toiletlabels.austy.cz`.
2. GitHub secrets repa `austy246/ToiletLabelsPython` (hotovo, hodnoty z Azure App Service `toiletlabels`,
   skupina `ToiletLabels`, předplatné „My Own Subscription“):
   `SSH_HOST`, `SSH_USER`, `SSH_KEY` (vlastní klíč projektu), `APP_DOMAIN`,
   `AZURE_STORAGE_CONNECTION_STRING`, `MAPY_API_KEY`, `DJANGO_SUPERUSER_USERNAME`, `DJANGO_SUPERUSER_PASSWORD`
   (hodnoty z Azure App Service → Environment variables).
3. Image se publikuje do GHCR jako `ghcr.io/austy246/toiletlabels-app`. Deploy se přihlašuje tokenem workflow,
   takže nic dalšího netřeba.
4. Lokální zkouška image (volitelné): `docker build -t toiletlabels-app:local .` a
   `docker run --rm -p 8000:8000 -e DJANGO_ALLOWED_HOSTS=localhost toiletlabels-app:local`, pak `curl localhost:8000/healthz`.

## B. První nasazení (bez dopadu na uživatele)

1. Actions → **Deploy to VPS** → Run workflow.
2. Na serveru: `cd /srv/toiletlabels/deploy && docker compose ps` (služba `app` musí být `healthy`).
3. `curl -I https://toiletlabels.austy.cz/healthz` → 200. Přihlásit se na `/login/` superuživatelem, zkontrolovat galerii, mapu
   (`MAPY_API_KEY`), nahrání fotky (včetně HEIC z telefonu) a editaci.

## C. Přepnutí

1. Pokud existují odkazy na `toiletlabels.azurewebsites.net`, nahradit je novou adresou.
2. Azure Web App nechat běžet, dokud se nová adresa neosvědčí (oba používají stejná data v Azure, takže je to bezpečné).

## D. Návrat

Nic se nemigrovalo, takže stačí dál používat Azure Web App. Odstranění z VPS:
`rm /srv/infra/sites/toiletlabels.caddy` + `caddy reload` v Caddy kontejneru a `docker compose down` v `/srv/toiletlabels/deploy`.

## E. Po několika dnech

1. Přepnout `deploy-vps.yml` na `on: push: branches: [main]` a smazat `azure-webapp.yml`
   (CLAUDE.md: push do `main` pak nasazuje na VPS).
2. Vypnout Azure Web App.
3. Aktualizovat `CLAUDE.md` (sekce Deployment, proměnné prostředí) a `README.md`.
4. Zvážit rotaci klíče storage účtu.
