# Geolocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Doplnit ke všem značkám (novým, editovaným i existujícím) souřadnice `Latitude`/`Longitude` získané z EXIF GPS fotek, s fallbackem na geokódování z `Place` + `City` + `Country`.

**Architecture:** Veškerá logika získání souřadnic je v novém čistém modulu `gallery/services/geo.py` (bez Django/Azure závislostí, plně testovatelný). Views a management příkaz `backfill_geo` ho volají. Souřadnice se ukládají do Azure Table entity vedle stávajících polí. Frontend se nemění — šablona souřadnice už čte.

**Tech Stack:** Django 5.2, Pillow (EXIF), Nominatim/OpenStreetMap přes `urllib` (stdlib), Azure Table & Blob Storage.

**Spec:** [docs/superpowers/specs/2026-06-02-geolocation-design.md](../specs/2026-06-02-geolocation-design.md)

**Priorita získání souřadnic (platí všude stejně):**
1. EXIF GPS z fotky mužů → 2. EXIF GPS z fotky žen → 3. geokódování `Place, City, Country` → 4. prázdné.

**Jak spouštět testy:** `python manage.py test gallery -v 2`

---

### Task 1: Přidat Pillow do requirements

**Files:**
- Modify: `requirements.txt`

Pillow je už v lokálním prostředí nainstalovaný, ale chybí v `requirements.txt`, takže by Oryx build při deployi na Azure spadl. Bez testu — jen závislost.

- [ ] **Step 1: Přidat Pillow**

Do `requirements.txt` přidej řádek (zachovej existující řádky beze změny):

```
django>=4.2
azure-storage-blob>=12.19.0
azure-data-tables>=12.6.0
gunicorn>=21.2.0
whitenoise>=6.6.0
Pillow>=10.0.0
```

- [ ] **Step 2: Ověřit instalaci**

Run: `python -c "import PIL; print(PIL.__version__)"`
Expected: vypíše verzi (např. `12.1.0`), bez chyby.

- [ ] **Step 3: Commit**

```bash
git add requirements.txt
git commit -m "Add Pillow dependency for EXIF GPS extraction"
```

---

### Task 2: `geo.extract_gps` — čtení GPS z EXIF

