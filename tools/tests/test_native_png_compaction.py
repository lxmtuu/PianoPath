import io,tempfile,unittest,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from compact_native_review_pngs import compact
class NativePngCompactionTests(unittest.TestCase):
    def test_opaque_pixels_and_second_run_are_identical(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow unavailable')
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'native.png';im=Image.new('RGBA',(21,17),(40,50,60,255));im.putpixel((2,4),(100,30,20,255));im.save(p,compress_level=0)
            expected=im.tobytes();result=compact(p);self.assertGreater(result['saved'],0)
            with Image.open(p) as decoded:self.assertEqual(expected,decoded.convert('RGBA').tobytes())
            self.assertEqual(0,compact(p)['saved'])
    def test_nonopaque_and_animated_inputs_are_not_rewritten(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow unavailable')
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'native.png';a=Image.new('RGBA',(3,3),(40,50,60,10));a.save(p);original=p.read_bytes()
            with self.assertRaisesRegex(ValueError,'Nonopaque'):compact(p)
            self.assertEqual(original,p.read_bytes())
            a=Image.new('RGBA',(3,3),(40,50,60,255));b=Image.new('RGBA',(3,3),(90,50,60,255));a.save(p,save_all=True,append_images=[b],duration=33);original=p.read_bytes()
            with self.assertRaisesRegex(ValueError,'animation'):compact(p)
            self.assertEqual(original,p.read_bytes())
