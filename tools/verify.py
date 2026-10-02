#!/usr/bin/env python3
"""Restore, build, run offline tests and pack. Requires Python 3.10+ and .NET 10."""
from __future__ import annotations
import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import re

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ("Abstractions", "Core", "Mistral", "OpenAI")

def run(*args: str) -> None:
    print("+ " + " ".join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Release")
    parser.add_argument("--version", help="Stable release version applied to all projects and package checks")
    args = parser.parse_args()
    if args.version and not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", args.version):
        parser.error("--version must be a stable major.minor.patch version")
    properties = [f"-p:Version={args.version}"] if args.version else []
    if not args.version:
        import xml.etree.ElementTree as ET
        args.version = ET.parse(ROOT / "Directory.Build.props").findtext(".//Version")
    run(sys.executable, "tools/check_repository.py")
    if shutil.which("dotnet") is None:
        print("ERROR: .NET SDK 10 is required. No compilation, C# tests or packaging were performed.", file=sys.stderr)
        return 2
    run(sys.executable, "-m", "unittest", "discover", "-s", "tools/tests", "-v")
    run("dotnet", "--info")
    run("dotnet", "restore", "TypedDocumentAI.sln", *properties)
    run("dotnet", "build", "TypedDocumentAI.sln", "-c", args.configuration, "--no-restore", *properties)
    run("dotnet", "test", "tests/TypedDocumentAI.Tests/TypedDocumentAI.Tests.csproj",
        "-c", args.configuration, "--no-build", "--no-restore", "--logger", "trx",
        "--results-directory", str(ROOT / "artifacts/test-results"),
        "--settings", "tests/coverage.runsettings", "--collect", "XPlat Code Coverage", *properties)
    run("dotnet", "run", "--project", "samples/CustomProviderDemo", "-c", args.configuration,
        "--no-build", "--no-restore", *properties)
    output = ROOT / "artifacts/packages"
    output.mkdir(parents=True, exist_ok=True)
    # Remove only generated package files, not an arbitrary directory supplied by the caller.
    for pattern in ("*.nupkg", "*.snupkg"):
        for file in output.glob(pattern):
            file.unlink()
    for package in PACKAGES:
        run("dotnet", "pack", f"src/TypedDocumentAI.{package}/TypedDocumentAI.{package}.csproj",
            "-c", args.configuration, "--no-build", "--no-restore", "-o", str(output), *properties)
    run(sys.executable, "tools/check_packages.py", "--version", args.version)
    run(sys.executable, "tools/check_install.py", "--version", args.version)
    print("Verification completed. Offline tests and package checks passed; no external AI API was called.")
    return 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode)
