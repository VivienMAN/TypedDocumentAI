"""Offline release regressions: no GitHub, NuGet or paid provider requests."""
import io
from contextlib import redirect_stdout
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import shutil
import subprocess
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release

SHA = 'a'*40


def candidate(version='1.0.0', draft=True, run='10'):
    data = {'version': version, 'source_sha': SHA, 'run_id': run}
    return {'id': 1, 'tag_name': 'v'+version, 'draft': draft, 'prerelease': False,
            'body': '<!-- typeddocumentai-release: '+json.dumps(data)+' -->'}


class ReleaseTests(unittest.TestCase):
    def test_initial_and_semver_increments(self):
        for bump in ('patch', 'minor', 'major'):
            self.assertEqual('1.0.0', release.next_version([], bump))
        tags = ['v1.9.9', 'v1.10.2', 'v2.0.0-preview.1', 'unrelated']
        for bump, expected in [('patch', '1.10.3'), ('minor', '1.11.0'), ('major', '2.0.0')]:
            self.assertEqual(expected, release.next_version(tags, bump))

    def test_invalid_versions_and_resume_inputs(self):
        for value in ('01.0.0', '1.0', '1.0.0-preview.1', '1.0.0\nx=y'):
            with self.assertRaises(ValueError):
                release.version_tuple(value)
        with self.assertRaises(ValueError):
            release.select([], 'patch', 'v1.0.0\n', '11', SHA)

    def test_pending_release_blocks_new_version(self):
        with self.assertRaisesRegex(ValueError, 'Incomplete release'):
            release.select([candidate()], 'patch', '', '11', SHA)

    def test_rerun_and_explicit_resume_keep_original_version_and_source(self):
        for run, resume in [('10', ''), ('11', 'v1.0.0')]:
            state = release.select([candidate()], 'major', resume, run, 'b'*40)
            self.assertEqual(('1.0.0', SHA, '10'), (state['version'], state['source_sha'], state['run_id']))
            self.assertTrue(state['resume'])

    def test_completed_rerun_is_noop_but_new_dispatch_increments(self):
        saved = candidate(draft=False)
        self.assertTrue(release.select([saved], 'patch', '', '10', SHA)['completed'])
        self.assertEqual('1.0.1', release.select([saved], 'patch', '', '11', SHA)['version'])

    def test_missing_or_unmanaged_resume_is_rejected(self):
        with self.assertRaises(ValueError):
            release.select([], 'patch', 'v1.0.0', '11', SHA)
        saved = candidate()
        saved['body'] = ''
        with self.assertRaises(ValueError):
            release.select([saved], 'patch', 'v1.0.0', '11', SHA)

    def test_branch_and_missing_nuget_account_fail_before_remote_calls(self):
        for ref, user, message in [('refs/heads/feature', 'person', 'main'), ('refs/heads/main', '', 'NUGET_USER')]:
            with patch.dict(os.environ, {'GITHUB_REF': ref, 'NUGET_USER': user}), patch.object(release, 'releases') as remote:
                args = type('Args', (), {'bump': 'patch', 'resume_tag': ''})()
                with self.assertRaisesRegex(ValueError, message):
                    release.plan(args)
                remote.assert_not_called()

    def test_bundle_restores_original_bytes_and_rejects_tampering(self):
        state = {'version': '1.0.0', 'source_sha': SHA, 'run_id': '10'}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root/'source'
            source.mkdir()
            for name in release.filenames('1.0.0'):
                (source/name).write_bytes(name.encode())
            bundle = root/'release-bundle.zip'
            with patch.object(release, 'check_files'):
                release.make_bundle(source, state, bundle)
            restored = root/'restored'
            release.unpack_bundle(bundle, restored, state)
            for name in release.filenames('1.0.0'):
                self.assertEqual((source/name).read_bytes(), (restored/name).read_bytes())
            with self.assertRaises(ValueError):
                release.unpack_bundle(bundle, root/'wrong', {**state, 'source_sha': 'b'*40})
            with zipfile.ZipFile(bundle) as archive:
                contents = {name: archive.read(name) for name in archive.namelist()}
            contents[next(iter(release.filenames('1.0.0')))] = b'tampered'
            with zipfile.ZipFile(bundle, 'w') as archive:
                for name, value in contents.items():
                    archive.writestr(name, value)
            with self.assertRaisesRegex(ValueError, 'checksum'):
                release.unpack_bundle(bundle, root/'tampered', state)
            self.assertFalse((root/'tampered').exists())

    def test_zip_traversal_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bundle = root/'bad.zip'
            with zipfile.ZipFile(bundle, 'w') as archive:
                archive.writestr('../outside', b'bad')
            with self.assertRaises(ValueError):
                release.unpack_bundle(bundle, root/'out', {'version': '1.0.0'})
            self.assertFalse((root/'outside').exists())

    def test_nuget_repository_signature_is_ignored_but_payload_changes_fail(self):
        def package(signed=False, dll=b'original'):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, 'w') as archive:
                archive.writestr('lib/net10.0/library.dll', dll)
                signature = '<Default Extension="p7s" ContentType="signature"/>' if signed else ''
                archive.writestr('[Content_Types].xml', '<Types><Default Extension="dll" ContentType="binary"/>'+signature+'</Types>')
                if signed:
                    archive.writestr('.signature.p7s', b'repository signature')
            return stream.getvalue()
        self.assertEqual(release.package_payload(package()), release.package_payload(package(True)))
        self.assertNotEqual(release.package_payload(package()), release.package_payload(package(True, b'changed')))

    def test_publish_uses_saved_bundle_and_only_finalizes_after_verification(self):
        for outcome in ('success', 'mismatch', 'symbol_failure', 'push_failure'):
            with self.subTest(outcome=outcome), tempfile.TemporaryDirectory() as temporary, redirect_stdout(io.StringIO()):
                root = Path(temporary)
                source = root/'source'
                source.mkdir()
                for name in release.filenames('1.0.0'):
                    with zipfile.ZipFile(source/name, 'w') as archive:
                        archive.writestr('content', name.encode())
                bundle = root/'original.zip'
                state = {'version': '1.0.0', 'source_sha': SHA, 'run_id': '10'}
                with patch.object(release, 'check_files'):
                    release.make_bundle(source, state, bundle)
                target = root/'packages'
                target.mkdir()
                # Rebuilt output must be replaced by the bundle before publication.
                for name in release.filenames('1.0.0'):
                    (target/name).write_bytes(b'newly rebuilt bytes')
                def saved(tag, name, directory):
                    path = directory/name
                    shutil.copyfile(bundle, path)
                    return path
                def remote(package, version):
                    if outcome == 'push_failure':
                        return None
                    if outcome == 'mismatch':
                        return (source/'TypedDocumentAI.Core.1.0.0.nupkg').read_bytes()
                    return (source/f'{package}.{version}.nupkg').read_bytes()
                args = type('Args', (), {'version': '1.0.0', 'source_sha': SHA, 'directory': target})()
                completed = {'draft': False, 'html_url': 'https://github.com/VivienMAN/TypedDocumentAI/releases/tag/v1.0.0'}
                with patch.dict(os.environ, {'NUGET_API_KEY': 'test-only', 'GITHUB_STEP_SUMMARY': '', 'GITHUB_REPOSITORY': 'VivienMAN/TypedDocumentAI'}), \
                     patch.object(release, 'releases', return_value=[candidate()]), \
                     patch.object(release, 'download', side_effect=saved), \
                     patch.object(release, 'check_files'), \
                     patch.object(release, 'remote_package', side_effect=remote), \
                     patch.object(release.time, 'sleep'), \
                     patch.object(release, 'push', return_value=subprocess.CompletedProcess([], int(outcome in ('symbol_failure', 'push_failure')))) as push, \
                     patch.object(release, 'api', return_value=completed) as api:
                    if outcome == 'success':
                        release.publish(args)
                        self.assertEqual(4, push.call_count)
                        self.assertTrue(all(call.kwargs['symbols'] for call in push.call_args_list))
                        self.assertEqual(2, api.call_count)
                    else:
                        with self.assertRaises((ValueError, RuntimeError)):
                            release.publish(args)
                        api.assert_not_called()
                    for name in release.filenames('1.0.0'):
                        self.assertEqual((source/name).read_bytes(), (target/name).read_bytes())


if __name__ == '__main__':
    unittest.main()
