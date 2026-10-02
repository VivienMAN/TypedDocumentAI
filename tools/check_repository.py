#!/usr/bin/env python3
"""Offline structure checks only. This script is NOT a C# parser, compiler or test runner."""
from __future__ import annotations
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ERRORS: list[str] = []

def expect(condition: bool, message: str) -> None:
    if not condition:
        ERRORS.append(message)

def main() -> int:
    projects = sorted(ROOT.glob("src/**/*.csproj")) + sorted(ROOT.glob("tests/**/*.csproj")) + sorted(ROOT.glob("samples/**/*.csproj"))
    expect(len(projects) == 8, "Expected four libraries, one test project and three sample projects.")
    central = ET.parse(ROOT / "Directory.Packages.props")
    dependencies = {node.attrib["Include"]: node.attrib["Version"] for node in central.findall(".//PackageVersion")}
    expect(len(dependencies) == len(central.findall(".//PackageVersion")), "Duplicate centrally managed package.")
    props = ET.parse(ROOT / "Directory.Build.props")
    expect(props.findtext(".//TargetFramework") == "net10.0", "Unexpected target framework.")
    expect(props.findtext(".//TreatWarningsAsErrors") == "true", "Warnings must be errors.")
    expect(props.findtext(".//NuGetAudit") == "true", "NuGet audit must remain enabled.")
    json.loads((ROOT / "global.json").read_text())
    for xml in (ROOT / "NuGet.config", ROOT / "tests/coverage.runsettings"):
        ET.parse(xml)
    solution = (ROOT / "TypedDocumentAI.sln").read_text()
    graph: dict[Path, list[Path]] = {}
    for project in projects:
        tree = ET.parse(project)
        rel = project.relative_to(ROOT)
        expect(str(rel).replace("/", "\\") in solution, f"Project missing from solution: {rel}")
        refs = []
        for ref in tree.findall(".//ProjectReference"):
            target = (project.parent / ref.attrib["Include"].replace("\\", "/")).resolve()
            expect(target.exists(), f"Broken project reference in {rel}: {target}")
            refs.append(target)
        graph[project.resolve()] = refs
        for ref in tree.findall(".//PackageReference"):
            name = ref.attrib["Include"]
            expect(name in dependencies, f"Unversioned dependency: {name}")
            expect("Version" not in ref.attrib, f"Use central version management for {name}")
        if rel.parts[0] == "src":
            expect(tree.findtext(".//IsPackable") == "true", f"Library is not packable: {rel}")
            expect(tree.findtext(".//GenerateDocumentationFile") == "true", f"XML docs disabled: {rel}")
    def visit(node: Path, active: set[Path], done: set[Path]) -> None:
        if node in active:
            ERRORS.append(f"Project reference cycle: {node.name}")
            return
        if node in done:
            return
        active.add(node)
        for child in graph.get(node, []):
            visit(child, active, done)
        active.remove(node)
        done.add(node)
    done: set[Path] = set()
    for project in graph:
        visit(project, set(), done)
    abstractions = ROOT / "src/TypedDocumentAI.Abstractions/TypedDocumentAI.Abstractions.csproj"
    expect(not ET.parse(abstractions).findall(".//PackageReference"), "Abstractions must remain dependency-free.")
    core = ROOT / "src/TypedDocumentAI.Core/TypedDocumentAI.Core.csproj"
    expect(all(".Mistral" not in str(p) and ".OpenAI" not in str(p) for p in graph[core.resolve()]), "Core cannot depend on provider implementations.")
    needed = ["README.md", "README.fr.md", "LICENSE", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md", "CODE_OF_CONDUCT.md",
              "docs/ARCHITECTURE.md", "docs/MIGRATING-V1.md", "docs/EXTENDING.md", "docs/TESTING.md", "docs/RELEASING.md",
              "docs/UPSTREAM-CONTRACTS.md", "docs/VALIDATION.md", ".github/workflows/ci.yml", ".github/workflows/release.yml"]
    for file in needed:
        expect((ROOT / file).is_file(), f"Missing repository file: {file}")
    # Relative documentation links must resolve. External URLs and in-page anchors are intentionally ignored.
    for file in list(ROOT.glob("*.md")) + list((ROOT / "docs").rglob("*.md")):
        for link in re.findall(r"\]\(([^)]+)\)", file.read_text()):
            if "://" in link:
                url = urlsplit(link)
                prefix = "/VivienMAN/TypedDocumentAI/blob/main/"
                if url.netloc == "github.com" and url.path.startswith(prefix):
                    target = unquote(url.path.removeprefix(prefix))
                    expect((ROOT / target).is_file(), f"Broken repository URL in {file.name}: {link}")
                continue
            if link.startswith("#") or link.startswith("mailto:"):
                continue
            expect(file.parent != ROOT / "docs/nuget", f"NuGet README link must be absolute: {file.name}: {link}")
            target = link.split("#", 1)[0]
            expect((file.parent / target).exists(), f"Broken Markdown link in {file.name}: {link}")
    for workflow in (ROOT / ".github/workflows").glob("*.yml"):
        text = workflow.read_text()
        expect("pull_request_target" not in text, f"Unsafe PR execution trigger: {workflow.name}")
        for action in re.findall(r"uses:\s*([^\s#]+)", text):
            if action.startswith("./"):
                continue
            expect(bool(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}", action)), f"Unpinned action: {action}")
    source_files = sorted(p for p in ROOT.glob("src/**/*.cs") if not {"obj", "bin"}.intersection(p.relative_to(ROOT).parts))
    tests = sorted(p for p in ROOT.glob("tests/**/*.cs") if not {"obj", "bin"}.intersection(p.relative_to(ROOT).parts))
    expect(bool(source_files) and bool(tests), "Sources and tests must both exist.")
    for file in source_files:
        text = file.read_text()
        expect("NotImplementedException" not in text, f"Unimplemented public feature: {file.name}")
    fixture = ROOT / "samples/InvoiceConsole/Fixtures/invoice.png"
    expect(fixture.exists() and fixture.read_bytes().startswith(b"\x89PNG\r\n\x1a\n"), "Missing synthetic PNG fixture.")
    facts = sum(len(re.findall(r"\[Fact\]", p.read_text())) for p in tests)
    theories = sum(len(re.findall(r"\[Theory\]", p.read_text())) for p in tests)
    rows = sum(len(re.findall(r"\[InlineData\(", p.read_text())) for p in tests)
    if ERRORS:
        print("\n".join(ERRORS), file=sys.stderr)
        return 1
    print(f"Repository structure OK: {len(projects)} projects; {len(source_files)} library source files.")
    print(f"Authored xUnit inventory: {facts} facts, {theories} theories, {rows} inline-data rows. NOT EXECUTED.")
    print("No C# compilation, package restore, vulnerability audit or live API test was performed by this checker.")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
