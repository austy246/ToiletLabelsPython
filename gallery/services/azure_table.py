import os
from azure.data.tables import TableServiceClient

class AzureTableManager:
    def __init__(self):
        self.connection_string = os.environ.get("AZURE_STORAGE_CONNECTION_STRING")
        self.table_name = "ToiletLabels"
        self.service_client = TableServiceClient.from_connection_string(self.connection_string)
        self.table_client = self.service_client.get_table_client(self.table_name)
        try:
            self.table_client.create_table()
        except Exception as e:
            print(f"[AzureToiletLabelService] Table creation failed or already exists: {e}")

    def upsert_label(self, label_id, place, description, men_image_url, women_image_url, num_voters, avg_vote, country=None, city=None, created=None, latitude=None, longitude=None, men_thumb_url=None, women_thumb_url=None):
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
            'MenThumbUrl': men_thumb_url if men_thumb_url is not None else '',
            'WomenThumbUrl': women_thumb_url if women_thumb_url is not None else '',
            'Created': created,
        }
        self.table_client.upsert_entity(entity=entity)

    def get_label(self, label_id):
        try:
            entity = self.table_client.get_entity(partition_key='label', row_key=str(label_id))
            return entity
        except Exception:
            return None

    def list_labels(self):
        # Fetch all, then sort by Created descending (newest first)
        labels = list(self.table_client.query_entities("PartitionKey eq 'label'"))
        return sorted(labels, key=lambda x: x.get('Created', ''), reverse=True)

