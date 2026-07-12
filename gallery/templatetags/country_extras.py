"""Template helpers for rendering country info (flag lookup)."""
from django import template

from gallery.services.countries import country_code as _country_code

register = template.Library()


@register.filter
def country_code(value):
    """ISO alpha-2 code for a country name (e.g. 'Rakousko' -> 'at'), '' if unknown."""
    return _country_code(value)
