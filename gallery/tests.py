import os
from django.test import TestCase
from unittest.mock import patch, MagicMock
from .services.azure_blob import AzureBlobManager
from .services.azure_table import AzureTableManager

from unittest.mock import patch

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
        # Test get_label returns None on exception
        mock_table_client.get_entity.side_effect = Exception('Not found')
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


from io import BytesIO
from PIL import Image
from gallery.services.geo import extract_gps


def _make_image_with_gps(lat_ref, lat_dms, lon_ref, lon_dms):
    img = Image.new("RGB", (10, 10))
    exif = img.getexif()
    exif[0x8825] = {
        1: lat_ref,
        2: lat_dms,
        3: lon_ref,
        4: lon_dms,
    }
    buf = BytesIO()
    img.save(buf, format="JPEG", exif=exif)
    return buf.getvalue()


class GeoExtractGpsTest(TestCase):
    def test_extracts_decimal_degrees_north_east(self):
        data = _make_image_with_gps("N", (50.0, 5.0, 0.0), "E", (14.0, 25.0, 0.0))
        self.assertEqual(extract_gps(data), (50.083333, 14.416667))

    def test_south_west_are_negative(self):
        data = _make_image_with_gps("S", (33.0, 51.0, 0.0), "W", (151.0, 12.0, 0.0))
        lat, lon = extract_gps(data)
        self.assertLess(lat, 0)
        self.assertLess(lon, 0)

    def test_no_exif_returns_none(self):
        buf = BytesIO()
        Image.new("RGB", (10, 10)).save(buf, format="JPEG")
        self.assertIsNone(extract_gps(buf.getvalue()))

    def test_none_input_returns_none(self):
        self.assertIsNone(extract_gps(None))


import json as _json
from gallery.services.geo import geocode_place


class GeoGeocodeTest(TestCase):
    @patch("gallery.services.geo.urlopen")
    def test_returns_first_result_coords(self, mock_urlopen):
        cm = MagicMock()
        cm.read.return_value = _json.dumps(
            [{"lat": "50.087", "lon": "14.421"}]
        ).encode("utf-8")
        mock_urlopen.return_value.__enter__.return_value = cm
        self.assertEqual(geocode_place("Cafe Louvre", "Prague", "Czechia"),
                         (50.087, 14.421))

    def test_empty_fields_returns_none_without_network(self):
        with patch("gallery.services.geo.urlopen") as mock_urlopen:
            self.assertIsNone(geocode_place("", "", ""))
            mock_urlopen.assert_not_called()

    @patch("gallery.services.geo.urlopen")
    def test_no_results_returns_none(self, mock_urlopen):
        cm = MagicMock()
        cm.read.return_value = b"[]"
        mock_urlopen.return_value.__enter__.return_value = cm
        self.assertIsNone(geocode_place("Nowhere", "", ""))

    @patch("gallery.services.geo.urlopen", side_effect=OSError("network"))
    def test_network_error_returns_none(self, mock_urlopen):
        self.assertIsNone(geocode_place("Cafe", "Prague", "CZ"))


from gallery.services.geo import resolve_coordinates


class GeoResolveTest(TestCase):
    @patch("gallery.services.geo.geocode_place")
    @patch("gallery.services.geo.extract_gps")
    def test_prefers_men_exif(self, mock_extract, mock_geocode):
        mock_extract.return_value = (1.0, 2.0)
        result = resolve_coordinates(b"men", b"women", "p", "c", "co")
        self.assertEqual(result, (1.0, 2.0))
        mock_geocode.assert_not_called()
        self.assertEqual(mock_extract.call_count, 1)

    @patch("gallery.services.geo.geocode_place")
    @patch("gallery.services.geo.extract_gps")
    def test_falls_back_to_women_then_geocode(self, mock_extract, mock_geocode):
        mock_extract.side_effect = [None, None]  # men None, women None
        mock_geocode.return_value = (3.0, 4.0)
        result = resolve_coordinates(b"men", b"women", "p", "c", "co")
        self.assertEqual(result, (3.0, 4.0))
        self.assertEqual(mock_extract.call_count, 2)
        mock_geocode.assert_called_once_with("p", "c", "co")

    @patch("gallery.services.geo.geocode_place", return_value=None)
    @patch("gallery.services.geo.extract_gps", return_value=None)
    def test_returns_none_when_nothing_found(self, mock_extract, mock_geocode):
        self.assertIsNone(resolve_coordinates(b"m", b"w", "", "", ""))


@patch.dict(os.environ, {"AZURE_STORAGE_CONNECTION_STRING": "fake-connection-string"})
class UpsertCoordinatesTest(TestCase):
    @patch("gallery.services.azure_table.TableServiceClient")
    def test_stores_float_coordinates(self, mock_tsc):
        mock_table_client = MagicMock()
        mock_tsc.from_connection_string.return_value.get_table_client.return_value = (
            mock_table_client
        )
        manager = AzureTableManager()
        manager.upsert_label("id1", "P", "D", "m.jpg", "w.jpg", 0, 0,
                             latitude=50.1, longitude=14.2)
        entity = mock_table_client.upsert_entity.call_args.kwargs["entity"]
        self.assertEqual(entity["Latitude"], 50.1)
        self.assertEqual(entity["Longitude"], 14.2)

    @patch("gallery.services.azure_table.TableServiceClient")
    def test_empty_coordinates_stored_as_blank(self, mock_tsc):
        mock_table_client = MagicMock()
        mock_tsc.from_connection_string.return_value.get_table_client.return_value = (
            mock_table_client
        )
        manager = AzureTableManager()
        manager.upsert_label("id1", "P", "D", "m.jpg", "w.jpg", 0, 0)
        entity = mock_table_client.upsert_entity.call_args.kwargs["entity"]
        self.assertEqual(entity["Latitude"], "")
        self.assertEqual(entity["Longitude"], "")
