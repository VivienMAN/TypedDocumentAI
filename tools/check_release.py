#!/usr/bin/env python3
"""Validate that a release tag exactly matches the repository's package version."""
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
version = os.environ.get("PACKAGE_VERSION") or ET.parse(root / "Directory.Build.props").findtext(".//Version") or ""
tag = sys.argv[1] if len(sys.argv) == 2 else os.environ.get("RELEASE_TAG", "")
if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?", version):
    raise SystemExit("Invalid package version.")
if tag != "v" + version:
    raise SystemExit(f"Release tag must exactly equal v{version}.")
print(f"Validated release tag: {tag}")
