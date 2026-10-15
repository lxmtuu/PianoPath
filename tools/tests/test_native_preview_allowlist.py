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
            for name in ('confetti-art-v1/live-stage-held.bgra.gz','fireworks-art-v1/ar-reference-burst.bgra.gz','ok.png',
                         'random.gz','confetti-art-v1/manifest.json.gz','unknown/live-stage-held.bgra.gz'):
                p=folder/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'contract-fixture')
            with patch.object(check,'ROOT',root):errors=check.scan_previews_folder()
            self.assertEqual(3,len(errors))
            self.assertTrue(any('random.gz' in e for e in errors))
            self.assertTrue(any('manifest.json.gz' in e for e in errors))
            self.assertTrue(any('unknown/' in e for e in errors))
