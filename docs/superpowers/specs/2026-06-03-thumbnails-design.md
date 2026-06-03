# Miniatury obrázků (WebP, 400 px)

**Datum:** 2026-06-03
**Stav:** Schváleno k implementaci

## Cíl

Hlavní stránka teď stahuje plné originály obrázků pro každou kartu i map popup.
Zavést miniatury, které se použijí pro zobrazení, a výrazně tak snížit objem dat.

## Rozhodnutí

- Generovat **při uploadu** přes Pillow.
- Formát **WebP**, kvalita ~80 %.
- Delší strana max **400 px** (ostré na retina kartách ~360 px i v popupech 60 px).
- **Originály se zachovají** (samostatný blob).
- Existující obrázky doplní **backfill příkaz**.
- Pracuje se rovnou na `main`, bez feature branchí.

## Komponenty

### 1. Generování (`gallery/services/images.py`, nový)
- `make_thumbnail(image_bytes, max_size=400) -> bytes`
  - Pillow: `Image.open`, `thumbnail((max_size, max_size))` (zachová poměr stran),
    uloží jako WebP (quality=80).
  - Režim: RGBA/LA/P se převede tak, aby WebP korektně uložil (P→RGBA, jinak ponechat
    RGB/RGBA). Při chybě vyhodí výjimku (volající ošetří).

### 2. Úložiště
- Miniatura = samostatný blob podle konvence: originál `<id>_men.jpg` →
  `<id>_men_thumb.webp`.
- `AzureBlobManager.upload_image(file, container, blob_name, content_type=None)` —
  volitelný `content_type`; když je zadán, nastaví `ContentSettings(content_type=...)`
  (pro WebP `image/webp`). Stávající volání bez `content_type` se nemění.
- Entita (`upsert_label`) dostane pole `MenThumbUrl` / `WomenThumbUrl` (jména blobů
  miniatur; prázdná, když nejsou).

### 3. Upload / edit flow (`gallery/views.py`)
- `upload_label` a `edit_label`: z nahraných bajtů se vedle originálu vygeneruje
  miniatura (`make_thumbnail`), nahraje se jako `<...>_thumb.webp` s `image/webp`,
  a do entity se uloží `MenThumbUrl`/`WomenThumbUrl`.
- V `edit_label`: nová fotka → nová miniatura; bez nové fotky zůstávají stávající.

### 4. Zobrazení
- **Karty** (`signpair_list.html`): `src` = `{% firstof pair.MenThumbUrl pair.MenImageUrl %}`
  (miniatura, fallback na originál).
- **Map popupy**: `_build_map_points` použije thumb URL s fallbackem na originál.
- Editační stránka (superuser, nízký provoz) může dál používat originál — mimo rozsah.

### 5. Backfill (`gallery/management/commands/backfill_thumbnails.py`, nový)
- `python manage.py backfill_thumbnails [--dry-run]`
- Projde značky bez `MenThumbUrl`/`WomenThumbUrl`, stáhne originály
  (`AzureBlobManager.download_image`), vygeneruje a nahraje WebP miniatury,
  doplní pole v entitě. Vzor jako `backfill_geo`.

## Testy (`gallery/tests.py`)
- `make_thumbnail`: výstup menší než vstup, delší strana ≤ 400 px, validní WebP
  (Pillow ho znovu otevře a `format == 'WEBP'`), zachovaný poměr stran.
- `upload_image` s `content_type` nastaví ContentSettings (mock ověří argument).
- `upsert_label` ukládá `MenThumbUrl`/`WomenThumbUrl`.
- `signpair_list`/`_build_map_points` preferuje thumb, fallback na originál.
- Render karty používá thumb URL.
- `backfill_thumbnails`: doplní jen značky bez miniatur; `--dry-run` neukládá.

## Mimo rozsah
- Lightbox / zobrazení originálu v plné velikosti (originály jen uloženy pro budoucno).
- Miniatury na editační stránce.
- On-the-fly resizing / CDN.
