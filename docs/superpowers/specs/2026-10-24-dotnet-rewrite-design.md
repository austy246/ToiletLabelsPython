# Přepis ToiletLabels do .NET 10: návrh

Datum: 2026-10-24
Stav: návrh ke schválení

## Záměr a proč

Aplikaci ToiletLabels (galerie párových cedulí na toalety, Django 5.2) přepíšeme do ASP.NET Core na .NET 10 (LTS). Je to test architektury. Cíle majitele:

- **Menší spotřeba prostředků** na sdíleném VPS i v buildu.
- **Stabilní, předvídatelný build.**
- **Žádné SQLite.** Uživatelé se přesunou do Azure Table Storage, takže aplikace nemá žádný lokální stav a nepotřebuje volume.
- **Lokální testovací prostředí v Dockeru.**
- **Konec GitHub Actions.** CI je pomalé a spotřebovává kredit. Image se postaví na lokálním stroji, pushne a server ho jen stáhne a spustí.

## Cíle

1. Funkční parita se současnou aplikací: veřejná galerie s mapou, nahrávání a úprava párů (jen admin), přihlášení a odhlášení, `/healthz`.
2. Nová aplikace čte stávající produkční data v tabulce `ToiletLabels` a kontejneru `toiletlabels` beze změny a bez migrace.
3. Uživatelé jsou v Table Storage, přihlášení přežije restart i nové nasazení kontejneru.
4. `docker compose up` spustí lokálně kompletní prostředí: aplikaci a vlastní Azurite.
5. Jeden lokální příkaz otestuje, postaví, pushne a nasadí aplikaci na VPS.

## Ne-cíle

- Žádné nové funkce: mazání párů, hlasování, vyhledávání, filtry, stránkování, registrace, obnova hesla. Hlasování se jen zobrazuje, pokud `NumVoters > 0`, jako dnes.
- Žádná změna vzhledu ani textů UI, včetně dnešního mixu angličtiny a češtiny.
- Žádný frontendový build: Tailwind Play CDN, Leaflet, exifr a flagcdn zůstanou z CDN jako dnes.
- Žádná migrace ani úprava existujících entit štítků.
- Žádné CI. GitHub Actions workflow se odstraní.

## Anti-cíle (selhání, i kdyby to technicky fungovalo)

- Nová aplikace přepíše nebo poškodí produkční štítky či obrázky, nebo je kvůli novému formátu přestane umět číst.
- Lokální vývoj nebo testy zapisují do produkčního storage.
- Image nebo běžící kontejner je výrazně těžší než dnešní Python image (python:3.13-slim, limit 512 MB). Cíl je menší image i menší paměť.
- Po každém nasazení nebo restartu je admin odhlášen.
- Nasazení závisí na tom, co je zrovna nainstalované na vývojářském stroji, mimo pinnutý .NET SDK a Docker.
- Kontejnery a volume nejsou poznat podle jména projektu.

## Rozhodnuté (majitelem)

- .NET 10 LTS, ASP.NET Core.
- Uživatelé v Azure Table Storage, žádné SQLite.
- Lokálně Azurite, **vlastní kontejner pro každý projekt** (ne sdílený). Docker Desktop je společný.
- Názvy kontejnerů musí jasně identifikovat projekt.
- HEIC z iPhonu se musí podporovat.
- Image se staví lokálně a nasazuje z lokálního stroje, GitHub Actions odpadá.
- Jediný správce je majitel (předpoklad, kterému majitel neodporoval): jedna role admin, bez správy uživatelů v UI.

## Technické řešení

### Aplikace

