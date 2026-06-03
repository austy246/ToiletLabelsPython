# Homepage Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Přidat na hlavní stránku interaktivní mapu (banner nad mřížkou karet) zobrazující všechny značky se souřadnicemi jako markery s popupy.

**Architecture:** View `signpair_list` připraví `map_points` (jen značky s platnými souřadnicemi) a `MAPY_API_KEY`, předá je do šablony. Šablona vykreslí Leaflet mapu s dlaždicemi z Mapy.com; data se předají přes Django `json_script`, inline JS je jen vykreslí. Když nejsou body nebo chybí klíč, mapa se nevykreslí.

**Tech Stack:** Django, Leaflet 1.9.4 (CDN), Mapy.com raster tiles (REST API, env `MAPY_API_KEY`), Tailwind (CDN, stávající).

**Spec:** [docs/superpowers/specs/2026-06-02-homepage-map-design.md](../specs/2026-06-02-homepage-map-design.md)

**Jak spouštět testy:** `.venv/Scripts/python.exe manage.py test gallery -v 2`

**Mapy.com – ověřené detaily (oficiální getting-started):**
- Tile URL: `https://api.mapy.com/v1/maptiles/basic/256/{z}/{x}/{y}?apikey=<KEY>`
- Logo (povinné): `https://api.mapy.com/img/api/logo.svg`, odkaz na `https://mapy.com/`
- Copyright (attribution): odkaz na `https://api.mapy.com/copyright`, text `© Seznam.cz a.s. a další`

---

### Task 1: View — sestavení `map_points` a `MAPY_API_KEY`

