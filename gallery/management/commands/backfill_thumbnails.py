"""Backfill WebP thumbnails for labels whose photos have none yet.

For each label missing MenThumbUrl/WomenThumbUrl, downloads the original blob,
generates a WebP thumbnail, uploads it, and stores the thumbnail filename.
Run with --dry-run to preview without saving.
"""
import os

from django.core.management.base import BaseCommand

from gallery.services.azure_table import AzureTableManager
from gallery.services.azure_blob import AzureBlobManager
from gallery.services.images import make_thumbnail, THUMB_CONTENT_TYPE

_CONTAINER = "toiletlabels"


class Command(BaseCommand):
    help = "Backfill WebP thumbnails for labels missing them."

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
            men_name = label.get("MenImageUrl", "")
            women_name = label.get("WomenImageUrl", "")
            men_thumb = label.get("MenThumbUrl", "")
            women_thumb = label.get("WomenThumbUrl", "")

            need_men = bool(men_name) and not men_thumb
            need_women = bool(women_name) and not women_thumb
            if not need_men and not need_women:
                continue

            if need_men:
                new = self._make(blob, men_name, dry_run)
                if new:
                    men_thumb = new
            if need_women:
                new = self._make(blob, women_name, dry_run)
                if new:
                    women_thumb = new

            self.stdout.write(f"  {row_key}: men={men_thumb or '-'} women={women_thumb or '-'}")
            if not dry_run:
                table.upsert_label(
                    label_id=row_key,
                    place=label.get("Place", ""),
                    description=label.get("Description", ""),
                    men_image_url=men_name,
                    women_image_url=women_name,
                    num_voters=label.get("NumVoters", 0),
                    avg_vote=label.get("AvgVote", 0),
                    country=label.get("Country", ""),
                    city=label.get("City", ""),
                    created=label.get("Created"),
                    latitude=label.get("Latitude", ""),
                    longitude=label.get("Longitude", ""),
                    men_thumb_url=men_thumb,
                    women_thumb_url=women_thumb,
                )
                updated += 1
        suffix = " (dry-run, nothing saved)" if dry_run else ""
        self.stdout.write(self.style.SUCCESS(f"Done. {updated} label(s) updated.{suffix}"))

    def _make(self, blob, blob_name, dry_run):
        """Download original, build a WebP thumbnail, upload it, return its name."""
        try:
            original = blob.download_image(_CONTAINER, blob_name)
            thumb_bytes = make_thumbnail(original)
        except Exception as exc:
            self.stdout.write(f"    failed for {blob_name}: {exc}")
            return ""
        base, _ = os.path.splitext(blob_name)
        thumb_name = f"{base}_thumb.webp"
        if not dry_run:
            blob.upload_image(thumb_bytes, _CONTAINER, thumb_name,
                              content_type=THUMB_CONTENT_TYPE)
        return thumb_name