- ASP.NET Core 10, **Razor Pages**, jeden webový projekt plus jeden testovací projekt.
- Šablony se převedou z Django šablon 1:1, včetně inline CSS a JS (mapa Leaflet + Mapy.com, location picker, `_file_upload_fix`, flagy zemí). Texty zůstanou stejné. Odkaz „Forgot password?“ a mrtvý blok stránkování se odstraní.
- `countries.py` se přenese doslova: 18 kanonických českých názvů v daném pořadí, ISO kódy, aliasy, fold přes NFKD + odstranění diakritiky + lowercase. Chování `normalize_country` a `country_code` zůstane stejné, včetně vracení neznámé hodnoty oříznuté.
- Obrázky: **Magick.NET** (Q8, x64). Je to jediná rozumná volba, která v Linuxovém kontejneru spolehlivě dekóduje HEIC. ImageSharp HEIC neumí a NetVips prebuilt bez HEVC. Náhled: otočit podle EXIF, převést do RGB/RGBA, zmenšit do 400×400 se zachováním poměru a bez zvětšování, WebP kvalita 80. Selhání náhledu se jako dnes spolkne a jméno náhledu zůstane prázdné.
- EXIF GPS: přednost mají ruční souřadnice, pak EXIF z fotky mužů, pak EXIF z fotky žen, pak serverový geokód přes Nominatim (stejná URL, User-Agent a timeout 10 s). Výsledek se zaokrouhlí na 6 desetinných míst.
- Limit nahrávání 50 MB (Kestrel `MaxRequestBodySize` a `FormOptions`), shodně s Caddy.
- Za Caddy: Forwarded headers (`X-Forwarded-Proto`, `X-Forwarded-For`), důvěřovat proxy v Docker síti, secure cookies.

### Data (Azure Table + Blob)

Jména jsou shodná lokálně i v produkci a všechna nesou předponu projektu:

| Zdroj | Jméno | Poznámka |
|---|---|---|
| Tabulka štítků | `ToiletLabels` | stávající, beze změny schématu |
| Tabulka uživatelů | `ToiletLabelsUsers` | nová |
| Kontejner obrázků | `toiletlabels` | stávající, veřejné čtení blobů |
| Kontejner klíčů | `toiletlabels-keys` | nový, privátní, ASP.NET Data Protection |

**Entita štítku** (PartitionKey `label`, RowKey = UUID s pomlčkami). Vlastnosti `Place`, `Description`, `Country`, `City`, `MenImageUrl`, `WomenImageUrl`, `MenThumbUrl`, `WomenThumbUrl` (jen jména blobů, ne URL), `NumVoters`, `AvgVote`, `Latitude`, `Longitude`, `Created`.

Pravidla kompatibility:

- `Latitude` a `Longitude` jsou v existujících datech buď double, nebo prázdný string `''`, případně číselný string. Čtení musí tolerovat všechny tři varianty. Nový kód zapisuje double, nebo vlastnost vynechá, když souřadnice nejsou.
- `Created` je string ISO 8601 v naivním UTC s mikrosekundami bez `Z`, např. `2026-06-03T12:34:56.123456`. Nové záznamy se zapisují ve stejném formátu, protože se podle něj řadí (sestupně, string compare; chybějící `Created` řadit na konec). Při úpravě se `Created`, `NumVoters` a `AvgVote` zachovají.
- Seznam načte celou partition a seřadí v paměti, jako dnes. Dat je málo.
- *(Rozhodnutí návrhu)* Typy v produkčních datech nejsou ověřené: `NumVoters`/`AvgVote` mohou být Int32, Int64, nebo Double; starší entity nemusí mít `Country`, `City` ani `*ThumbUrl`. Entita se proto čte **tolerantně** přes `TableEntity` (slovník) s převodem typů a výchozími hodnotami, ne přes striktně typovaný model. Při zápisu se zachovají typy, které entita už měla.
- *(Rozhodnutí návrhu)* Aplikace má režim `--check-data`: jen čte, projde všechny entity štítků, každou zkusí převést a vypíše souhrn problémů. Před cutoverem se spustí na VPS proti produkčnímu storage (`docker compose run --rm app --check-data`). Když najde nečitelnou entitu, cutover se zastaví.

