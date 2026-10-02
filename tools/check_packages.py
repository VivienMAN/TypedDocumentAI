#!/usr/bin/env python3
"""Check real dotnet-pack outputs. Does not create or simulate NuGet packages."""
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile
import argparse
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--version", default=ET.parse(ROOT / "Directory.Build.props").findtext(".//Version"))
parser.add_argument("--directory", type=Path, default=ROOT / "artifacts/packages")
args = parser.parse_args()
version = args.version
packages = ("Abstractions", "Core", "Mistral", "OpenAI")
expected = {f"TypedDocumentAI.{name}" for name in packages}
output = args.directory
errors = []
for package in sorted(expected):
    path = output / f"{package}.{version}.nupkg"
    symbols = output / f"{package}.{version}.snupkg"
    if not path.exists():
        errors.append(f"Missing actual package: {path.name}")
        continue
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        specs = [name for name in names if name.endswith(".nuspec")]
        if len(specs) != 1:
            errors.append(f"Expected one nuspec in {path.name}")
            continue
        spec = ET.fromstring(archive.read(specs[0]))
        metadata = spec.find("{*}metadata")
        if metadata is None:
            errors.append(f"Missing metadata in {path.name}")
            continue
        def value(tag):
            element = metadata.find("{*}" + tag)
            return element.text if element is not None else None
        if value("id") != package or value("version") != version:
            errors.append(f"Identity mismatch in {path.name}")
        if value("license") != "MIT":
            errors.append(f"Missing MIT expression in {path.name}")
        if "README.md" not in names or value("readme") != "README.md":
            errors.append(f"README not packed in {path.name}")
        short = package.removeprefix("TypedDocumentAI.")
        if "README.md" in names and archive.read("README.md") != (ROOT / "docs/nuget" / f"{short}.md").read_bytes():
            errors.append(f"Wrong package-specific README in {path.name}")
        if value("authors") != "VivienMAN and TypedDocumentAI contributors" or not value("copyright"):
            errors.append(f"Missing author/copyright metadata in {path.name}")
        if value("releaseNotes") != f"https://github.com/VivienMAN/TypedDocumentAI/releases/tag/v{version}":
            errors.append(f"Wrong release notes URL in {path.name}")
        if value("icon") != "icon.png" or "icon.png" not in names:
            errors.append(f"Missing package icon in {path.name}")
        else:
            icon = archive.read("icon.png")
            if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (128, 128) or icon[25] != 6:
                errors.append(f"Icon must be a 128x128 RGBA PNG in {path.name}")
        if f"lib/net10.0/{package}.dll" not in names:
            errors.append(f"Missing compiled library in {path.name}")
        if f"lib/net10.0/{package}.xml" not in names:
            errors.append(f"Missing XML API documentation in {path.name}")
        repository = metadata.find("{*}repository")
        if repository is None or repository.get("url") != "https://github.com/VivienMAN/TypedDocumentAI":
            errors.append(f"Missing repository URL in {path.name}")
        commit = repository.get("commit", "") if repository is not None else ""
        if not re.fullmatch(r"[0-9a-f]{40}", commit):
            errors.append(f"Missing repository commit in {path.name}")
        for dependency in metadata.findall(".//{*}dependency"):
            dependency_id = dependency.get("id", "")
            if dependency_id.startswith("TypedDocumentAI.") and dependency_id not in expected:
                errors.append(f"Unknown internal dependency: {dependency_id}")
            if dependency_id in expected and dependency.get("version") not in (version, f"[{version}, )", f"[{version}]"):
                errors.append(f"Wrong internal dependency version in {path.name}: {dependency_id}")
    if not symbols.exists():
        errors.append(f"Missing symbol package: {symbols.name}")
    else:
        with zipfile.ZipFile(symbols) as archive:
            pdbs = [name for name in archive.namelist() if name.endswith(".pdb")]
            if not pdbs:
                errors.append(f"Missing portable PDB in {symbols.name}")
            for name in pdbs:
                pdb = archive.read(name)
                source_url = f"https://raw.githubusercontent.com/VivienMAN/TypedDocumentAI/{commit}/".encode()
                if not pdb.startswith(b"BSJB") or source_url not in pdb:
                    errors.append(f"Missing portable PDB Source Link commit URL in {symbols.name}")
if errors:
    raise SystemExit("\n".join(errors))
print("Checked four NuGet packages: metadata, per-package READMEs, icon, XML docs, symbols and Source Link.")
