# Geolokace značek z EXIF s geokódovacím fallbackem

**Datum:** 2026-06-02
**Stav:** Schváleno k implementaci

## Cíl

Ke všem značkám (novým i existujícím) doplnit zeměpisné souřadnice
(`Latitude` / `Longitude`), aby fungoval odkaz „View on map", který už je
v šabloně [signpair_list.html](../../../gallery/templates/gallery/signpair_list.html)
připravený (řádky kolem 212), ale backend souřadnice zatím neukládá.

## Logika získání souřadnic (priorita)

Pro každou značku se souřadnice určí v tomto pořadí; použije se první úspěšný zdroj:

1. EXIF GPS z fotky mužů
2. EXIF GPS z fotky žen
3. Geokódování z volného dotazu sestaveného z `Place` + `City` + `Country`
   (`Place` je většinou název restaurace/podniku, takže má v dotazu přednost
   a dotahuje přesnost na úroveň konkrétního bodu)
4. Prázdné — když nic z výše uvedeného nevrátí výsledek

Tato priorita platí jednotně pro nový upload, editaci i zpětný backfill.

## Klíčové zjištění z průzkumu

- `AzureBlobManager.upload_image` ([azure_blob.py](../../../gallery/services/azure_blob.py))
  nahrává surové bajty souboru bez zpracování přes Pillow → **EXIF data se
  v uložených blobech zachovávají**. Backfill z již nahraných fotek je proto možný.
- Šablona čte `pair.Latitude` a `pair.Longitude` a generuje odkaz na Google Maps
  (`https://maps.google.com/?q=<lat>,<lon>`). Frontend tedy není potřeba měnit.

## Komponenty

### 1. Závislosti (`requirements.txt`)

- **Pillow** — čtení EXIF GPS z obrázků.
- **Geokódování:** Nominatim (OpenStreetMap) volaný přes `urllib` ze standardní
  knihovny — žádná další závislost. Pravidla Nominatim:
  - povinný popisný `User-Agent` (název aplikace + kontaktní e‑mail),
  - limit ~1 dotaz/s → v backfillu se mezi dotazy vloží pauza.
  - *Alternativa (neimplementuje se nyní):* Azure Maps Search API — vyšší limity
    a přesnost, ale vyžaduje zřízený Maps resource a další env proměnnou s klíčem.

### 2. Nový service modul `gallery/services/geo.py`

Veškerá logika na jednom místě, testovatelná nezávisle na Django:

- `extract_gps(image_bytes) -> (lat, lon) | None`
  - Načte EXIF přes Pillow, vyhledá `GPSInfo`, převede DMS (stupně/minuty/vteřiny
    jako racionální čísla) na desetinné stupně a aplikuje reference N/S/E/W
    (S a W → záporné hodnoty). Při jakékoli chybě / chybějících datech vrátí `None`.
- `geocode_place(place, city, country) -> (lat, lon) | None`
  - Sestaví volný dotaz spojením neprázdných polí v pořadí `Place, City, Country`.
  - Když jsou všechna pole prázdná, vrátí `None` bez volání API.
  - Zavolá Nominatim `search` (`format=json`, `limit=1`) s nastaveným `User-Agent`,
    vrátí souřadnice prvního výsledku, jinak `None`. Síťové chyby odchytí → `None`.
- `resolve_coordinates(men_bytes, women_bytes, place, city, country) -> (lat, lon) | None`
  - Orchestruje výše uvedenou prioritu (EXIF muži → EXIF ženy → geokódování).
  - Parametry fotek jsou bajty (nebo `None`), aby modul nezávisel na typu
    Django uploadu a šel použít i z backfillu.

### 3. Úložiště (`gallery/services/azure_table.py`)

`upsert_label` dostane nové parametry `latitude=None, longitude=None` a uloží je
do entity jako `Latitude` a `Longitude`:

- když souřadnice existují → uloží se jako `float`,
- když chybí → uloží se `''` (prázdný řetězec), konzistentně s dosavadním
  přístupem k `Country` / `City`.

### 4. Views (`gallery/views.py`)

**`upload_label`:**
- Po získání souborů se přečtou jejich bajty, zavolá se
  `resolve_coordinates(...)` a výsledek se předá do `upsert_label`.
- **Důležité:** čtení EXIF posouvá ukazatel v souboru. Bajty se přečtou jednou
  (`men_image.read()`), použijí se pro EXIF i pro upload, případně se před
  `upload_image` zavolá `file.seek(0)`. Cílem je neporušit stávající nahrávání blobů.

**`edit_label`:**
- Když je nahrána nová fotka → souřadnice se přepočítají z ní.
- Když nová fotka není → ponechají se stávající uložené souřadnice; pokud jsou
  prázdné a je vyplněno `Place`/`City`/`Country`, zkusí se geokódování.

### 5. Zpětný backfill — management příkaz

`gallery/management/commands/backfill_geo.py` → `python manage.py backfill_geo`

- Projde všechny značky (`list_labels`) a zpracuje ty, které nemají vyplněné
  `Latitude`/`Longitude`.
- Pro každou stáhne bajty blob fotek (přidá se metoda
  `AzureBlobManager.download_image(container, blob_name) -> bytes`) a aplikuje
  `resolve_coordinates` (stejná priorita EXIF → geokódování).
- Uloží výsledek přes `upsert_label`.
- Mezi geokódovacími dotazy vloží pauzu (~1 s) kvůli limitu Nominatim.
- Podporuje `--dry-run` (jen vypíše, co by se změnilo, nic neukládá) a průběžné
  logování.

### 6. Frontend

Beze změny — šablona už souřadnice čte a generuje odkaz na mapu. Odkaz se díky
podmínce `{% if pair.Latitude and pair.Longitude %}` zobrazí jen u značek,
které souřadnice mají.

## Testování

- `gallery/services/geo.py` je čistá logika bez Django/Azure závislostí →
  jednotkové testy:
  - `extract_gps`: obrázek s GPS EXIF → správné desetinné stupně; obrázek bez
    EXIF → `None`; ošetření jižní/západní polokoule (záporné hodnoty).
  - `geocode_place`: prázdná pole → `None` bez volání sítě; sestavení dotazu
    z `Place/City/Country` (volání Nominatim se v testu mockuje).
  - `resolve_coordinates`: ověření priority (EXIF má přednost před geokódováním;
    fallback na ženy → geokódování).

## Mimo rozsah

- Ruční zadávání souřadnic ve formuláři.
- Interaktivní mapa s výběrem bodu.
- Přepnutí na Azure Maps (zůstává jako budoucí alternativa).
