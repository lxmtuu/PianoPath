"""The native frame exception must not admit arbitrary gzip/diagnostics files."""
import sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import check_sources as check
class NativePreviewAllowlistTests(unittest.TestCase):
    def test_only_explicit_native_track_paths_allowed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder=root/'docs/previews'
            for name in ('confetti-art-v1/live-stage-held.bgra.gz','dust-art-v1/live-stage-burst.bgra.gz','dust-art-v2/ar-reference-held.bgra.gz','shatter-art-v1/live-stage-burst.bgra.gz','fireworks-art-v1/ar-reference-burst.bgra.gz','ok.png',
                         'shatter-billboard-v1/ar-near-overhead-held.bgra.gz',
                         'melt-art-v1/live-stage-burst.bgra.gz','melt-art-v1/live-stage-held.bgra.gz',
                         'melt-art-v1/ar-reference-burst.bgra.gz','melt-art-v1/ar-reference-held.bgra.gz',
                         'bounce-art-v1/live-stage-held.bgra.gz','bounce-art-v1/ar-reference-burst.bgra.gz',
                         'melt-art-v1/ar-near-overhead-held.bgra.gz','melt-art-v1/manifest.json.gz','random.gz','confetti-art-v1/manifest.json.gz','unknown/live-stage-held.bgra.gz'):
                p=folder/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'contract-fixture')
            with patch.object(check,'ROOT',root):errors=check.scan_previews_folder()
            self.assertEqual(5,len(errors))
            self.assertTrue(any('melt-art-v1/ar-near-overhead-held.bgra.gz' in e for e in errors))
            self.assertTrue(any('melt-art-v1/manifest.json.gz' in e for e in errors))
            self.assertTrue(any('random.gz' in e for e in errors))
            self.assertTrue(any('manifest.json.gz' in e for e in errors))
            self.assertTrue(any('unknown/' in e for e in errors))

    def test_release_group_has_only_twelve_bounded_tracks(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder=root/'docs/previews'
            allowed=[f'release-{family}-v1/{view}-{phase}.bgra.gz' for family in ('float-up','dissolve','smoke')
                for view in ('live-stage','ar-reference') for phase in ('burst','held')]
            denied=['release-smoke-v1/ar-oblique-held.bgra.gz','release-float-up-v1/manifest.json.gz',
                'release-dissolve-v2/live-stage-held.bgra.gz','release-other-v1/live-stage-burst.bgra.gz']
            for name in allowed+denied:
                p=folder/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'contract-fixture')
            with patch.object(check,'ROOT',root):errors=check.scan_previews_folder()
            self.assertEqual(4,len(errors))
            for name in denied:self.assertTrue(any(name in e for e in errors))

    def test_budget_group_has_only_two_bounded_temporal_slices(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder=root/'docs/previews'
            allowed=['ar-particle-budget-v1/embers-wisps.bgra.gz','ar-particle-budget-v1/splash-flames.bgra.gz']
            denied=['ar-particle-budget-v1/manifest.json.gz','ar-particle-budget-v1/fireworks-wisps.bgra.gz',
                'ar-particle-budget-v2/embers-wisps.bgra.gz','ar-particle-budget-v1/inventory.json.gz']
            for name in allowed+denied:
                p=folder/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'contract-fixture')
            with patch.object(check,'ROOT',root):errors=check.scan_previews_folder()
            self.assertEqual(4,len(errors))
            for name in denied:self.assertTrue(any(name in e for e in errors))
