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
