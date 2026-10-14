# Migrace ToiletLabels na sdílený VPS: runbook

Cíl: aplikace běží na stejném OVH VPS jako `manarozeniny` a `az-portal-python`, za jejich sdíleným Caddy.
Vzor: `az-portal-python/docs/provoz-vps.md`, `docs/migrace-vps.md`, `docs/navrh-sdileny-caddy.md`.

## Co se mění a co ne

- **Data se nestěhují.** Obrázky jsou v Azure Blob a metadata v Azure Table. VPS se připojí ke stejnému storage účtu
  přes `AZURE_STORAGE_CONNECTION_STRING`.
- **Databáze je SQLite ve volume `app_data` (`/data/db.sqlite3`).** Obsahuje jen účet superuživatele a sessions, takže
  není Postgres ani restic záloha. Superuživatele vytvoří `docker-entrypoint.sh` z `DJANGO_SUPERUSER_*`. Po přepnutí se
  musíte jednou znovu přihlásit (sessions se nekopírují).
- **Azure Web App `toiletlabels` byl 14. 10. 2026 smazán** (včetně plánu `ToiletLabelsPlan`). Ve skupině `ToiletLabels`
  zůstal jen storage účet `toiletlabelsstorage` s daty, **ten nemazat**. Workflow `azure-webapp.yml` je odstraněný
  a `deploy-vps.yml` nasazuje při každém pushi do `main`.

## Stav k 14. 10. 2026

- **Hotovo:** adresář `/srv/toiletlabels` na serveru (vlastník `deploy`), všechny GitHub secrets včetně `SSH_KEY`
  (vlastní klíč `toiletlabels-ci`, veřejná část v `~deploy/.ssh/authorized_keys`; odvolání = smazat ten řádek),
  A záznam `toiletlabels` → `57.131.165.173` v DNS zóně `austy.cz`, image se lokálně sestaví a odpovídá na `/healthz`.
- **Pozor:** v tu chvíli `austy.cz` nevracely veřejné DNS servery (`Non-existent domain` i pro apex), pravděpodobně
  nedokončená delegace nameserverů u registrátora. Dokud DNS nefunguje, deploy proběhne, ale Caddy nezíská
  certifikát. Po opravě ověřit `nslookup -type=A toiletlabels.austy.cz` (musí vrátit `57.131.165.173`) a
  `nslookup -type=AAAA toiletlabels.austy.cz` (nesmí vrátit wildcard `2a0e:acc0::d22`, jinak přidat AAAA záznam
  VPS), pak znovu spustit workflow nebo `caddy reload` v kontejneru `deploy-caddy-1`.
- **Hotovo:** první deploy proběhl, HTTPS funguje, DNS (A i AAAA `2001:41d0:701:1100::ea6c`) je v pořádku, přihlášení,
  mapa a nahrání fotky ověřeno. Zbývá jen zvážit rotaci klíče storage účtu (krok E).
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

## C. Přepnutí (hotovo)

Odkazy na `toiletlabels.azurewebsites.net` je potřeba nahradit adresou `https://toiletlabels.austy.cz`.

## D. Návrat (po smazání Azure Web App už není k dispozici)

Staré prostředí neexistuje. Při problému na VPS se opravuje VPS: logy `docker compose logs app` v
`/srv/toiletlabels/deploy`, nebo vrácení předchozí verze přes **Run workflow** na starším commitu. Dočasné vypnutí
webu: `rm /srv/infra/sites/toiletlabels.caddy` + `caddy reload` v Caddy kontejneru a `docker compose down` v
`/srv/toiletlabels/deploy`. Případný návrat na Azure by znamenal znovu vytvořit Web App (viz historie gitu, `startup.sh`).

## E. Zbývá

1. Zvážit rotaci klíče storage účtu `toiletlabelsstorage` (po rotaci aktualizovat secret
   `AZURE_STORAGE_CONNECTION_STRING` a znovu nasadit).
