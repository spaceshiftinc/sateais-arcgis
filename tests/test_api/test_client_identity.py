"""The client names itself and its version on every request.

That header is how the server attributes usage to this client. The version
comes from one place so it cannot be forgotten on release.
"""

from __future__ import annotations

import re
from unittest.mock import patch

from sateais_arcgis import __version__
from sateais_arcgis.core.api.http import UrllibApiClient


class _Response:
    def __init__(self, body: bytes) -> None:
        self.body = body
        self.headers = {"Content-Type": "application/json"}

    def read(self, *_args) -> bytes:
        return self.body

    def __enter__(self):
        return self

    def __exit__(self, *_exc):
        return None


def test_version_is_semver():
    assert re.fullmatch(r"\d+\.\d+\.\d+", __version__)


@patch("sateais_arcgis.core.api.http._AUTH_STRIPPING_OPENER.open")
def test_every_request_identifies_this_client_and_its_version(mock_open):
    mock_open.return_value = _Response(b"{}")
    UrllibApiClient(api_key="sk_test").get_me()

    sent = mock_open.call_args[0][0]
    expected = f"sateais-arcgis/{__version__}"
    assert sent.get_header("X-sateais-client") == expected
    assert sent.get_header("User-agent") == expected
