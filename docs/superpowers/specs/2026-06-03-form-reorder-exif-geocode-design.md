# Přeskládání formuláře + auto-EXIF + tlačítko Vyhledat

**Datum:** 2026-06-03
**Stav:** Schváleno k implementaci

## Cíl

Zlepšit flow vytváření/editace značky: nejdřív fotky, pak zbytek. Při výběru
fotky přečíst GPS z EXIF v prohlížeči a automaticky umístit bod na mapu; pro
fotky bez GPS přidat tlačítko „Vyhledej na mapě" (geokódování přes Mapy.com).

## Pořadí ve formuláři (upload + edit), levý sloupec
1. Men's Image, Women's Image
2. Place, Description, Country, City
3. tlačítko „Vyhledej na mapě" (pod City) + místo pro hlášku
Pravý sloupec: mapa pickeru (beze změny rozložení).

## Klient-side chování (v `_location_picker.html`)
- Načíst `exifr` z CDN (`https://cdn.jsdelivr.net/npm/exifr/dist/full.umd.js`).
- Po `change` na `#men_image` / `#women_image`: `exifr.gps(file)` → když vrátí
  `latitude`/`longitude`, položit marker (`setPoint`) a vycentrovat. Poslední
  akce vyhrává (jiná fotka s GPS / klik / tažení přepíše).
- Tlačítko `#geocode-search`: poskládá dotaz z `#place` + `#city` + `#country`
  (neprázdné), zavolá `https://api.mapy.com/v1/geocode?lang=cs&limit=1&apikey=<key>&query=<q>`,
  z `items[0].position.lat/lon` položí marker + vycentruje.
  - prázdná pole → hláška „Vyplň místo, město nebo zemi.";
  - nenalezeno → „Místo nenalezeno."; chyba sítě → „Chyba vyhledávání.".
- Posluchače navěsí picker partial; prvky čte podle id (graceful, když chybí).

## Server
Beze změny. Klient nastaví skrytá `latitude`/`longitude` → server je bere jako
ruční (priorita). Serverový EXIF→geokódování (Nominatim) zůstává jako fallback,
když JS nic nenastaví.

## Testy
- upload GET: pole `men_image` je v HTML před polem `place` (přeskládáno);
  `id="geocode-search"` přítomné; načítá se `exifr`.

## Mimo rozsah
- Změna serverové logiky priority.
- Náhrada serverového Nominatim geokódování za Mapy.com.
