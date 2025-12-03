#!/bin/bash

# Run database migrations
python manage.py migrate --noinput

# Create superuser if it doesn't exist
python manage.py shell << 'EOF'
from django.contrib.auth import get_user_model
import os

User = get_user_model()
username = os.environ.get('DJANGO_SUPERUSER_USERNAME', 'austy')
password = os.environ.get('DJANGO_SUPERUSER_PASSWORD')

if password and not User.objects.filter(username=username).exists():
    User.objects.create_superuser(username=username, email='', password=password)
    print(f'Superuser {username} created')
else:
    print(f'Superuser {username} already exists or no password set')
EOF

# Start Gunicorn
gunicorn --bind=0.0.0.0:8000 --timeout 600 toiletlabels.wsgi:application
