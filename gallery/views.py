from django.shortcuts import render, redirect
from django.http import HttpResponseForbidden
from django.urls import reverse
from .services.azure_table import AzureTableManager
from .services.azure_blob import AzureBlobManager
from .services.geo import resolve_coordinates, geocode_place
from .services.images import make_thumbnail, THUMB_CONTENT_TYPE
import uuid
import os


def _make_and_upload_thumb(blob_manager, image_bytes, original_filename):
    """Generate a WebP thumbnail, upload it, return its blob filename ('' on failure)."""
    if not image_bytes:
        return ''
    try:
        thumb_bytes = make_thumbnail(image_bytes)
    except Exception:
        return ''
    base, _ = os.path.splitext(original_filename)
    thumb_filename = f"{base}_thumb.webp"
    blob_manager.upload_image(thumb_bytes, 'toiletlabels', thumb_filename,
                              content_type=THUMB_CONTENT_TYPE)
    return thumb_filename


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
        # Read bytes once: used for both EXIF extraction and blob upload.
        men_bytes = men_image.read()
        women_bytes = women_image.read()
        label_id = str(uuid.uuid4())
        # Resolve coordinates: EXIF(men) -> EXIF(women) -> geocode.
        coords = resolve_coordinates(men_bytes, women_bytes, place, city, country)
        latitude, longitude = coords if coords else (None, None)
        # Upload images to Azure Blob Storage
        import os
        men_ext = os.path.splitext(men_image.name)[1]
        women_ext = os.path.splitext(women_image.name)[1]
        men_filename = f"{label_id}_men{men_ext}"
        women_filename = f"{label_id}_women{women_ext}"
        blob_manager.upload_image(men_bytes, 'toiletlabels', men_filename)
        blob_manager.upload_image(women_bytes, 'toiletlabels', women_filename)
        # Generate and upload WebP thumbnails
        men_thumb = _make_and_upload_thumb(blob_manager, men_bytes, men_filename)
        women_thumb = _make_and_upload_thumb(blob_manager, women_bytes, women_filename)
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
            latitude=latitude,
            longitude=longitude,
            men_thumb_url=men_thumb,
            women_thumb_url=women_thumb,
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
        men_thumb = pair.get('MenThumbUrl', '')
        women_thumb = pair.get('WomenThumbUrl', '')
        men_bytes = None
        women_bytes = None
        # Handle men image upload if provided
        if men_image:
            import os
            men_bytes = men_image.read()
            men_ext = os.path.splitext(men_image.name)[1]
            men_filename = f"{pk}_men{men_ext}"
            blob_manager.upload_image(men_bytes, 'toiletlabels', men_filename)
            men_thumb = _make_and_upload_thumb(blob_manager, men_bytes, men_filename)
        # Handle women image upload if provided
        if women_image:
            import os
            women_bytes = women_image.read()
            women_ext = os.path.splitext(women_image.name)[1]
            women_filename = f"{pk}_women{women_ext}"
            blob_manager.upload_image(women_bytes, 'toiletlabels', women_filename)
            women_thumb = _make_and_upload_thumb(blob_manager, women_bytes, women_filename)
        # Resolve coordinates: recompute from new photo(s) if any were uploaded;
        # otherwise keep existing coords, falling back to geocode when missing.
        if men_bytes or women_bytes:
            coords = resolve_coordinates(men_bytes, women_bytes, place, city, country)
            latitude, longitude = coords if coords else (None, None)
        else:
            existing_lat = pair.get('Latitude', '')
            existing_lon = pair.get('Longitude', '')
            if existing_lat != '' and existing_lon != '':
                latitude, longitude = existing_lat, existing_lon
            else:
                coords = geocode_place(place, city, country)
                latitude, longitude = coords if coords else (None, None)
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
            created=pair.get('Created'),
            latitude=latitude,
            longitude=longitude,
            men_thumb_url=men_thumb,
            women_thumb_url=women_thumb,
        )
        return redirect(reverse('gallery:signpair_list'))
    return render(request, 'gallery/edit_label.html', {
        'pair': pair,
        'AZURE_BLOB_BASE_URL': AzureBlobManager.get_blob_base_url(),
    })

def _build_map_points(pairs, base_url):
    """Build map marker data for labels that have valid coordinates."""
    points = []
    for pair in pairs:
        lat = pair.get('Latitude', '')
        lon = pair.get('Longitude', '')
        if lat is None or lon is None or lat == '' or lon == '':
            continue
        try:
            lat_f = float(lat)
            lon_f = float(lon)
        except (TypeError, ValueError):
            continue
        men = pair.get('MenThumbUrl') or pair.get('MenImageUrl', '')
        women = pair.get('WomenThumbUrl') or pair.get('WomenImageUrl', '')
        points.append({
            'lat': lat_f,
            'lon': lon_f,
            'place': pair.get('Place') or pair.get('City') or 'Untitled',
            'men_url': (base_url + men) if men else '',
            'women_url': (base_url + women) if women else '',
            'row_key': pair.get('RowKey', ''),
        })
    return points


def signpair_list(request):
    table_manager = AzureTableManager()
    pairs = table_manager.list_labels()
    base_url = AzureBlobManager.get_blob_base_url()
    return render(request, 'gallery/signpair_list.html', {
        'pairs': pairs,
        'AZURE_BLOB_BASE_URL': base_url,
        'map_points': _build_map_points(pairs, base_url),
        'MAPY_API_KEY': os.environ.get('MAPY_API_KEY', ''),
    })