**Bloby.** Originál `{id}_men{ext}` / `{id}_women{ext}`, náhled `{id}_men_thumb.webp` / `{id}_women_thumb.webp`. Originál se ukládá nezměněný, nově **s Content-Type** podle detekovaného formátu. Při úpravě s jinou příponou se původní originál smaže, aby nevznikaly osiřelé bloby.

**Veřejná URL obrázků** je samostatné nastavení (`Storage:PublicBlobBaseUrl`), protože lokálně se aplikace připojuje na `http://azurite:10000/...`, ale prohlížeč potřebuje `http://localhost:10100/...`. V produkci `https://toiletlabelsstorage.blob.core.windows.net/toiletlabels/`.

**Uživatel** (PartitionKey `user`, RowKey = uživatelské jméno lowercase). Vlastnosti `UserName`, `PasswordHash` (ASP.NET Core `PasswordHasher<T>` z `Microsoft.Extensions.Identity.Core`, bez celého Identity a EF), `IsAdmin` (bool), `Created`. Při startu: pokud jsou nastavené `Admin__UserName` a `Admin__Password` a uživatel neexistuje, založí se s `IsAdmin = true`. Existující heslo se nepřepisuje, stejně jako dnes. Přihlášení je cookie autentizace. Do upload a edit stránek smí jen `IsAdmin`. Nepřihlášený dostane redirect na `/login?ReturnUrl=…`, přihlášený bez adminu 403.

**Data Protection** (klíče pro auth cookie a antiforgery) se ukládají do blobu v `toiletlabels-keys` (`Azure.Extensions.AspNetCore.DataProtection.Blobs`). Díky tomu přihlášení přežije nové nasazení a aplikace nemá žádný volume.

**Vytváření zdrojů** *(rozhodnutí návrhu)*: aplikace při startu ve všech prostředích idempotentně vytvoří chybějící tabulky `ToiletLabels` a `ToiletLabelsUsers` a kontejnery `toiletlabels` a `toiletlabels-keys`, druhý jako privátní. Stejně tak to dělala Python verze se svou tabulkou. Přístupovou úroveň existujícího kontejneru nikdy nemění. Veřejné čtení blobů nastavuje jen v `Development` a jen u nově vytvořeného kontejneru `toiletlabels`.

### Konfigurace (proměnné prostředí)

| Proměnná | Účel |
|---|---|
| `ConnectionStrings__Storage` | Azure Storage connection string (lokálně Azurite) |
| `Storage__PublicBlobBaseUrl` | základ URL obrázků pro prohlížeč |
| `Mapy__ApiKey` | klíč Mapy.com (mapa i picker v prohlížeči) |
| `Admin__UserName`, `Admin__Password` | admin zakládaný při startu |
| `ASPNETCORE_ENVIRONMENT` | `Development` lokálně, `Production` na VPS |
| `APP_DOMAIN` | doména (`toiletlabels.austy.cz`). Jediný zdroj pro `AllowedHosts` (`AllowedHosts=${APP_DOMAIN}` v `.env`) i pro render Caddy bloku |

Django proměnné (`DJANGO_*`, `CSRF_TRUSTED_ORIGINS`) zaniknou po cutoveru.

### Lokální prostředí

`docker-compose.yml` v kořeni repozitáře, `name: toiletlabels`:

- `app` (kontejner `toiletlabels-app-1`): build z lokálního Dockerfile, port `8080` na hostiteli například `8000`.
- `azurite` (kontejner `toiletlabels-azurite-1`): `mcr.microsoft.com/azure-storage/azurite`, pinnutá verze shodná s FoxLordem (`3.31.0`, nebo novější pinnutá), volume `toiletlabels_azurite-data`. Na hostitele se publikuje blob `10100→10000` (prohlížeč) a table `10102→10002` (Azure Storage Explorer). Porty FoxLordu `10000-10002` zůstanou volné.
- Při startu aplikace v `Development` se vytvoří tabulky a kontejnery, pokud chybí. Kontejner `toiletlabels` dostane veřejný přístup na bloby.
- Admin pro lokál: z `.env` (gitignored), ukázka v `.env.example`.

