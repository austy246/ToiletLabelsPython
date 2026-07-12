"""Normalize legacy free-text Country values to canonical Czech names.

Maps every label's Country through gallery.services.countries.normalize_country
and re-saves it when the value changes. Run with --dry-run to preview.
"""
from django.core.management.base import BaseCommand

from gallery.services.azure_table import AzureTableManager
from gallery.services.countries import normalize_country


class Command(BaseCommand):
    help = "Normalize Country values to canonical Czech names."

    def add_arguments(self, parser):
        parser.add_argument(
            "--dry-run",
            action="store_true",
            help="Show what would change without saving.",
        )

    def handle(self, *args, **options):
        dry_run = options["dry_run"]
        table = AzureTableManager()
        updated = 0
        for label in table.list_labels():
            old = label.get("Country", "")
            new = normalize_country(old)
            if new == old:
                continue
            self.stdout.write(f"  {label.get('RowKey')}: {old!r} -> {new!r}")
            if not dry_run:
                table.upsert_label(
                    label_id=label.get("RowKey"),
                    place=label.get("Place", ""),
                    description=label.get("Description", ""),
                    men_image_url=label.get("MenImageUrl", ""),
                    women_image_url=label.get("WomenImageUrl", ""),
                    num_voters=label.get("NumVoters", 0),
                    avg_vote=label.get("AvgVote", 0),
                    country=new,
                    city=label.get("City", ""),
                    created=label.get("Created"),
                    latitude=label.get("Latitude", ""),
                    longitude=label.get("Longitude", ""),
                    men_thumb_url=label.get("MenThumbUrl", ""),
                    women_thumb_url=label.get("WomenThumbUrl", ""),
                )
            updated += 1
        suffix = " (dry-run, nothing saved)" if dry_run else ""
        self.stdout.write(self.style.SUCCESS(f"Done. {updated} label(s) changed.{suffix}"))
