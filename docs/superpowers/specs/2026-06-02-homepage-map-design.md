# Mapa značek na hlavní stránce (Leaflet + Mapy.com)

**Datum:** 2026-06-02
**Stav:** Schváleno k implementaci

## Cíl

Na hlavní stránku (`signpair_list`) přidat interaktivní mapu zobrazující všechny
značky, které mají souřadnice, jako markery. Mapa je banner nad stávající mřížkou
karet. Navazuje na geolokační funkci ([2026-06-02-geolocation-design.md](2026-06-02-geolocation-design.md)),
díky které má 18 značek vyplněné `Latitude`/`Longitude`.

## Architektura a tok dat

- Mapová knihovna **Leaflet** se načítá z CDN (stejný princip jako Tailwind).
- Dlaždice z **Mapy.com** REST API (rastrové), vrstva `basic`:
  `https://api.mapy.com/v1/maptiles/basic/256/{z}/{x}/{y}?apikey=<MAPY_API_KEY>`
- View `signpair_list` ([gallery/views.py](../../../gallery/views.py)) připraví z `pairs`
  seznam `map_points` — pouze pro značky s platnými (neprázdnými) `Latitude`/`Longitude`.
  Logika filtrování a převod na float zůstává v Pythonu.
- Body se do šablony předají bezpečně přes Django `json_script`; inline JS je jen
  vykreslí. Když nejsou žádné body, mapa se vůbec nevykreslí.

Každý bod (`map_point`) nese:
- `lat` (float), `lon` (float)
- `place` — `Place`, případně `City`, jinak „Untitled"
- `men_url`, `women_url` — kompletní URL náhledů (`AZURE_BLOB_BASE_URL` + filename), nebo prázdné
- `row_key` — pro kotvu na kartu (`id="pair-<RowKey>"`)

## API klíč

- Nová env proměnná **`MAPY_API_KEY`**, kterou view načte z `os.environ`
  a předá do šablony.
- **Lokálně:** v `.env` (gitignorováno — klíč se necommituje). HOTOVO.
- **Produkce:** Azure App Setting `MAPY_API_KEY` (stejný vzor jako
  `AZURE_STORAGE_CONNECTION_STRING`). HOTOVO.
- Dlaždicový klíč je ze své podstaty viditelný v prohlížeči (browser stahuje
  dlaždice přímo z api.mapy.com); ochrana = omezení klíče na doménu v dashboardu
  Mapy.com. GitHub secrets ani změna workflow nejsou potřeba.

## Vzhled mapy, markery, popupy

### Mapa
- `<div id="map">` jako banner nad mřížkou, výška ~400 px (mobil ~300 px),
  zaoblené rohy v duchu stávajících karet.
- Leaflet po inicializaci nastaví výřez `fitBounds` na všechny markery; při
  jediném bodu se vycentruje s pevným zoomem (např. 13).

### Markery a popupy
- Jeden marker na každou značku se souřadnicemi.
- Popup obsahuje:
  - mini náhledy mužské a ženské cedule (pokud URL existují),
  - název místa,
  - odkaz „Zobrazit kartu", který odroluje na odpovídající kartu v mřížce
    (`id="pair-<RowKey>"`) a krátce ji zvýrazní.
- Scroll a zvýraznění řeší malý inline JS (`scrollIntoView` + dočasná CSS třída).

### Atribuce (povinná dle podmínek Mapy.com)
- Logo Mapy.com vlevo dole — Leaflet control s odkazem na https://mapy.com.
- Textový copyright v attribution liště: `© Seznam.cz a.s. a další`.

## Testy a ošetření chyb

- **View test (`gallery/tests.py`):**
  - `signpair_list` vrací v kontextu `map_points` jen pro značky s platnými
    `Latitude`/`Longitude`; značky se souřadnicí `''` se vynechají.
  - tvar bodu (`lat`/`lon` jako float, `place`, `row_key`),
  - kontext obsahuje `MAPY_API_KEY`.
- **Ošetření chyb:**
  - chybí-li `MAPY_API_KEY` nebo je-li `map_points` prázdný, mapa se nevykreslí
    (šablona blok přeskočí), žádný JS error,
  - souřadnice `''` se odfiltrují, číselné se převedou na float; nevalidní hodnota
    bod přeskočí.
- Stávající testy zůstávají zelené.

## Soubory

- `gallery/views.py` — sestavení `map_points` + `MAPY_API_KEY` do kontextu
  `signpair_list`.
- `gallery/templates/gallery/signpair_list.html` — Leaflet CSS/JS z CDN, `#map`,
  `json_script` s body, init skript (mapa, markery, popupy, logo control,
  scroll-to-card), `id="pair-<RowKey>"` na článcích karet.
- `gallery/tests.py` — test kontextu `signpair_list`.

## Mimo rozsah

- Shlukování markerů (clustering) — při 18 bodech není potřeba.
- Přepínání vrstev (outdoor/aerial/winter) — zatím jen `basic`.
- Samostatná detailní stránka značky.
- Filtrování značek podle výřezu mapy.