### Testy

- xUnit. Jednotkové testy pro čistou logiku: normalizace zemí, převod EXIF GPS, výběr souřadnic, sestavení bodů mapy, parsování smíšených `Latitude`/`Longitude`, formát `Created`, náhledy (velikost, bez zvětšování, RGBA PNG, **HEIC vzorek**, EXIF orientace).
- Integrační testy přes `WebApplicationFactory` a **Testcontainers.Azurite**: každý běh si spustí vlastní dočasný Azurite, takže testy nesahají na lokální ani produkční data. Pokrytí: přístupová práva (anonym → login, ne-admin → 403), login a logout, upload (originály, náhledy, Content-Type, souřadnice), edit (zachování `Created`/hlasů, ruční souřadnice vítězí, smazání starého originálu při změně přípony), seznam a mapa, `/healthz`, založení admina při startu, čtení legacy entit s `''` souřadnicemi.
- Testy převzaté z `gallery/tests.py` se přenesou podle chování, ne řádek po řádku.

### Image

- Multi-stage Dockerfile: `mcr.microsoft.com/dotnet/sdk:10.0` pro restore a publish (BuildKit cache mount pro NuGet), runtime `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (bez shellu, ne-root).
- **Ověřit:** Magick.NET native knihovna se v chiseled image načte (glibc, libstdc++), a fold diakritiky (`string.Normalize`) v něm funguje bez ICU. Pokud ne, použít `10.0-noble-chiseled-extra` (má ICU). Pokud ani to nejde, `10.0-noble`. Rozhoduje test v image, ne odhad.
- Workstation GC, ne Server GC (malá paměť, jeden kontejner na sdíleném VPS). `mem_limit` zůstane 512 MB, dokud měření neukáže, že stačí méně.
- Healthcheck bez curl a shellu: aplikace spuštěná s argumentem `--healthcheck` udělá HTTP GET na `http://localhost:8080/healthz` a skončí kódem 0/1. Compose: `["CMD", "dotnet", "ToiletLabels.dll", "--healthcheck"]`.
- Jméno image zůstane `ghcr.io/austy246/toiletlabels-app`, tagy = krátké git SHA + `latest`.

### Nasazení z lokálního stroje

Skript `deploy/deploy.ps1` (PowerShell, Windows):

1. Zkontroluje čistý git strom a odmítne nasadit necommitnuté změny. Tag = `git rev-parse --short HEAD`.
2. Spustí `dotnet test`. Ten běží na hostiteli, protože Testcontainers potřebuje Docker socket, což v `docker build` nejde. SDK pinne `global.json`.
3. `docker buildx build --platform linux/amd64` → `ghcr.io/austy246/toiletlabels-app:<sha>` a `:latest`, `--push`. Jednorázově `docker login ghcr.io` s classic PAT (`write:packages`).
4. Přes SSH (`deploy@57.131.165.173`, klíč `~/.ssh/id_rsa_austy`) nahraje do `/srv/toiletlabels/deploy/` soubory `docker-compose.yml`, `toiletlabels.caddy` a pomocné skripty. *(Rozhodnutí návrhu)* Bere je **z nasazovaného commitu** (`git archive <sha> deploy/`), ne z pracovní kopie. Vzdálená logika se posílá přes SSH stdin přímo z `deploy.ps1`, takže nezávisí na tom, co v `deploy/` daného commitu je. Díky tomu `-Tag <sha>` funguje i pro poslední Python commit: jeho compose má volume, port 8000 a Python healthcheck a Caddy blok má upstream 8000. Vzdálená logika převezme dnešní kroky z workflow:
   - nastaví `APP_IMAGE` v `deploy/.env`,
   - `docker compose pull` a `up -d --wait --remove-orphans`,
   - vyrenderuje Caddy blok do `/srv/infra/sites/toiletlabels.caddy`, `caddy validate`, `caddy reload`, při chybě rollback,
   - prune starých image.
