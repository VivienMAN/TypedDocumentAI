#!/usr/bin/env python3
"""Install all four generated packages from a local feed and run the offline demo."""
import argparse
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', default=ET.parse(ROOT/'Directory.Build.props').findtext('.//Version'))
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix='typeddocumentai-consumer-') as temporary:
    consumer = Path(temporary)
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': 'net10.0', 'ImplicitUsings': 'enable',
                       'Nullable': 'enable', 'TreatWarningsAsErrors': 'true', 'NuGetAudit': 'true',
                       'NuGetAuditMode': 'all'}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    for name in ('Abstractions', 'Core', 'Mistral', 'OpenAI'):
        ET.SubElement(items, 'PackageReference', Include='TypedDocumentAI.'+name, Version=args.version)
    ET.ElementTree(project).write(consumer/'Consumer.csproj', encoding='unicode')
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local', value=str(ROOT/'artifacts/packages'))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(config, 'packageSourceMapping')
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='local'), 'package', pattern='TypedDocumentAI.*')
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget.org'), 'package', pattern='*')
    ET.ElementTree(config).write(consumer/'NuGet.config', encoding='unicode')
    (consumer/'Program.cs').write_text((ROOT/'samples/CustomProviderDemo/Program.cs').read_text())
    # A fresh cache guarantees that installation uses the packages just generated, including transitive references.
    subprocess.run(['dotnet', 'restore', '--configfile', str(consumer/'NuGet.config'),
                    '--packages', str(consumer/'packages')], cwd=consumer, check=True)
    subprocess.run(['dotnet', 'run', '--no-restore', '-c', 'Release'], cwd=consumer, check=True)
print('Local-feed installation and offline consumer passed for all four packages.')
