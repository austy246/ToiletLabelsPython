"""Image helpers: generate WebP thumbnails for label photos.

Pure module, no Django/Azure dependencies.
"""
from io import BytesIO

from PIL import Image, ImageOps
import pillow_heif

# Register the HEIF/HEIC opener so Pillow can decode iPhone photos.
pillow_heif.register_heif_opener()

THUMB_CONTENT_TYPE = "image/webp"


def make_thumbnail(image_bytes, max_size=400, quality=80):
    """Return WebP thumbnail bytes scaled so the longest side <= max_size.

    Aspect ratio is preserved and images smaller than max_size are not upscaled.
    """
    img = Image.open(BytesIO(image_bytes))
    # Bake EXIF orientation into the pixels (phone photos are often stored
    # sideways with an Orientation tag); the tag is dropped on save otherwise.
    img = ImageOps.exif_transpose(img)
    # Palette / grayscale-with-alpha -> a mode WebP can save cleanly.
    if img.mode == "P":
        img = img.convert("RGBA")
    elif img.mode not in ("RGB", "RGBA", "L"):
        img = img.convert("RGB")
    # thumbnail() keeps aspect ratio and never upscales.
    img.thumbnail((max_size, max_size))
    out = BytesIO()
    img.save(out, format="WEBP", quality=quality)
    return out.getvalue()
