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
