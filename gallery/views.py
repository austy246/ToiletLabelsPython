from django.shortcuts import render, redirect
from django.http import HttpResponseForbidden
from django.urls import reverse
from .services.azure_table import AzureTableManager
from .services.azure_blob import AzureBlobManager
import uuid


def _require_superuser(request):
    if not request.user.is_authenticated:
        return redirect(f'/login/?next={request.path}')
    if not request.user.is_superuser:
        return HttpResponseForbidden('Access denied')
    return None


def upload_label(request):
    denied = _require_superuser(request)
    if denied:
        return denied
    table_manager = AzureTableManager()
    blob_manager = AzureBlobManager()
    if request.method == 'POST':
        place = request.POST.get('place', '')
        description = request.POST.get('description', '')
        country = request.POST.get('country', '')
        city = request.POST.get('city', '')
        men_image = request.FILES['men_image']
        women_image = request.FILES['women_image']
        label_id = str(uuid.uuid4())
        # Upload images to Azure Blob Storage
        import os
        men_ext = os.path.splitext(men_image.name)[1]
        women_ext = os.path.splitext(women_image.name)[1]
        men_filename = f"{label_id}_men{men_ext}"
        women_filename = f"{label_id}_women{women_ext}"
        men_url = blob_manager.upload_image(men_image, 'toiletlabels', men_filename)
        women_url = blob_manager.upload_image(women_image, 'toiletlabels', women_filename)
        # Store only the filenames in Azure Table
        table_manager.upsert_label(
            label_id=label_id,
            place=place,
            description=description,
            men_image_url=men_filename,
            women_image_url=women_filename,
            num_voters=0,
            avg_vote=0,
            country=country,
            city=city,
        )
        return redirect(reverse('gallery:signpair_list'))
    return render(request, 'gallery/upload_label.html')


def edit_label(request, pk):
    denied = _require_superuser(request)
    if denied:
        return denied
    table_manager = AzureTableManager()
    blob_manager = AzureBlobManager()
    pair = table_manager.get_label(pk)
    if not pair:
        from django.http import Http404
        raise Http404
    if request.method == 'POST':
        place = request.POST.get('place', '')
        description = request.POST.get('description', '')
        country = request.POST.get('country', '')
        city = request.POST.get('city', '')
        men_image = request.FILES.get('men_image')
        women_image = request.FILES.get('women_image')
        men_filename = pair.get('MenImageUrl', '')
        women_filename = pair.get('WomenImageUrl', '')
        # Handle men image upload if provided
        if men_image:
            import os
            men_ext = os.path.splitext(men_image.name)[1]
            men_filename = f"{pk}_men{men_ext}"
            blob_manager.upload_image(men_image, 'toiletlabels', men_filename)
        # Handle women image upload if provided
        if women_image:
            import os
            women_ext = os.path.splitext(women_image.name)[1]
            women_filename = f"{pk}_women{women_ext}"
            blob_manager.upload_image(women_image, 'toiletlabels', women_filename)
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
