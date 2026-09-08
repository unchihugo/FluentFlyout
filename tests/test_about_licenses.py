"""Regression coverage for the licenses shown in the About page."""

import re
from pathlib import Path


ABOUT_VIEW_MODEL = (
    Path(__file__).parents[1] / "FluentFlyoutWPF" / "ViewModels" / "AboutViewModel.cs"
)
LICENSE_ENTRY = re.compile(r"new LicenseInfo\s*\{(?P<body>.*?)\n\s*\},", re.DOTALL)
FIELD = re.compile(r'(?P<name>Name|Version|License|Url)\s*=\s*"(?P<value>[^"]*)"')


def about_licenses() -> list[dict[str, str]]:
    source = ABOUT_VIEW_MODEL.read_text(encoding="utf-8")
    return [dict(FIELD.findall(match.group("body"))) for match in LICENSE_ENTRY.finditer(source)]


def test_about_lists_the_forked_wpf_ui_package() -> None:
    licenses = about_licenses()

    assert {
        "Name": "unchihugo.WPF-UI",
        "Version": "4.4.1",
        "License": "MIT",
        "Url": "https://github.com/unchihugo/wpfui",
    } in licenses
    assert not any(license_info.get("Name") == "WPF-UI" for license_info in licenses)