**Files:**
- Create: `gallery/services/geo.py`
- Test: `gallery/tests.py` (přidat novou třídu `GeoExtractGpsTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
from io import BytesIO
from PIL import Image
from gallery.services.geo import extract_gps


def _make_image_with_gps(lat_ref, lat_dms, lon_ref, lon_dms):
    img = Image.new("RGB", (10, 10))
    exif = img.getexif()
    exif[0x8825] = {
        1: lat_ref,
        2: lat_dms,
        3: lon_ref,
        4: lon_dms,
    }
    buf = BytesIO()
    img.save(buf, format="JPEG", exif=exif)
    return buf.getvalue()


class GeoExtractGpsTest(TestCase):
    def test_extracts_decimal_degrees_north_east(self):
        data = _make_image_with_gps("N", (50.0, 5.0, 0.0), "E", (14.0, 25.0, 0.0))
        self.assertEqual(extract_gps(data), (50.083333, 14.416667))

    def test_south_west_are_negative(self):
        data = _make_image_with_gps("S", (33.0, 51.0, 0.0), "W", (151.0, 12.0, 0.0))
        lat, lon = extract_gps(data)
        self.assertLess(lat, 0)
        self.assertLess(lon, 0)

    def test_no_exif_returns_none(self):
        buf = BytesIO()
        Image.new("RGB", (10, 10)).save(buf, format="JPEG")
        self.assertIsNone(extract_gps(buf.getvalue()))

    def test_none_input_returns_none(self):
        self.assertIsNone(extract_gps(None))
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.GeoExtractGpsTest -v 2`
Expected: FAIL / ERROR `ModuleNotFoundError: No module named 'gallery.services.geo'`

- [ ] **Step 3: Vytvořit `gallery/services/geo.py` s `extract_gps`**

```python
"""Resolve geographic coordinates for toilet labels.

Priority: EXIF GPS (men photo) -> EXIF GPS (women photo) -> geocode from
Place/City/Country -> None. Pure module, no Django/Azure dependencies.
"""
from io import BytesIO

from PIL import Image
from PIL.ExifTags import GPSTAGS

# EXIF tag id of the GPS IFD pointer.
_GPS_IFD_TAG = 0x8825


def _to_degrees(value):
    """Convert an EXIF (degrees, minutes, seconds) rational tuple to a float."""
    d, m, s = value
    return float(d) + float(m) / 60.0 + float(s) / 3600.0


def extract_gps(image_bytes):
    """Return (lat, lon) decimal degrees from image EXIF GPS, or None."""
    if not image_bytes:
        return None
    try:
        img = Image.open(BytesIO(image_bytes))
        exif = img.getexif()
        if not exif:
            return None
        gps_ifd = exif.get_ifd(_GPS_IFD_TAG)
        if not gps_ifd:
            return None
        gps = {GPSTAGS.get(key, key): val for key, val in gps_ifd.items()}
        lat = gps.get("GPSLatitude")
        lat_ref = gps.get("GPSLatitudeRef")
        lon = gps.get("GPSLongitude")
        lon_ref = gps.get("GPSLongitudeRef")
        if not (lat and lat_ref and lon and lon_ref):
            return None
        lat_deg = _to_degrees(lat)
        lon_deg = _to_degrees(lon)
        if str(lat_ref).upper().startswith("S"):
            lat_deg = -lat_deg
        if str(lon_ref).upper().startswith("W"):
            lon_deg = -lon_deg
        return (round(lat_deg, 6), round(lon_deg, 6))
    except Exception:
        return None
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.GeoExtractGpsTest -v 2`
Expected: PASS (4 testy OK)

- [ ] **Step 5: Commit**

```bash
git add gallery/services/geo.py gallery/tests.py
git commit -m "Add geo.extract_gps for EXIF GPS extraction"
```

---

### Task 3: `geo.geocode_place` — geokódování přes Nominatim

**Files:**
- Modify: `gallery/services/geo.py`
- Test: `gallery/tests.py` (nová třída `GeoGeocodeTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
import json as _json
from gallery.services.geo import geocode_place


class GeoGeocodeTest(TestCase):
    @patch("gallery.services.geo.urlopen")
    def test_returns_first_result_coords(self, mock_urlopen):
        cm = MagicMock()
        cm.read.return_value = _json.dumps(
            [{"lat": "50.087", "lon": "14.421"}]
        ).encode("utf-8")
        mock_urlopen.return_value.__enter__.return_value = cm
        self.assertEqual(geocode_place("Cafe Louvre", "Prague", "Czechia"),
                         (50.087, 14.421))

    def test_empty_fields_returns_none_without_network(self):
        with patch("gallery.services.geo.urlopen") as mock_urlopen:
            self.assertIsNone(geocode_place("", "", ""))
            mock_urlopen.assert_not_called()

    @patch("gallery.services.geo.urlopen")
    def test_no_results_returns_none(self, mock_urlopen):
        cm = MagicMock()
        cm.read.return_value = b"[]"
        mock_urlopen.return_value.__enter__.return_value = cm
        self.assertIsNone(geocode_place("Nowhere", "", ""))

    @patch("gallery.services.geo.urlopen", side_effect=OSError("network"))
    def test_network_error_returns_none(self, mock_urlopen):
        self.assertIsNone(geocode_place("Cafe", "Prague", "CZ"))
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.GeoGeocodeTest -v 2`
Expected: FAIL `ImportError: cannot import name 'geocode_place'`

- [ ] **Step 3: Doplnit `geocode_place` do `geo.py`**

Na začátek `geo.py` přidej importy (pod stávající importy):

```python
import json
from urllib.parse import urlencode
from urllib.request import Request, urlopen
```

A za `extract_gps` přidej:

```python
_NOMINATIM_URL = "https://nominatim.openstreetmap.org/search"
# Nominatim usage policy requires a descriptive User-Agent with contact info.
_USER_AGENT = "ToiletLabels/1.0 (hausterlitz@gmail.com)"


def geocode_place(place, city, country):
    """Geocode a free-form "Place, City, Country" query via Nominatim.

    Place is usually a venue/restaurant name, so it leads the query to pull
    accuracy down to a specific point of interest. Returns (lat, lon) or None.
    """
    parts = [p.strip() for p in (place, city, country) if p and p.strip()]
    if not parts:
        return None
    query = ", ".join(parts)
    params = urlencode({"q": query, "format": "json", "limit": 1})
    request = Request(
        f"{_NOMINATIM_URL}?{params}", headers={"User-Agent": _USER_AGENT}
    )
    try:
        with urlopen(request, timeout=10) as response:
            data = json.loads(response.read().decode("utf-8"))
        if not data:
            return None
        return (round(float(data[0]["lat"]), 6), round(float(data[0]["lon"]), 6))
    except Exception:
        return None
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.GeoGeocodeTest -v 2`
Expected: PASS (4 testy OK)

- [ ] **Step 5: Commit**

```bash
git add gallery/services/geo.py gallery/tests.py
git commit -m "Add geo.geocode_place using Nominatim"
```

---

### Task 4: `geo.resolve_coordinates` — orchestrace priority

**Files:**
- Modify: `gallery/services/geo.py`
- Test: `gallery/tests.py` (nová třída `GeoResolveTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
from gallery.services.geo import resolve_coordinates


class GeoResolveTest(TestCase):
    @patch("gallery.services.geo.geocode_place")
    @patch("gallery.services.geo.extract_gps")
    def test_prefers_men_exif(self, mock_extract, mock_geocode):
        mock_extract.return_value = (1.0, 2.0)
        result = resolve_coordinates(b"men", b"women", "p", "c", "co")
        self.assertEqual(result, (1.0, 2.0))
        mock_geocode.assert_not_called()
        self.assertEqual(mock_extract.call_count, 1)

    @patch("gallery.services.geo.geocode_place")
    @patch("gallery.services.geo.extract_gps")
    def test_falls_back_to_women_then_geocode(self, mock_extract, mock_geocode):
        mock_extract.side_effect = [None, None]  # men None, women None
        mock_geocode.return_value = (3.0, 4.0)
        result = resolve_coordinates(b"men", b"women", "p", "c", "co")
        self.assertEqual(result, (3.0, 4.0))
        self.assertEqual(mock_extract.call_count, 2)
        mock_geocode.assert_called_once_with("p", "c", "co")

    @patch("gallery.services.geo.geocode_place", return_value=None)
    @patch("gallery.services.geo.extract_gps", return_value=None)
    def test_returns_none_when_nothing_found(self, mock_extract, mock_geocode):
        self.assertIsNone(resolve_coordinates(b"m", b"w", "", "", ""))
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.GeoResolveTest -v 2`
Expected: FAIL `ImportError: cannot import name 'resolve_coordinates'`

- [ ] **Step 3: Doplnit `resolve_coordinates` do `geo.py`**

Na konec `geo.py` přidej:

```python
def resolve_coordinates(men_bytes, women_bytes, place, city, country):
    """Resolve coordinates using the project priority order.

    EXIF(men) -> EXIF(women) -> geocode(Place, City, Country) -> None.
    """
    coords = extract_gps(men_bytes)
    if coords:
        return coords
    coords = extract_gps(women_bytes)
    if coords:
        return coords
    return geocode_place(place, city, country)
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.GeoResolveTest -v 2`
Expected: PASS (3 testy OK)

- [ ] **Step 5: Commit**

```bash
git add gallery/services/geo.py gallery/tests.py
git commit -m "Add geo.resolve_coordinates orchestrating EXIF and geocode priority"
```

---

### Task 5: Uložení souřadnic v `AzureTableManager.upsert_label`

**Files:**
- Modify: `gallery/services/azure_table.py:15-32`
- Test: `gallery/tests.py` (nová třída `UpsertCoordinatesTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
@patch.dict(os.environ, {"AZURE_STORAGE_CONNECTION_STRING": "fake-connection-string"})
class UpsertCoordinatesTest(TestCase):
    @patch("gallery.services.azure_table.TableServiceClient")
    def test_stores_float_coordinates(self, mock_tsc):
        mock_table_client = MagicMock()
        mock_tsc.from_connection_string.return_value.get_table_client.return_value = (
            mock_table_client
        )
        manager = AzureTableManager()
        manager.upsert_label("id1", "P", "D", "m.jpg", "w.jpg", 0, 0,
                             latitude=50.1, longitude=14.2)
        entity = mock_table_client.upsert_entity.call_args.kwargs["entity"]
        self.assertEqual(entity["Latitude"], 50.1)
        self.assertEqual(entity["Longitude"], 14.2)

    @patch("gallery.services.azure_table.TableServiceClient")
    def test_empty_coordinates_stored_as_blank(self, mock_tsc):
        mock_table_client = MagicMock()
        mock_tsc.from_connection_string.return_value.get_table_client.return_value = (
            mock_table_client
        )
        manager = AzureTableManager()
        manager.upsert_label("id1", "P", "D", "m.jpg", "w.jpg", 0, 0)
        entity = mock_table_client.upsert_entity.call_args.kwargs["entity"]
        self.assertEqual(entity["Latitude"], "")
        self.assertEqual(entity["Longitude"], "")
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.UpsertCoordinatesTest -v 2`
Expected: FAIL `TypeError: upsert_label() got an unexpected keyword argument 'latitude'`

- [ ] **Step 3: Upravit `upsert_label`**

V `gallery/services/azure_table.py` nahraď celou metodu `upsert_label` (řádky 15-32) tímto:

```python
    def upsert_label(self, label_id, place, description, men_image_url, women_image_url, num_voters, avg_vote, country=None, city=None, created=None, latitude=None, longitude=None):
        import datetime
        if created is None:
            created = datetime.datetime.utcnow().isoformat()
        entity = {
            'PartitionKey': 'label',
            'RowKey': str(label_id),
            'Place': place,
            'Description': description,
            'MenImageUrl': men_image_url,
            'WomenImageUrl': women_image_url,
            'NumVoters': num_voters,
            'AvgVote': avg_vote,
            'Country': country if country is not None else '',
            'City': city if city is not None else '',
            'Latitude': latitude if latitude is not None else '',
            'Longitude': longitude if longitude is not None else '',
            'Created': created,
        }
        self.table_client.upsert_entity(entity=entity)
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.UpsertCoordinatesTest -v 2`
Expected: PASS (2 testy OK)

- [ ] **Step 5: Commit**

```bash
git add gallery/services/azure_table.py gallery/tests.py
git commit -m "Store Latitude/Longitude on label entities"
```

---

### Task 6: `AzureBlobManager.download_image` — stažení bajtů blobu

**Files:**
- Modify: `gallery/services/azure_blob.py` (přidat metodu)
- Test: `gallery/tests.py` (rozšířit `AzureBlobManagerTest`)

Potřebné pro backfill (Task 8), který čte EXIF z už nahraných fotek.

- [ ] **Step 1: Napsat padající test**

Do existující třídy `AzureBlobManagerTest` v `gallery/tests.py` přidej metodu:

```python
    @patch('gallery.services.azure_blob.BlobServiceClient')
    def test_download_image(self, mock_blob_service_client):
        mock_blob_client = MagicMock()
        mock_container_client = MagicMock()
        mock_container_client.get_blob_client.return_value = mock_blob_client
        mock_blob_service_client.from_connection_string.return_value.get_container_client.return_value = mock_container_client
        mock_blob_client.download_blob.return_value.readall.return_value = b'imgdata'
        manager = AzureBlobManager('fake-conn-string')
        data = manager.download_image('container', 'blob.jpg')
        self.assertEqual(data, b'imgdata')
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.AzureBlobManagerTest.test_download_image -v 2`
Expected: FAIL `AttributeError: 'AzureBlobManager' object has no attribute 'download_image'`

- [ ] **Step 3: Přidat `download_image` do `azure_blob.py`**

Na konec třídy `AzureBlobManager` (za `upload_image`) přidej:

```python
    def download_image(self, container_name, blob_name):
        container_client = self.blob_service_client.get_container_client(container_name)
        blob_client = container_client.get_blob_client(blob_name)
        return blob_client.download_blob().readall()
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.AzureBlobManagerTest.test_download_image -v 2`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add gallery/services/azure_blob.py gallery/tests.py
git commit -m "Add AzureBlobManager.download_image for backfill"
```

---

### Task 7: Napojit views (`upload_label`, `edit_label`)

**Files:**
- Modify: `gallery/views.py`
- Test: `gallery/tests.py` (nová třída `UploadLabelViewTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
from django.core.files.uploadedfile import SimpleUploadedFile


class UploadLabelViewTest(TestCase):
    def setUp(self):
        from django.contrib.auth.models import User
        self.user = User.objects.create_superuser("admin", "a@b.c", "pw")
        self.client.force_login(self.user)

    @patch("gallery.views.resolve_coordinates")
    @patch("gallery.views.AzureBlobManager")
    @patch("gallery.views.AzureTableManager")
    def test_upload_passes_resolved_coordinates(self, mock_table_cls,
                                                mock_blob_cls, mock_resolve):
        mock_table = MagicMock()
        mock_table_cls.return_value = mock_table
        mock_blob_cls.return_value = MagicMock()
        mock_resolve.return_value = (50.1, 14.2)
        men = SimpleUploadedFile("m.jpg", b"menbytes", content_type="image/jpeg")
        women = SimpleUploadedFile("w.jpg", b"womenbytes", content_type="image/jpeg")
        resp = self.client.post("/upload/", {
            "place": "Cafe", "description": "d", "country": "CZ",
            "city": "Prague", "men_image": men, "women_image": women,
        })
        self.assertEqual(resp.status_code, 302)
        kwargs = mock_table.upsert_label.call_args.kwargs
        self.assertEqual(kwargs["latitude"], 50.1)
        self.assertEqual(kwargs["longitude"], 14.2)
        mock_resolve.assert_called_once()
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.UploadLabelViewTest -v 2`
Expected: FAIL — `resolve_coordinates` se ve `views` ještě neimportuje / `latitude` se nepředává (AttributeError nebo KeyError na `kwargs["latitude"]`).

- [ ] **Step 3: Upravit `upload_label`**

V `gallery/views.py` přidej na začátek (k ostatním importům) řádek:

```python
from .services.geo import resolve_coordinates, geocode_place
```

Pak nahraď tělo `if request.method == 'POST':` ve funkci `upload_label` (řádky 23-51) tímto:

```python
    if request.method == 'POST':
        place = request.POST.get('place', '')
        description = request.POST.get('description', '')
        country = request.POST.get('country', '')
        city = request.POST.get('city', '')
        men_image = request.FILES['men_image']
        women_image = request.FILES['women_image']
        # Read bytes once: used for both EXIF extraction and blob upload.
        men_bytes = men_image.read()
        women_bytes = women_image.read()
        label_id = str(uuid.uuid4())
        # Resolve coordinates: EXIF(men) -> EXIF(women) -> geocode.
        coords = resolve_coordinates(men_bytes, women_bytes, place, city, country)
        latitude, longitude = coords if coords else (None, None)
        # Upload images to Azure Blob Storage
        import os
        men_ext = os.path.splitext(men_image.name)[1]
        women_ext = os.path.splitext(women_image.name)[1]
        men_filename = f"{label_id}_men{men_ext}"
        women_filename = f"{label_id}_women{women_ext}"
        blob_manager.upload_image(men_bytes, 'toiletlabels', men_filename)
        blob_manager.upload_image(women_bytes, 'toiletlabels', women_filename)
        # Store only the filenames in Azure Table
        table_manager.upsert_label(
            label_id=label_id,
            place=place,
            description=description,
            men_image_url=men_filename,
            women_image_url=women_filename,
            num_voters=0,
            avg_vote=0,
            country=country,
            city=city,
            latitude=latitude,
            longitude=longitude,
        )
        return redirect(reverse('gallery:signpair_list'))
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.UploadLabelViewTest -v 2`
Expected: PASS

- [ ] **Step 5: Upravit `edit_label`**

V `gallery/views.py` nahraď tělo `if request.method == 'POST':` ve funkci `edit_label` (řádky 65-97) tímto:

```python
    if request.method == 'POST':
        place = request.POST.get('place', '')
        description = request.POST.get('description', '')
        country = request.POST.get('country', '')
        city = request.POST.get('city', '')
        men_image = request.FILES.get('men_image')
        women_image = request.FILES.get('women_image')
        men_filename = pair.get('MenImageUrl', '')
        women_filename = pair.get('WomenImageUrl', '')
        men_bytes = None
        women_bytes = None
        # Handle men image upload if provided
        if men_image:
            import os
            men_bytes = men_image.read()
            men_ext = os.path.splitext(men_image.name)[1]
            men_filename = f"{pk}_men{men_ext}"
            blob_manager.upload_image(men_bytes, 'toiletlabels', men_filename)
        # Handle women image upload if provided
        if women_image:
            import os
            women_bytes = women_image.read()
            women_ext = os.path.splitext(women_image.name)[1]
            women_filename = f"{pk}_women{women_ext}"
            blob_manager.upload_image(women_bytes, 'toiletlabels', women_filename)
        # Resolve coordinates: recompute from new photo(s) if any were uploaded;
        # otherwise keep existing coords, falling back to geocode when missing.
        if men_bytes or women_bytes:
            coords = resolve_coordinates(men_bytes, women_bytes, place, city, country)
            latitude, longitude = coords if coords else (None, None)
        else:
            existing_lat = pair.get('Latitude', '')
            existing_lon = pair.get('Longitude', '')
            if existing_lat != '' and existing_lon != '':
                latitude, longitude = existing_lat, existing_lon
            else:
                coords = geocode_place(place, city, country)
                latitude, longitude = coords if coords else (None, None)
        table_manager.upsert_label(
            label_id=pk,
            place=place,
            description=description,
            men_image_url=men_filename,
            women_image_url=women_filename,
            num_voters=pair.get('NumVoters', 0),
            avg_vote=pair.get('AvgVote', 0),
            country=country,
            city=city,
            created=pair.get('Created'),
            latitude=latitude,
            longitude=longitude,
        )
        return redirect(reverse('gallery:signpair_list'))
```

Pozn.: `created=pair.get('Created')` zachová původní datum vzniku (jinak by se `edit` přepsal na „nyní").

- [ ] **Step 6: Spustit celou sadu testů**

Run: `python manage.py test gallery -v 2`
Expected: PASS — všechny dosavadní testy včetně `UploadLabelViewTest`.

- [ ] **Step 7: Commit**

```bash
git add gallery/views.py gallery/tests.py
git commit -m "Wire coordinate resolution into upload and edit views"
```

---

### Task 8: Management příkaz `backfill_geo`

**Files:**
- Create: `gallery/management/__init__.py`
- Create: `gallery/management/commands/__init__.py`
- Create: `gallery/management/commands/backfill_geo.py`
- Test: `gallery/tests.py` (nová třída `BackfillGeoCommandTest`)

- [ ] **Step 1: Vytvořit prázdné `__init__.py` soubory**

Vytvoř (prázdný obsah):
- `gallery/management/__init__.py`
- `gallery/management/commands/__init__.py`

- [ ] **Step 2: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
from django.core.management import call_command
from io import StringIO as _StringIO


@patch.dict(os.environ, {"AZURE_STORAGE_CONNECTION_STRING": "fake-connection-string"})
class BackfillGeoCommandTest(TestCase):
    @patch("gallery.management.commands.backfill_geo.geocode_place")
    @patch("gallery.management.commands.backfill_geo.extract_gps")
    @patch("gallery.management.commands.backfill_geo.AzureBlobManager")
    @patch("gallery.management.commands.backfill_geo.AzureTableManager")
    def test_updates_only_labels_missing_coords(self, mock_table_cls, mock_blob_cls,
                                                mock_extract, mock_geocode):
        mock_table = MagicMock()
        mock_table_cls.return_value = mock_table
        mock_blob = MagicMock()
        mock_blob_cls.return_value = mock_blob
        mock_blob.download_image.return_value = b"img"
        mock_table.list_labels.return_value = [
            {"RowKey": "id1", "Latitude": "", "Longitude": "",
             "MenImageUrl": "m.jpg", "WomenImageUrl": "w.jpg",
             "Place": "Cafe", "City": "Prague", "Country": "CZ",
             "Description": "d", "NumVoters": 0, "AvgVote": 0, "Created": "2024"},
            {"RowKey": "id2", "Latitude": 50.0, "Longitude": 14.0,
             "MenImageUrl": "m2.jpg", "WomenImageUrl": "w2.jpg"},
        ]
        mock_extract.return_value = (1.0, 2.0)
        call_command("backfill_geo", stdout=_StringIO())
        self.assertEqual(mock_table.upsert_label.call_count, 1)
        kwargs = mock_table.upsert_label.call_args.kwargs
        self.assertEqual(kwargs["label_id"], "id1")
        self.assertEqual(kwargs["latitude"], 1.0)
        self.assertEqual(kwargs["longitude"], 2.0)
        self.assertEqual(kwargs["created"], "2024")

    @patch("gallery.management.commands.backfill_geo.geocode_place")
    @patch("gallery.management.commands.backfill_geo.extract_gps")
    @patch("gallery.management.commands.backfill_geo.AzureBlobManager")
    @patch("gallery.management.commands.backfill_geo.AzureTableManager")
    def test_dry_run_does_not_save(self, mock_table_cls, mock_blob_cls,
                                   mock_extract, mock_geocode):
        mock_table = MagicMock()
        mock_table_cls.return_value = mock_table
        mock_blob_cls.return_value = MagicMock()
        mock_table.list_labels.return_value = [
            {"RowKey": "id1", "Latitude": "", "Longitude": "",
             "MenImageUrl": "m.jpg", "WomenImageUrl": "w.jpg",
             "Place": "Cafe", "City": "Prague", "Country": "CZ"},
        ]
        mock_extract.return_value = (1.0, 2.0)
        call_command("backfill_geo", "--dry-run", stdout=_StringIO())
        mock_table.upsert_label.assert_not_called()
```

- [ ] **Step 3: Spustit test — musí padat**

Run: `python manage.py test gallery.tests.BackfillGeoCommandTest -v 2`
Expected: FAIL `CommandError: Unknown command: 'backfill_geo'`

- [ ] **Step 4: Vytvořit `gallery/management/commands/backfill_geo.py`**

```python
"""Backfill Latitude/Longitude for labels that have no coordinates yet.

Applies the same priority as uploads: EXIF GPS from the stored blob photos,
falling back to geocoding from Place/City/Country. Run with --dry-run to
preview without saving.
"""
import time

from django.core.management.base import BaseCommand

from gallery.services.azure_table import AzureTableManager
from gallery.services.azure_blob import AzureBlobManager
from gallery.services.geo import extract_gps, geocode_place

_CONTAINER = "toiletlabels"
# Nominatim usage policy: at most ~1 request/second.
_GEOCODE_DELAY_SECONDS = 1


class Command(BaseCommand):
    help = "Backfill Latitude/Longitude for labels missing coordinates."

    def add_arguments(self, parser):
        parser.add_argument(
            "--dry-run",
            action="store_true",
            help="Show what would change without saving.",
        )

    def handle(self, *args, **options):
        dry_run = options["dry_run"]
        table = AzureTableManager()
        blob = AzureBlobManager()
        updated = 0
        for label in table.list_labels():
            row_key = label.get("RowKey")
            lat = label.get("Latitude", "")
            lon = label.get("Longitude", "")
            if lat != "" and lon != "":
                continue
            men_bytes = self._download(blob, label.get("MenImageUrl", ""))
            women_bytes = self._download(blob, label.get("WomenImageUrl", ""))
            coords = extract_gps(men_bytes) or extract_gps(women_bytes)
            source = "EXIF"
            if not coords:
                coords = geocode_place(
                    label.get("Place", ""),
                    label.get("City", ""),
                    label.get("Country", ""),
                )
                source = "geocode"
                time.sleep(_GEOCODE_DELAY_SECONDS)
            if not coords:
                self.stdout.write(f"  {row_key}: no coordinates found")
                continue
            self.stdout.write(f"  {row_key}: {coords} (via {source})")
            if not dry_run:
                table.upsert_label(
                    label_id=row_key,
                    place=label.get("Place", ""),
                    description=label.get("Description", ""),
                    men_image_url=label.get("MenImageUrl", ""),
                    women_image_url=label.get("WomenImageUrl", ""),
                    num_voters=label.get("NumVoters", 0),
                    avg_vote=label.get("AvgVote", 0),
                    country=label.get("Country", ""),
                    city=label.get("City", ""),
                    created=label.get("Created"),
                    latitude=coords[0],
                    longitude=coords[1],
                )
                updated += 1
        suffix = " (dry-run, nothing saved)" if dry_run else ""
        self.stdout.write(self.style.SUCCESS(f"Done. {updated} label(s) updated.{suffix}"))

    def _download(self, blob, blob_name):
        if not blob_name:
            return None
        try:
            return blob.download_image(_CONTAINER, blob_name)
        except Exception:
            return None
```

- [ ] **Step 5: Spustit test — musí projít**

Run: `python manage.py test gallery.tests.BackfillGeoCommandTest -v 2`
Expected: PASS (2 testy OK)

- [ ] **Step 6: Commit**

```bash
git add gallery/management/ gallery/tests.py
git commit -m "Add backfill_geo management command"
```

---

### Task 9: Závěrečné ověření celé sady + manuální kontrola

**Files:** žádné změny (jen ověření)

- [ ] **Step 1: Spustit kompletní testy**

Run: `python manage.py test gallery -v 2`
Expected: PASS — všechny testy bez chyb.

- [ ] **Step 2: Manuálně ověřit upload (vyžaduje `AZURE_STORAGE_CONNECTION_STRING`)**

Run: `python manage.py runserver`
Postup: přihlas se jako superuser → `/upload/` → nahraj pár fotek s GPS v EXIF → ověř, že se na výpisu (`/`) u karty objeví odkaz „View on map" mířící na správné místo. Pak zkus fotku bez GPS s vyplněným Place/City/Country a ověř, že se souřadnice dohledaly geokódováním.

- [ ] **Step 3: (Volitelné) Náhled backfillu existujících značek**

Run: `python manage.py backfill_geo --dry-run`
Expected: výpis značek bez souřadnic a navržených souřadnic, nic se neuloží. Po kontrole spusť bez `--dry-run` pro reálné doplnění.

---

## Poznámky k nasazení

- `backfill_geo` se spouští ručně (jednorázově po nasazení), není součástí [startup.sh](../../../startup.sh).
- Nominatim má limit ~1 dotaz/s a vyžaduje kontakt v `User-Agent` — případná masivní dávka by měla běžet šetrně (příkaz už pauzu obsahuje).
