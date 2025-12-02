#!/bin/bash

# Run database migrations
python manage.py migrate --noinput

# Start Gunicorn
gunicorn --bind=0.0.0.0:8000 --timeout 600 toiletlabels.wsgi:application
