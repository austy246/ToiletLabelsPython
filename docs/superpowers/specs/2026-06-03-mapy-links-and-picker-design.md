# Mapy.com odkazy + výběr místa ve formulářích

**Datum:** 2026-06-03
**Stav:** Schváleno k implementaci

## Cíl

1. Odkaz „View on map" u karet přepnout z Google Maps na Mapy.com.
2. Do upload i edit formuláře přidat interaktivní mapu Mapy.com pro ruční výběr
   přesného místa (klik / tažení markeru → souřadnice).

## Rozhodnutí

- Ruční výběr na mapě **má přednost** před automatikou (EXIF → geokódování).
  Když uživatel nevybere, zůstává stávající automatika.
- Při editaci se mapa **předvyplní** na stávající souřadnice (marker tažitelný).
- **Bez** vyhledávacího pole — jen klik/tažení.
- Pracuje se rovnou na `main`.

## Komponenty

### 1. Odkaz na Mapy.com (`signpair_list.html`)
- Karta: odkaz „View on map" →
  `https://mapy.com/fnc/v1/showmap?center={lon},{lat}&zoom=17&marker=true`
  (pozor: pořadí **lon,lat**; bez API klíče).

### 2. Partial `_location_picker.html` (nový)
Znovupoužitelný blok vložený do upload i edit formuláře:
- Leaflet (CDN) + dlaždice Mapy.com (`basic`) + povinné logo/atribuce
  (stejně jako mapa na homepage).
- `<div id="location-map">`, **skrytá pole** `name="latitude"` / `name="longitude"`,
  výpis vybraných souřadnic a tlačítko „Vymazat".
- Konfigurace přes `json_script` (`#location-picker-config`): `apiKey`, `lat`, `lon`.
- JS:
  - init mapy; když jsou `lat`/`lon` zadané (edit), umístí **tažitelný marker**
    a vycentruje na něj; jinak start na širokém výřezu (Evropa, zoom ~4).
  - klik na mapu → položí/přesune marker a vyplní skrytá pole;
  - `dragend` markeru → aktualizuje pole;
  - „Vymazat" → odstraní marker a vyprázdní pole.
- Renderuje se jen když je `MAPY_API_KEY` k dispozici.

### 3. View (`views.py`)
- Helper `_manual_coords(request) -> (lat, lon) | None` — z POST polí `latitude`
  / `longitude`; prázdné nebo nevalidní → `None`.
- `upload_label`:
  - GET: do kontextu `MAPY_API_KEY`.
  - POST: `manual = _manual_coords(request)`; když je, použít ho; jinak
    `resolve_coordinates(...)` (EXIF→geokódování) jako dosud.
- `edit_label`:
  - GET: do kontextu `MAPY_API_KEY`, `init_lat`/`init_lon` ze stávající entity.
  - POST: ruční přednost; jinak stávající logika (nová fotka → resolve;
    bez fotky → ponechat stávající; když chybí → geokódovat).

## Testy (`gallery/tests.py`)
- `upload_label` POST s `latitude`/`longitude` → `upsert_label` dostane ručně
  zadané souřadnice; `resolve_coordinates` se nepoužije (přebito).
- `edit_label` POST s `latitude`/`longitude` → uloží ručně zadané.
- Karta renderuje odkaz `mapy.com/fnc/v1/showmap?center={lon},{lat}`.
- upload i edit GET obsahují `id="location-map"` když je `MAPY_API_KEY`.

## Mimo rozsah
- Vyhledávací pole (geocoding) ve formuláři.
- Výběr vrstvy mapy v pickeru (jen `basic`).
- Změna mapy/markerů na homepage (řešeno dříve).