**Files:**
- Modify: `gallery/views.py` (přidat `import os` nahoře, helper `_build_map_points`, upravit `signpair_list`)
- Test: `gallery/tests.py` (nová třída `SignpairListMapContextTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
class SignpairListMapContextTest(TestCase):
    @patch("gallery.views.AzureBlobManager")
    @patch("gallery.views.AzureTableManager")
    def test_builds_map_points_only_for_valid_coords(self, mock_table_cls, mock_blob_cls):
        mock_table = MagicMock()
        mock_table_cls.return_value = mock_table
        mock_blob_cls.get_blob_base_url.return_value = "https://blob/"
        mock_table.list_labels.return_value = [
            {"RowKey": "id1", "Latitude": 50.1, "Longitude": 14.2,
             "Place": "Cafe", "City": "Prague",
             "MenImageUrl": "m.jpg", "WomenImageUrl": "w.jpg"},
            {"RowKey": "id2", "Latitude": "", "Longitude": "", "Place": "NoGeo"},
        ]
        with patch.dict(os.environ, {"MAPY_API_KEY": "testkey"}):
            resp = self.client.get("/")
        points = resp.context["map_points"]
        self.assertEqual(len(points), 1)
        self.assertEqual(points[0]["row_key"], "id1")
        self.assertEqual(points[0]["lat"], 50.1)
        self.assertEqual(points[0]["lon"], 14.2)
        self.assertEqual(points[0]["place"], "Cafe")
        self.assertEqual(points[0]["men_url"], "https://blob/m.jpg")
        self.assertEqual(points[0]["women_url"], "https://blob/w.jpg")
        self.assertEqual(resp.context["MAPY_API_KEY"], "testkey")

    @patch("gallery.views.AzureBlobManager")
    @patch("gallery.views.AzureTableManager")
    def test_place_falls_back_to_city(self, mock_table_cls, mock_blob_cls):
        mock_table = MagicMock()
        mock_table_cls.return_value = mock_table
        mock_blob_cls.get_blob_base_url.return_value = "https://blob/"
        mock_table.list_labels.return_value = [
            {"RowKey": "id3", "Latitude": 1.0, "Longitude": 2.0,
             "Place": "", "City": "Berlin", "MenImageUrl": "", "WomenImageUrl": ""},
        ]
        with patch.dict(os.environ, {"MAPY_API_KEY": "k"}):
            resp = self.client.get("/")
        points = resp.context["map_points"]
        self.assertEqual(points[0]["place"], "Berlin")
        self.assertEqual(points[0]["men_url"], "")
```

- [ ] **Step 2: Spustit test — musí padat**

Run: `.venv/Scripts/python.exe manage.py test gallery.tests.SignpairListMapContextTest -v 2`
Expected: FAIL — `KeyError: 'map_points'` (kontext zatím tento klíč nemá).

- [ ] **Step 3: Upravit `gallery/views.py`**

Nahoře u importů (za `import uuid`) přidej:

```python
import os
```

Před funkci `signpair_list` přidej helper:

```python
def _build_map_points(pairs, base_url):
    """Build map marker data for labels that have valid coordinates."""
    points = []
    for pair in pairs:
        lat = pair.get('Latitude', '')
        lon = pair.get('Longitude', '')
        if lat is None or lon is None or lat == '' or lon == '':
            continue
        try:
            lat_f = float(lat)
            lon_f = float(lon)
        except (TypeError, ValueError):
            continue
        men = pair.get('MenImageUrl', '')
        women = pair.get('WomenImageUrl', '')
        points.append({
            'lat': lat_f,
            'lon': lon_f,
            'place': pair.get('Place') or pair.get('City') or 'Untitled',
            'men_url': (base_url + men) if men else '',
            'women_url': (base_url + women) if women else '',
            'row_key': pair.get('RowKey', ''),
        })
    return points
```

Nahraď funkci `signpair_list` tímto:

```python
def signpair_list(request):
    table_manager = AzureTableManager()
    pairs = table_manager.list_labels()
    base_url = AzureBlobManager.get_blob_base_url()
    return render(request, 'gallery/signpair_list.html', {
        'pairs': pairs,
        'AZURE_BLOB_BASE_URL': base_url,
        'map_points': _build_map_points(pairs, base_url),
        'MAPY_API_KEY': os.environ.get('MAPY_API_KEY', ''),
    })
```

- [ ] **Step 4: Spustit test — musí projít**

Run: `.venv/Scripts/python.exe manage.py test gallery.tests.SignpairListMapContextTest -v 2`
Expected: PASS (2 testy OK)

- [ ] **Step 5: Commit**

```bash
git add gallery/views.py gallery/tests.py
git commit -m "Build map_points context for homepage map"
```

---

### Task 2: Šablona — Leaflet mapa, markery, popupy, kotvy na karty

**Files:**
- Modify: `gallery/templates/gallery/signpair_list.html`
- Test: `gallery/tests.py` (nová třída `SignpairListMapRenderTest`)

- [ ] **Step 1: Napsat padající test**

Přidej na konec `gallery/tests.py`:

```python
class SignpairListMapRenderTest(TestCase):
    @patch("gallery.views.AzureBlobManager")
    @patch("gallery.views.AzureTableManager")
    def _get(self, labels, key, mock_table_cls, mock_blob_cls):
        mock_table = MagicMock()
        mock_table_cls.return_value = mock_table
        mock_blob_cls.get_blob_base_url.return_value = "https://blob/"
        mock_table.list_labels.return_value = labels
        with patch.dict(os.environ, {"MAPY_API_KEY": key}):
            return self.client.get("/")

    def test_map_rendered_when_points_and_key(self):
        labels = [{"RowKey": "id1", "Latitude": 50.1, "Longitude": 14.2,
                   "Place": "Cafe", "City": "", "MenImageUrl": "", "WomenImageUrl": ""}]
        resp = self._get(labels, "testkey")
        html = resp.content.decode()
        self.assertIn('id="map"', html)
        self.assertIn("map-points-data", html)
        self.assertIn("api.mapy.com/v1/maptiles/basic", html)
        self.assertIn('id="pair-id1"', html)

    def test_map_hidden_when_no_key(self):
        labels = [{"RowKey": "id1", "Latitude": 50.1, "Longitude": 14.2,
                   "Place": "Cafe", "City": "", "MenImageUrl": "", "WomenImageUrl": ""}]
        resp = self._get(labels, "")
        self.assertNotIn('id="map"', resp.content.decode())
```

Pozn.: dekorátory `@patch` na pomocné metodě `_get` injektují mocky jako poslední dva poziční argumenty — proto je signatura `(self, labels, key, mock_table_cls, mock_blob_cls)`.

- [ ] **Step 2: Spustit test — musí padat**

Run: `.venv/Scripts/python.exe manage.py test gallery.tests.SignpairListMapRenderTest -v 2`
Expected: FAIL — `id="map"` se v HTML nenachází (šablona ho ještě nemá).

- [ ] **Step 3: Přidat `id` a highlight třídu na karty**

V `gallery/templates/gallery/signpair_list.html` nahraď řádek

```html
    <article class="card flex flex-col">
```

za

```html
    <article id="pair-{{ pair.RowKey }}" class="card flex flex-col">
```

Do `<style>` bloku (za pravidlo `.meta-tag { ... }`) přidej:

```css
  .card-highlight {
    outline: 3px solid #3b82f6;
    outline-offset: 2px;
    transition: outline-color 0.3s ease;
  }
```

- [ ] **Step 4: Přidat mapu na začátek `<main>`**

V témže souboru najdi otevírací tag `<main ...>` a hned za něj (před `{% if pairs %}`) vlož:

```html
  {% if map_points and MAPY_API_KEY %}
  <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
  <section class="mb-6">
    <div id="map" class="w-full rounded-2xl overflow-hidden shadow-sm" style="height:400px"></div>
  </section>
  {{ map_points|json_script:"map-points-data" }}
  <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
  <script>
  (function () {
    var points = JSON.parse(document.getElementById('map-points-data').textContent);
    if (!points.length) return;
    var apiKey = "{{ MAPY_API_KEY|escapejs }}";
    var map = L.map('map');
    L.tileLayer('https://api.mapy.com/v1/maptiles/basic/256/{z}/{x}/{y}?apikey=' + apiKey, {
      minZoom: 0,
      maxZoom: 19,
      attribution: '<a href="https://api.mapy.com/copyright" target="_blank" rel="noopener">&copy; Seznam.cz a.s. a další</a>',
    }).addTo(map);

    // Required Mapy.com logo control.
    var LogoControl = L.Control.extend({
      options: { position: 'bottomleft' },
      onAdd: function () {
        var container = L.DomUtil.create('div');
        var link = L.DomUtil.create('a', '', container);
        link.setAttribute('href', 'https://mapy.com/');
        link.setAttribute('target', '_blank');
        link.setAttribute('rel', 'noopener');
        link.innerHTML = '<img src="https://api.mapy.com/img/api/logo.svg" alt="Mapy.com" style="height:18px;display:block">';
        L.DomEvent.disableClickPropagation(link);
        return container;
      },
    });
    map.addControl(new LogoControl());

    function esc(s) {
      return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
        return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
      });
    }

    var markers = [];
    points.forEach(function (p) {
      var marker = L.marker([p.lat, p.lon]).addTo(map);
      var html = '<div style="text-align:center;min-width:140px">';
      if (p.men_url || p.women_url) {
        html += '<div style="display:flex;gap:4px;justify-content:center;margin-bottom:6px">';
        if (p.men_url) html += '<img src="' + esc(p.men_url) + '" style="width:60px;height:60px;object-fit:contain;border:2px solid #3b82f6;border-radius:6px">';
        if (p.women_url) html += '<img src="' + esc(p.women_url) + '" style="width:60px;height:60px;object-fit:contain;border:2px solid #ec4899;border-radius:6px">';
        html += '</div>';
      }
      html += '<div style="font-weight:600;margin-bottom:4px">' + esc(p.place) + '</div>';
      html += '<a href="#" data-row="' + esc(p.row_key) + '" class="map-card-link" style="color:#2563eb;font-size:0.85rem">Zobrazit kartu</a>';
      html += '</div>';
      marker.bindPopup(html);
      markers.push(marker);
    });

    if (markers.length === 1) {
      map.setView(markers[0].getLatLng(), 13);
    } else {
      map.fitBounds(L.featureGroup(markers).getBounds().pad(0.2));
    }

    map.on('popupopen', function (e) {
      var link = e.popup._contentNode.querySelector('.map-card-link');
      if (!link) return;
      link.addEventListener('click', function (ev) {
        ev.preventDefault();
        var card = document.getElementById('pair-' + link.getAttribute('data-row'));
        if (card) {
          card.scrollIntoView({ behavior: 'smooth', block: 'center' });
          card.classList.add('card-highlight');
          setTimeout(function () { card.classList.remove('card-highlight'); }, 2000);
        }
        map.closePopup();
      });
    });
  })();
  </script>
  {% endif %}
```

- [ ] **Step 5: Spustit test — musí projít**

Run: `.venv/Scripts/python.exe manage.py test gallery.tests.SignpairListMapRenderTest -v 2`
Expected: PASS (2 testy OK)

- [ ] **Step 6: Spustit celou sadu**

Run: `.venv/Scripts/python.exe manage.py test gallery -v 2`
Expected: PASS — všechny testy (24+).

- [ ] **Step 7: Commit**

```bash
git add gallery/templates/gallery/signpair_list.html gallery/tests.py
git commit -m "Render Leaflet + Mapy.com map with markers on homepage"
```

---

### Task 3: Ověření, nasazení a vizuální kontrola

**Files:** žádné změny (jen ověření)

- [ ] **Step 1: Spustit kompletní testy**

Run: `.venv/Scripts/python.exe manage.py test gallery -v 2`
Expected: PASS, 0 chyb.

- [ ] **Step 2: Lokální vizuální kontrola (vyžaduje `MAPY_API_KEY` a `AZURE_STORAGE_CONNECTION_STRING`)**

Run: `.venv/Scripts/python.exe -c "import os; os.environ.setdefault('DJANGO_SETTINGS_MODULE','toiletlabels.settings'); from dotenv import load_dotenv; load_dotenv(); from django.core.management import execute_from_command_line; execute_from_command_line(['manage.py','runserver'])"`
Postup: otevři `http://127.0.0.1:8000/`, ověř:
- mapa se zobrazí nad mřížkou, dlaždice z Mapy.com se načtou,
- vlevo dole je logo Mapy.com, vpravo dole copyright,
- markery sedí na správných místech, popup ukazuje náhledy + název,
- klik na „Zobrazit kartu" odroluje na kartu a krátce ji zvýrazní.

- [ ] **Step 3: Merge do `main`, push a kontrola nasazení**

```bash
git checkout main
git pull --ff-only
git merge --no-ff <feature-branch> -m "Merge: homepage map (Leaflet + Mapy.com)"
.venv/Scripts/python.exe manage.py test gallery
git branch -d <feature-branch>
git push origin main
```

Pak sledovat GitHub Actions deploy:

```bash
gh run list --branch main --workflow "Deploy to Azure Web App" --limit 1
gh run watch <run-id> --exit-status
```

Po nasazení ověřit produkční web (mapa se načítá s produkčním `MAPY_API_KEY` z Azure App Settings).

---

## Poznámky

- `MAPY_API_KEY` je už nastavený lokálně (`.env`) i v Azure App Settings.
- Klíč se renderuje do stránky (viditelný v prohlížeči) — to je u dlaždicových klíčů normální; ochrana je omezení klíče na doménu v dashboardu Mapy.com.
- Popup HTML je sestavováno přes `esc()` (ochrana proti vložení HTML z dat značky), i když data zadává jen superuser.