5. Vypíše výsledek a ověří `https://toiletlabels.austy.cz/healthz`.

Další pravidla nasazení:

- Balíček v GHCR musí být **veřejný**, pak server stahuje anonymně a odpadá přihlašování na serveru. Ověřit viditelnost balíčku. Pokud je privátní, přepnout na veřejný. Repozitář je veřejný.
- Tajné hodnoty: `deploy/.env` na serveru zůstává zdrojem pravdy. `deploy.ps1 -UpdateSecrets` volitelně pošle hodnoty z lokálního gitignored `deploy/secrets.env` přes stdin do `update-env-secrets.sh` s allow-listem nových proměnných. Nikdy v argv.
- Návrat k předchozí verzi: `deploy.ps1 -Tag <starší sha>` přeskočí build a nasadí existující tag.
- Caddy blok: upstream se mění na `toiletlabels-app:8080`, jinak beze změny.
- `.github/workflows/deploy-vps.yml` a `startup.sh` se odstraní.

### Přechod v produkci (cutover)

Cutover provádí agent (majitel rozhodl). Postupuje podle těchto kroků a zastaví se pro výslovný souhlas majitele před nasazením do produkce a před smazáním volume.

Kroky:

1. **Majitel jednorázově:** `docker login ghcr.io` s classic PAT (`write:packages`) na svém stroji. Ověří nebo přepne balíček `toiletlabels-app` v GHCR na veřejný. Hesla ani tokeny agent nezadává.
2. **Migrace `.env` na serveru** skriptem `deploy/migrate-env.sh`. Skript přečte stávající hodnoty a zapíše je pod novými jmény, takže je nikdo nemusí vidět ani kopírovat:
   - `AZURE_STORAGE_CONNECTION_STRING` → `ConnectionStrings__Storage`
   - `DJANGO_SUPERUSER_*` → `Admin__*`
   - `MAPY_API_KEY` → `Mapy__ApiKey`
   - první položka `DJANGO_ALLOWED_HOSTS` → `APP_DOMAIN`
   - doplní `Storage__PublicBlobBaseUrl` a `ASPNETCORE_ENVIRONMENT=Production`
   Staré proměnné zůstanou, aby šel rollback na poslední Python commit (`deploy.ps1 -Tag <python sha>`).
3. `docker compose run --rm app --check-data` s novým image proti produkci, jen čtení. **Stop, pokud hlásí problém.**
4. **Stop pro souhlas majitele**, pak nasazení nové verze do stejného compose projektu `toiletlabels`.
5. Ověření: galerie, mapa, přihlášení, restart kontejneru bez odhlášení. Upload a edit testovacího páru jen se souhlasem majitele, protože se objeví ve veřejné galerii; po kontrole se smaže ručně.
6. **Stop pro souhlas majitele**, pak smazání volume `toiletlabels_app_data` a starých proměnných z `.env`.
7. Python kód (`gallery/`, `toiletlabels/`, `manage.py`, `requirements.txt`, `tools/`, starý `Dockerfile`) se z repozitáře odstraní až po úspěšném cutoveru. Do té doby žije vedle nového kódu.
8. Aktualizovat `CLAUDE.md`, `docs/provoz-vps.md` a README.

## Ponecháno na builderovi

- Struktura složek a projektů v rámci jednoho web projektu a jednoho test projektu, jména tříd, rozdělení služeb.
- Knihovna pro čtení EXIF GPS (Magick.NET, nebo MetadataExtractor), pokud čte JPEG i HEIC.
- Detaily validace formulářů a chybových hlášek (musí nahradit dnešní 500 při chybějícím souboru srozumitelnou chybou ve formuláři).
- Port na hostiteli pro lokální aplikaci.
- Jak přesně kopírovat soubory na server (`scp`, nebo `ssh` + `tar` přes stdin).
- Rozdělení implementace na úkoly pro paralelní Sonnet agenty. Úkoly se nesmí překrývat v souborech.
