#!/usr/bin/env python3
"""Manual, resumable GitHub release and NuGet publication. No remote CI dispatch."""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PACKAGES = ('Abstractions', 'Core', 'Mistral', 'OpenAI')
VERSION = r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)'
MARKER = re.compile(r'<!-- typeddocumentai-release: (.*?) -->')
ROOT = Path(__file__).resolve().parents[1]


def version_tuple(value: str) -> tuple[int, int, int]:
    if not re.fullmatch(VERSION, value):
        raise ValueError('Expected a stable major.minor.patch version.')
    return tuple(map(int, value.split('.')))


def next_version(tags: list[str], bump: str) -> str:
    if bump not in ('patch', 'minor', 'major'):
        raise ValueError('Unknown version increment.')
    versions = [version_tuple(t[1:]) for t in tags if re.fullmatch('v' + VERSION, t)]
    if not versions:
        return '1.0.0'
    major, minor, patch = max(versions)
    return {'patch': f'{major}.{minor}.{patch+1}', 'minor': f'{major}.{minor+1}.0',
            'major': f'{major+1}.0.0'}[bump]


def metadata(release: dict) -> dict | None:
    match = MARKER.search(release.get('body') or '')
    if not match:
        return None
    data = json.loads(match[1])
    version_tuple(data['version'])
    if not re.fullmatch(r'[0-9a-f]{40}', data['source_sha']):
        raise ValueError('Invalid release source commit.')
    if release['tag_name'] != 'v' + data['version']:
        raise ValueError('Release tag and saved version disagree.')
    return data


def select(releases: list[dict], bump: str, resume_tag: str, run_id: str, sha: str) -> dict:
    if resume_tag and not re.fullmatch('v' + VERSION, resume_tag):
        raise ValueError('resume_tag must be vMAJOR.MINOR.PATCH.')
    candidate = None
    pending = []
    for release in releases:
        data = metadata(release)
        if data and release['draft']:
            pending.append(release)
        if (resume_tag and release['tag_name'] == resume_tag) or (not resume_tag and data and data['run_id'] == run_id):
            candidate = release
    if candidate:
        data = metadata(candidate)
        if not data:
            raise ValueError('Only releases created by this workflow can be resumed.')
        if any(r['id'] != candidate['id'] for r in pending):
            raise ValueError('Another incomplete release must be resolved first.')
        return {**data, 'tag': candidate['tag_name'], 'resume': True,
                'completed': not candidate['draft'], 'release_id': candidate['id']}
    if resume_tag:
        raise ValueError('Requested release does not exist.')
    if pending:
        raise ValueError(f"Incomplete release {pending[0]['tag_name']}: rerun its workflow or set resume_tag.")
    stable = [r['tag_name'] for r in releases if not r['draft'] and not r.get('prerelease')]
    version = next_version(stable, bump)
    return {'version': version, 'tag': 'v'+version, 'source_sha': sha, 'run_id': run_id,
            'resume': False, 'completed': False, 'release_id': ''}


def gh(*args: str, data: dict | None = None) -> str:
    command = ['gh', *args]
    if data is not None:
        command += ['--input', '-']
    result = subprocess.run(command, input=json.dumps(data) if data is not None else None,
                            text=True, capture_output=True)
    if result.returncode:
        # API responses are not dumped: keep tokens, private error bodies and provider data out of logs.
        raise RuntimeError(f'GitHub operation failed: {args[0]} (exit {result.returncode}).')
    return result.stdout


def api(path: str, method: str = 'GET', data: dict | None = None):
    return json.loads(gh('api', '--method', method, path, data=data))


def repo() -> str:
    name = os.environ['GITHUB_REPOSITORY']
    if name != 'VivienMAN/TypedDocumentAI':
        raise ValueError('Publication is restricted to VivienMAN/TypedDocumentAI.')
    return f'repos/{name}'


def releases() -> list[dict]:
    pages = json.loads(gh('api', '--paginate', '--slurp', f'{repo()}/releases?per_page=100'))
    return [r for page in pages for r in page]


def emit(data: dict) -> None:
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as stream:
        for key, value in data.items():
            stream.write(f'{key}={str(value).lower() if isinstance(value, bool) else value}\n')


