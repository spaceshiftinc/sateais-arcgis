"""Storing and resolving the API key.

The key goes into the operating system credential store via `keyring`, which
ArcGIS Pro ships, so there is no plain-text file to protect.

Resolution order matches the QGIS plugin and the `sateais` CLI, so anyone who
has already signed in with the CLI does not have to enter the key again:

    1. an explicit value (a tool parameter)
    2. the credential store
    3. SATEAIS_API_KEY
    4. ~/.sateais/credentials (shared with the CLI)

The API base URL is not stored; the client always talks to production.
"""

from __future__ import annotations

import contextlib
import json
import os
from pathlib import Path

SERVICE_NAME = "SateAIs"
ACCOUNT_NAME = "api_key"  # pragma: allowlist secret

CREDENTIALS_FILE = Path.home() / ".sateais" / "credentials"


class AuthNotConfiguredError(Exception):
    """No API key could be resolved."""


def _keyring():
    """Import keyring lazily; return None where it is unavailable."""
    try:
        import keyring

        return keyring
    except Exception:
        return None


def get_stored_api_key() -> str | None:
    kr = _keyring()
    if kr is None:
        return None
    try:
        return kr.get_password(SERVICE_NAME, ACCOUNT_NAME) or None
    except Exception:
        # An unusable credential store should not break the whole tool.
        return None


def set_api_key(api_key: str) -> None:
    kr = _keyring()
    if kr is None:
        raise AuthNotConfiguredError(
            "No credential store is available in this Python environment. "
            "Set the SATEAIS_API_KEY environment variable instead."
        )
    kr.set_password(SERVICE_NAME, ACCOUNT_NAME, api_key)


def clear_api_key() -> None:
    kr = _keyring()
    if kr is None:
        return
    with contextlib.suppress(Exception):
        kr.delete_password(SERVICE_NAME, ACCOUNT_NAME)


def _read_credentials_file() -> str | None:
    """Read the key from `~/.sateais/credentials`.

    Accepts the JSON the CLI writes, and a legacy file that is just the key.
    """
    if not CREDENTIALS_FILE.exists():
        return None
    try:
        text = CREDENTIALS_FILE.read_text(encoding="utf-8").strip()
    except OSError:
        return None
    if not text:
        return None
    if text.startswith("{"):
        try:
            data = json.loads(text)
        except json.JSONDecodeError:
            return None
        if isinstance(data, dict):
            key = data.get("api_key")
            return key if isinstance(key, str) and key else None
        return None
    return text


def resolve_api_key(explicit: str | None = None) -> str | None:
    if explicit:
        return explicit
    stored = get_stored_api_key()
    if stored:
        return stored
    from_env = os.environ.get("SATEAIS_API_KEY")
    if from_env:
        return from_env
    return _read_credentials_file()


def require_api_key(explicit: str | None = None) -> str:
    key = resolve_api_key(explicit)
    if not key:
        raise AuthNotConfiguredError(
            "No API key is configured. Run '1 Setup > Configure Account' first."
        )
    return key


def mask_api_key(api_key: str | None) -> str:
    """Mask a key for display, e.g. `sk_live_****abcd`."""
    if not api_key:
        return "(not set)"
    if len(api_key) <= 12:
        return "***"
    return f"{api_key[:8]}****{api_key[-4:]}"


def describe_source(explicit: str | None = None) -> str:
    """Where the key in use came from."""
    if explicit:
        return "the tool parameter"
    if get_stored_api_key():
        return "the operating system credential store"
    if os.environ.get("SATEAIS_API_KEY"):
        return "the SATEAIS_API_KEY environment variable"
    if _read_credentials_file():
        return f"{CREDENTIALS_FILE} (shared with the sateais CLI)"
    return "nowhere - no key is configured"
