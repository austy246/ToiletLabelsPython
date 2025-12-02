# ToiletLabelsPython

Reincarnation of my university time idea using Django.

A gallery of toilet sign images - always a pair of images, one for men and one for women.

Admin can upload images (one for men, one for women) along with the place name, coordinates, and description.

Images are stored in Azure Blob Storage.

## Local Development

```sh
# Create virtual environment
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt

# Run migrations
python manage.py migrate

# Run development server
python manage.py runserver
```

The app will be available at http://localhost:8000

## Deployment to Azure Web App

The project deploys automatically to Azure Web App via GitHub Actions on push to `main`.

### Setup

1. Create an Azure Service Principal:
   ```sh
   az ad sp create-for-rbac --name "toiletlabels-github" --role contributor \
       --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.Web/sites/toiletlabels
   ```

2. Add the `AZURE_CREDENTIALS` secret to your GitHub repository with this format:
   ```json
   {
       "clientId": "<appId from output>",
       "clientSecret": "<password from output>",
       "tenantId": "<tenant from output>",
       "subscriptionId": "<your subscription id>"
   }
   ```

3. Configure Azure Web App startup command:
   ```
   gunicorn --bind=0.0.0.0 --timeout 600 toiletlabels.wsgi:application
   ```

4. Set environment variables in Azure Web App Configuration:
   - `AZURE_ACCOUNT_NAME`
   - `AZURE_ACCOUNT_KEY`
   - `AZURE_CONTAINER`
   - `AZURE_TABLE_NAME`