def plan(args) -> None:
    if os.environ.get('GITHUB_REF') != 'refs/heads/main':
        raise ValueError('Run this workflow from main.')
    if not os.environ.get('NUGET_USER', '').strip():
        raise ValueError('Create your NuGet account, configure Trusted Publishing, and set the NUGET_USER repository variable. See docs/RELEASING.md.')
    result = select(releases(), args.bump, args.resume_tag, os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_SHA'])
    if result['resume'] and not result['completed']:
        subprocess.run(['git', 'merge-base', '--is-ancestor', result['source_sha'], os.environ['GITHUB_SHA']], check=True)
    emit(result)
    print(f"Version {result['version']}; commit {result['source_sha']}; completed={result['completed']}.")


def filenames(version: str) -> set[str]:
    version_tuple(version)
    return {f'TypedDocumentAI.{p}.{version}.{extension}' for p in PACKAGES for extension in ('nupkg', 'snupkg')}


def check_files(directory: Path, version: str) -> None:
    actual = {p.name for p in directory.iterdir() if p.suffix in ('.nupkg', '.snupkg')}
    if actual != filenames(version):
        raise ValueError('Expected exactly four packages and four symbol packages at the selected version.')
    subprocess.run([sys.executable, str(ROOT/'tools/check_packages.py'), '--version', version,
                    '--directory', str(directory)], check=True)


def make_bundle(directory: Path, state: dict, target: Path) -> None:
    check_files(directory, state['version'])
    manifest = {**state, 'files': {name: hashlib.sha256((directory/name).read_bytes()).hexdigest()
                                 for name in sorted(filenames(state['version']))}}
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('manifest.json', json.dumps(manifest, indent=2)+'\n')
        for name in manifest['files']:
            archive.write(directory/name, name)


def unpack_bundle(bundle: Path, directory: Path, state: dict) -> dict:
    with zipfile.ZipFile(bundle) as archive:
        names = archive.namelist()
        expected = filenames(state['version']) | {'manifest.json'}
        if len(names) != len(expected) or set(names) != expected:
            raise ValueError('Invalid release bundle contents.')
        manifest = json.loads(archive.read('manifest.json'))
        for key in ('version', 'source_sha', 'run_id'):
            if manifest[key] != state[key]:
                raise ValueError('Release bundle identity mismatch.')
        if set(manifest['files']) != filenames(state['version']):
            raise ValueError('Invalid package manifest.')
        # Validate all data before writing any file; no archive paths are accepted.
        content = {name: archive.read(name) for name in manifest['files']}
        for name, value in content.items():
            if hashlib.sha256(value).hexdigest() != manifest['files'][name]:
                raise ValueError('Release bundle checksum mismatch.')
        directory.mkdir(parents=True, exist_ok=True)
        for name, value in content.items():
            (directory/name).write_bytes(value)
        (directory/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    return manifest


def download(tag: str, name: str, directory: Path) -> Path:
    gh('release', 'download', tag, '--repo', os.environ['GITHUB_REPOSITORY'],
       '--pattern', name, '--dir', str(directory))
    return directory/name


def ensure_tag(tag: str, sha: str) -> None:
    refs = api(f'{repo()}/git/matching-refs/tags/{tag}')
    exact = [r for r in refs if r['ref'] == f'refs/tags/{tag}']
    if exact:
        if exact[0]['object']['type'] != 'commit' or exact[0]['object']['sha'] != sha:
            raise ValueError('Existing tag points to a different commit; tags are never rewritten.')
    else:
        api(f'{repo()}/git/refs', 'POST', {'ref': f'refs/tags/{tag}', 'sha': sha})


def prepare(args) -> None:
    version_tuple(args.version)
    state = {'version': args.version, 'source_sha': args.source_sha, 'run_id': os.environ['GITHUB_RUN_ID']}
    tag = 'v'+args.version
    existing = next((r for r in releases() if r['tag_name'] == tag), None)
    if existing:
        stored = metadata(existing)
        if not stored or stored['source_sha'] != args.source_sha or not existing['draft']:
            raise ValueError('Cannot overwrite an existing release.')
        state = stored
    else:
        check_files(args.directory, args.version)
        ensure_tag(tag, args.source_sha)
        notes = api(f'{repo()}/releases/generate-notes', 'POST', {'tag_name': tag, 'target_commitish': args.source_sha})
        links = '\n'.join(f'- [TypedDocumentAI.{p}](https://www.nuget.org/packages/TypedDocumentAI.{p}/{args.version})' for p in PACKAGES)
        body = f'<!-- typeddocumentai-release: {json.dumps(state)} -->\n\n'+notes['body']+'\n\n## NuGet packages\n\n'+links
        existing = api(f'{repo()}/releases', 'POST', {'tag_name': tag, 'name': tag, 'body': body, 'draft': True, 'prerelease': False})
    args.directory.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temporary:
        temp = Path(temporary)
        if any(a['name'] == 'release-bundle.zip' for a in existing.get('assets', [])):
            bundle = download(tag, 'release-bundle.zip', temp)
        else:
            # No NuGet push can happen before this complete bundle has been saved on GitHub.
            bundle = temp/'release-bundle.zip'
            make_bundle(args.directory, state, bundle)
            gh('release', 'upload', tag, str(bundle), '--repo', os.environ['GITHUB_REPOSITORY'])
        manifest = unpack_bundle(bundle, args.directory, state)
    check_files(args.directory, args.version)
    asset_names = {a['name'] for a in existing.get('assets', [])}
    for name in sorted(filenames(args.version) | {'manifest.json'}):
        if name not in asset_names:
            gh('release', 'upload', tag, str(args.directory/name), '--repo', os.environ['GITHUB_REPOSITORY'])
        else:
            with tempfile.TemporaryDirectory() as temporary:
                saved = download(tag, name, Path(temporary)).read_bytes()
            if saved != (args.directory/name).read_bytes():
                raise ValueError(f'Existing release asset differs: {name}. No asset was replaced.')
    print(f"Saved immutable release bundle for {tag} ({len(manifest['files'])} packages).")


def package_payload(value: bytes) -> dict:
    """Compare ZIP contents, ignoring only NuGet repository signing additions."""
    with zipfile.ZipFile(io.BytesIO(value)) as archive:
        if len(archive.namelist()) != len(set(archive.namelist())):
            raise ValueError('Duplicate package ZIP entries.')
        payload = {}
        for name in archive.namelist():
            if name == '.signature.p7s':
                continue
            content = archive.read(name)
            if name == '[Content_Types].xml':
                tree = ET.fromstring(content)
                content = repr(sorted((child.tag, tuple(sorted(child.attrib.items()))) for child in tree
                                      if child.get('PartName') != '/.signature.p7s'
                                      and child.get('Extension') != 'p7s')).encode()
            payload[name] = hashlib.sha256(content).hexdigest()
        return payload


def remote_package(package: str, version: str) -> bytes | None:
    url = f'https://api.nuget.org/v3-flatcontainer/{package.lower()}/{version}/{package.lower()}.{version}.nupkg'
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise RuntimeError(f'NuGet package lookup failed (HTTP {error.code}).') from None


def same_package(remote: bytes, local: Path) -> None:
    if package_payload(remote) != package_payload(local.read_bytes()):
        raise ValueError(f'Existing NuGet package differs: {local.name}. Publication stopped.')


def push(path: Path, symbols: bool = False) -> subprocess.CompletedProcess:
    # Never print the command: it contains a temporary credential masked by NuGet/login.
    command = ['dotnet', 'nuget', 'push', str(path), '--api-key', os.environ['NUGET_API_KEY'],
               '--source', 'https://api.nuget.org/v3/index.json']
    command += ['--skip-duplicate'] if symbols else ['--no-symbols']
    return subprocess.run(command, capture_output=True, text=True)


def push_status(result: subprocess.CompletedProcess) -> int | None:
    """Extract only known HTTP status codes. Never echo credential-bearing tool output."""
    output = (result.stdout or '') + '\n' + (result.stderr or '')
    match = re.search(r'(?i)(?:status code[^\d\n]*|HTTP(?:/\d(?:\.\d)?)?\s+)(400|401|403|409|422)\b', output)
    return int(match.group(1)) if match else None


def push_error(package: str, result: subprocess.CompletedProcess, symbols: bool = False) -> RuntimeError:
    status = push_status(result)
    target = 'symbols' if symbols else 'package'
    if status in (401, 403):
        guidance = 'Check the NuGet Trusted Publishing account, repository/workflow/environment policy and package permissions.'
    elif status in (400, 422):
        guidance = 'NuGet rejected the package; inspect package metadata and validation requirements.'
    else:
        guidance = 'Check NuGet service availability and resume the same saved release.'
    code = f'HTTP {status}' if status is not None else f'exit {result.returncode}'
    return RuntimeError(f'{package}: {target} upload failed ({code}). {guidance} The release remains a draft.')


def publish(args) -> None:
    if not os.environ.get('NUGET_API_KEY'):
        raise ValueError('Missing short-lived NuGet credential.')
    # Always obtain the saved files again; a rerun must not publish newly rebuilt bytes.
    tag = 'v'+args.version
    release = next(r for r in releases() if r['tag_name'] == tag)
    state = metadata(release)
    if not state or not release['draft'] or state['source_sha'] != args.source_sha:
        raise ValueError('No matching pending release.')
    with tempfile.TemporaryDirectory() as temporary:
        temp = Path(temporary)
        bundle = download(tag, 'release-bundle.zip', temp)
        unpack_bundle(bundle, args.directory, state)
    check_files(args.directory, args.version)
    for short in PACKAGES:
        package = 'TypedDocumentAI.'+short
        local = args.directory/f'{package}.{args.version}.nupkg'
        remote = remote_package(package, args.version)
        if remote is not None:
            same_package(remote, local)
            print(f'{package}: existing NuGet content matches the saved package.')
        else:
            result = push(local)
            if result.returncode:
                # Poll conflicts inline; other failures require diagnosis before resuming.
                if push_status(result) != 409:
                    raise push_error(package, result)
                for _ in range(60):
                    remote = remote_package(package, args.version)
                    if remote is not None:
                        same_package(remote, local)
                        break
                    time.sleep(10)
                else:
                    raise RuntimeError(f'{package}: NuGet conflict (HTTP 409), but saved content could not be verified. The release remains a draft; resume after indexing.')
        result = push(args.directory/f'{package}.{args.version}.snupkg', symbols=True)
        if result.returncode:
            raise push_error(package, result, symbols=True)
        print(f'{package}: package and symbols submitted.')
    # Accepted uploads are not proof of availability: wait for all packages to be indexed.
    deadline = time.monotonic()+1200
    pending = set(PACKAGES)
    while pending and time.monotonic() < deadline:
        for short in sorted(pending):
            package = 'TypedDocumentAI.'+short
            remote = remote_package(package, args.version)
            if remote is not None:
                same_package(remote, args.directory/f'{package}.{args.version}.nupkg')
                pending.remove(short)
        if pending:
            time.sleep(15)
    if pending:
        raise RuntimeError('NuGet indexing is still pending. The release remains a draft; resume it later.')
    api(f"{repo()}/releases/{release['id']}", 'PATCH', {'draft': False, 'make_latest': 'true'})
    updated = api(f"{repo()}/releases/{release['id']}")
    if updated['draft']:
        raise RuntimeError('GitHub release publication could not be verified.')
    print(f'Published {updated["html_url"]}')
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as stream:
            stream.write(f'## Published {tag}\n\n[GitHub release]({updated["html_url"]})\n\n')
            for short in PACKAGES:
                stream.write(f'- [TypedDocumentAI.{short}](https://www.nuget.org/packages/TypedDocumentAI.{short}/{args.version})\n')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('plan')
    p.add_argument('--bump', choices=['patch', 'minor', 'major'], default='patch')
    p.add_argument('--resume-tag', default='')
    for name in ('prepare', 'publish'):
        p = commands.add_parser(name)
        p.add_argument('--version', required=True)
        p.add_argument('--source-sha', required=True)
        p.add_argument('--directory', type=Path, default=ROOT/'artifacts/packages')
    args = parser.parse_args()
    if args.command != 'plan':
        version_tuple(args.version)
        if not re.fullmatch(r'[0-9a-f]{40}', args.source_sha):
            parser.error('Invalid source commit.')
    globals()[args.command](args)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        raise SystemExit(1)
