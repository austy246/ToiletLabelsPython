"""Resolve geographic coordinates for toilet labels.

Priority: EXIF GPS (men photo) -> EXIF GPS (women photo) -> geocode from
Place/City/Country -> None. Pure module, no Django/Azure dependencies.
"""
import json
from io import BytesIO
from urllib.parse import urlencode
from urllib.request import Request, urlopen

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
