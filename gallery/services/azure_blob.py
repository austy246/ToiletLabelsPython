import os
from azure.storage.blob import BlobServiceClient, ContentSettings

class AzureBlobManager:
    @staticmethod
    def get_blob_base_url():
        import os
        import re
        conn_str = os.environ.get("AZURE_STORAGE_CONNECTION_STRING", "")
        match = re.search(r"AccountName=([^;]+)", conn_str)
        account = match.group(1) if match else ""
        if account:
            return f"https://{account}.blob.core.windows.net/toiletlabels/"
        return ""

    def __init__(self, connection_string=None):
        if connection_string is None:
            connection_string = os.environ.get("AZURE_STORAGE_CONNECTION_STRING")
        self.blob_service_client = BlobServiceClient.from_connection_string(connection_string)

    def upload_image(self, file, container_name, blob_name, content_type=None):
        container_client = self.blob_service_client.get_container_client(container_name)
        try:
            container_client.create_container()
        except Exception:
            pass  # Container may already exist
        blob_client = container_client.get_blob_client(blob_name)
        if content_type:
            blob_client.upload_blob(
                file, overwrite=True,
                content_settings=ContentSettings(content_type=content_type),
            )
        else:
            blob_client.upload_blob(file, overwrite=True)
        return blob_client.url

    def download_image(self, container_name, blob_name):
        container_client = self.blob_service_client.get_container_client(container_name)
        blob_client = container_client.get_blob_client(blob_name)
        return blob_client.download_blob().readall()
