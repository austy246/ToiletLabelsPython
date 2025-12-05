import datetime
import logging
import os

from azure.core.exceptions import ResourceExistsError, ResourceNotFoundError
from azure.data.tables import TableServiceClient
from django.conf import settings

logger = logging.getLogger(__name__)

PARTITION_KEY = 'label'


class AzureTableManager:
    def __init__(self):
        self.connection_string = os.environ.get("AZURE_STORAGE_CONNECTION_STRING")
        self.table_name = settings.AZURE_TABLE_NAME
        self.service_client = TableServiceClient.from_connection_string(self.connection_string)
        self.table_client = self.service_client.get_table_client(self.table_name)
        try:
            self.table_client.create_table()
        except ResourceExistsError:
            pass  # Table already exists

    def upsert_label(self, label_id, place, description, men_image_url, women_image_url,
                     num_voters, avg_vote, country=None, city=None, restaurant=None, created=None):
        if created is None:
            created = datetime.datetime.utcnow().isoformat()
        entity = {
            'PartitionKey': PARTITION_KEY,
            'RowKey': str(label_id),
            'Place': place,
            'Description': description,
            'MenImageUrl': men_image_url,
            'WomenImageUrl': women_image_url,
            'NumVoters': num_voters,
            'AvgVote': avg_vote,
            'Country': country or '',
            'City': city or '',
            'Restaurant': restaurant or '',
            'Created': created,
        }
        self.table_client.upsert_entity(entity=entity)

    def get_label(self, label_id):
        try:
            entity = self.table_client.get_entity(partition_key=PARTITION_KEY, row_key=str(label_id))
            return entity
        except ResourceNotFoundError:
            return None

    def list_labels(self):
        labels = list(self.table_client.query_entities(f"PartitionKey eq '{PARTITION_KEY}'"))
        return sorted(labels, key=lambda x: x.get('Created', ''), reverse=True)
