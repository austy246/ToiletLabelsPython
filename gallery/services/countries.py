"""Canonical country list (Czech names) for the country picker + normalization.

The country field used to be free text, so the same country was stored under
many spellings ("Czechia", "CZECH REPUBLIC", "Česká republika"). This module
provides a fixed list for the dropdown and a normalizer that folds any legacy
or foreign spelling to the canonical Czech name.

Pure module, no Django/Azure dependencies.
"""
import unicodedata

# Order shown in the dropdown: neighbours first, then the rest alphabetically.
COUNTRIES = [
    "Česko",
    "Slovensko",
    "Rakousko",
    "Německo",
    "Polsko",
    "Belgie",
    "Francie",
    "Chorvatsko",
    "Itálie",
    "Maďarsko",
    "Nizozemsko",
    "Portugalsko",
    "Řecko",
    "Slovinsko",
    "Spojené království",
    "Španělsko",
    "Švédsko",
    "Švýcarsko",
]


def _fold(value):
    """Lower-case and strip accents for accent/case-insensitive matching."""
    text = unicodedata.normalize("NFKD", value.strip().lower())
    return "".join(c for c in text if not unicodedata.combining(c))


# Legacy / foreign spellings -> canonical Czech name. Keys are compared folded.
_ALIASES = {
    "czechia": "Česko",
    "czech republic": "Česko",
    "ceska republika": "Česko",
    "cesko": "Česko",
    "slovakia": "Slovensko",
    "austria": "Rakousko",
    "germany": "Německo",
    "poland": "Polsko",
    "belgium": "Belgie",
    "france": "Francie",
    "croatia": "Chorvatsko",
    "italy": "Itálie",
    "hungary": "Maďarsko",
    "netherlands": "Nizozemsko",
    "portugal": "Portugalsko",
    "greece": "Řecko",
    "slovenia": "Slovinsko",
    "united kingdom": "Spojené království",
    "great britain": "Spojené království",
    "uk": "Spojené království",
    "spain": "Španělsko",
    "sweden": "Švédsko",
    "switzerland": "Švýcarsko",
}

# Canonical names indexed by their folded form for fast self-match.
_CANONICAL_BY_FOLD = {_fold(name): name for name in COUNTRIES}


def normalize_country(value):
    """Return the canonical Czech country name for a free-form value.

    Empty/blank input stays "". An unknown value is returned trimmed but
    otherwise unchanged, so nothing is silently lost.
    """
    if not value or not value.strip():
        return ""
    key = _fold(value)
    if key in _CANONICAL_BY_FOLD:
        return _CANONICAL_BY_FOLD[key]
    if key in _ALIASES:
        return _ALIASES[key]
    return value.strip()
