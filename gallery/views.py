import os
import uuid

from django.shortcuts import render, redirect
from django.conf import settings

from .services.azure_table import AzureTableManager
from .services.azure_blob import AzureBlobManager

def upload_label(request):
    table_manager = AzureTableManager()
    blob_manager = AzureBlobManager()
    if not request.user.is_authenticated or not request.user.is_superuser:
        return render(request, '403.html', status=403)
    if request.method == 'POST':
        place = request.POST['place']
        description = request.POST['description']
        men_image = request.FILES['men_image']
        women_image = request.FILES['women_image']
        label_id = str(uuid.uuid4())
        # Upload images to Azure Blob Storage
        men_ext = os.path.splitext(men_image.name)[1]
        women_ext = os.path.splitext(women_image.name)[1]
        men_filename = f"{label_id}_men{men_ext}"
        women_filename = f"{label_id}_women{women_ext}"
        blob_manager.upload_image(men_image, settings.AZURE_CONTAINER, men_filename)
        blob_manager.upload_image(women_image, settings.AZURE_CONTAINER, women_filename)
        # Store only the filenames in Azure Table
        table_manager.upsert_label(
            label_id=label_id,
            place=place,
            description=description,
            men_image_url=men_filename,
            women_image_url=women_filename,
            num_voters=0,
            avg_vote=0,
        )
        return redirect(reverse('gallery:signpair_list'))
    return render(request, 'gallery/upload_label.html')


def edit_label(request, pk):
    table_manager = AzureTableManager()
    blob_manager = AzureBlobManager()
    if not request.user.is_authenticated or not request.user.is_superuser:
        return render(request, '403.html', status=403)
    pair = table_manager.get_label(pk)
    if not pair:
        return render(request, '404.html', status=404)
    if request.method == 'POST':
        place = request.POST.get('place', '')
        description = request.POST.get('description', '')
        restaurant = request.POST.get('restaurant', '')
        country = request.POST.get('country', '')
        city = request.POST.get('city', '')
        men_image = request.FILES.get('men_image')
        women_image = request.FILES.get('women_image')
        men_filename = pair.get('MenImageUrl', '')
        women_filename = pair.get('WomenImageUrl', '')
        # Handle men image upload if provided
        if men_image:
            men_ext = os.path.splitext(men_image.name)[1]
            men_filename = f"{pk}_men{men_ext}"
            blob_manager.upload_image(men_image, settings.AZURE_CONTAINER, men_filename)
        # Handle women image upload if provided
        if women_image:
            women_ext = os.path.splitext(women_image.name)[1]
            women_filename = f"{pk}_women{women_ext}"
            blob_manager.upload_image(women_image, settings.AZURE_CONTAINER, women_filename)
        table_manager.upsert_label(
            label_id=pk,
            place=place,
            description=description,
            men_image_url=men_filename,
            women_image_url=women_filename,
            num_voters=pair.get('NumVoters', 0),
            avg_vote=pair.get('AvgVote', 0),
            country=country,
            city=city,
            restaurant=restaurant,
        )
        return redirect(reverse('gallery:signpair_list'))
    return render(request, 'gallery/edit_label.html', {
        'pair': pair,
        'AZURE_BLOB_BASE_URL': AzureBlobManager.get_blob_base_url(),
    })

def signpair_list(request):
    table_manager = AzureTableManager()
    pairs = table_manager.list_labels()
    return render(request, 'gallery/signpair_list.html', {
        'pairs': pairs,
        'AZURE_BLOB_BASE_URL': AzureBlobManager.get_blob_base_url(),
    })
