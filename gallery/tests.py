import os

from azure.core.exceptions import ResourceNotFoundError
from django.test import TestCase
from unittest.mock import patch, MagicMock

from .services.azure_blob import AzureBlobManager
from .services.azure_table import AzureTableManager

@patch.dict(os.environ, {"AZURE_STORAGE_CONNECTION_STRING": "fake-connection-string"})
class AzureBlobManagerTest(TestCase):
    @patch('gallery.services.azure_blob.BlobServiceClient')
    def test_upload_image(self, mock_blob_service_client):
        mock_blob_client = MagicMock()
        mock_container_client = MagicMock()
        mock_container_client.get_blob_client.return_value = mock_blob_client
        mock_blob_service_client.from_connection_string.return_value.get_container_client.return_value = mock_container_client
        mock_blob_client.url = 'https://fakeurl.com/blob.jpg'
        manager = AzureBlobManager('fake-conn-string')
        url = manager.upload_image(b'data', 'container', 'blob.jpg')
        self.assertEqual(url, 'https://fakeurl.com/blob.jpg')
        mock_blob_client.upload_blob.assert_called_with(b'data', overwrite=True)

@patch.dict(os.environ, {"AZURE_STORAGE_CONNECTION_STRING": "fake-connection-string"})
class AzureTableManagerTest(TestCase):
    @patch('gallery.services.azure_table.TableServiceClient')
    def test_upsert_and_get_label(self, mock_table_service_client):
        mock_table_client = MagicMock()
        mock_table_service_client.from_connection_string.return_value.get_table_client.return_value = mock_table_client
        manager = AzureTableManager()
        # Test upsert_label
        manager.upsert_label('id1', 'Place', 'Desc', 'men.jpg', 'women.jpg', 0, 0)
        self.assertTrue(mock_table_client.upsert_entity.called)
        # Test get_label
        mock_table_client.get_entity.return_value = {'RowKey': 'id1'}
        result = manager.get_label('id1')
        self.assertEqual(result['RowKey'], 'id1')
        # Test get_label returns None on ResourceNotFoundError
        mock_table_client.get_entity.side_effect = ResourceNotFoundError('Not found')
        result = manager.get_label('id2')
        self.assertIsNone(result)
    @patch('gallery.services.azure_table.TableServiceClient')
    def test_list_labels(self, mock_table_service_client):
        mock_table_client = MagicMock()
        mock_table_service_client.from_connection_string.return_value.get_table_client.return_value = mock_table_client
        mock_table_client.query_entities.return_value = [{'RowKey': 'id1'}, {'RowKey': 'id2'}]
        manager = AzureTableManager()
        result = manager.list_labels()
        self.assertEqual(len(result), 2)
