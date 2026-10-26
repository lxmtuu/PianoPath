import copy
import hashlib
import io
import json
import tempfile
import sys
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_flames_v3 as r
from test_wisps_v3 import pair


def flame_pair():
    _,b=pair();m=copy.deepcopy(b)
    m.update(schema=3,sceneCount=8,captureCount=64,wispV3ForReview=False,flameV3ForReview=True,flameIntensity=70)
    m['scenes']=[s for s in m['scenes'] if s['Family']=='flames']
    for s in m['scenes']:
        for c in s['captures']:
            if c['Particles']:c['PixelSha256']='B'*64
    return m,b


class FlamesV3Tests(unittest.TestCase):
    def test_complete_flames_matrix_matches_baseline_samples_and_geometry(self):
        m,b=flame_pair();self.assertEqual(24,len(r.shared.validate(m,b,'test-only',family='flames')))

    def test_family_flag_intensity_or_geometry_cannot_be_changed(self):
        for mutate in (lambda m:m.__setitem__('wispV3ForReview',True),lambda m:m.__setitem__('flameIntensity',80),
                       lambda m:m['scenes'][0]['captures'][7]['Geometry'].__setitem__('Hash','F'*64)):
            m,b=flame_pair();mutate(m)
            with self.assertRaises(ValueError):r.shared.validate(m,b,family='flames')

    def test_accepted_wisps_pixels_population_and_motion_are_pinned(self):
        m,_=pair();old=copy.deepcopy(m);motion=dict(captures=[dict(name='fake-contract',hashes=['A'*64])])
        r.retained(m,old,motion,motion)
        for key in ('PixelSha256','Coverage'):
            bad=copy.deepcopy(m)
            if key=='Coverage':bad['scenes'][0]['captures'][7][key]['RawHash']='F'*64
            else:bad['scenes'][0]['captures'][7][key]='F'*64
            with self.assertRaises(ValueError):r.retained(bad,old,motion,motion)
        bad=copy.deepcopy(motion);bad['captures'][0]['hashes']=['F'*64]
        with self.assertRaises(ValueError):r.retained(m,old,bad,motion)

    def test_flames_motion_must_declare_the_v3_law(self):
        # The static quartets keep v2 geometry for the light comparison, so the only evidence of the W2
        # motion is the clip manifest. A clip set that does not say which law drew it is not evidence.
        with self.assertRaisesRegex(ValueError,'v3 motion law'):
            r.paired(Path('.'),dict(captures=[]),require_motion_law=True)

    def test_paired_apng_keeps_every_native_half_pixel_and_timing(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow decoder is review-only')
        from make_held_particle_clips import assemble
        # Fake colours test the lossless wrapper only, NOT captured/visual evidence.
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);captures=[]
            for view in ('live-stage','ar'):
                for v in ('v2','v3'):
                    frames=[];hashes=[]
                    for f in range(60):
                        im=Image.new('RGBA',(140,160),(f,50 if v=='v2' else 90,70,255));b=io.BytesIO();im.save(b,format='PNG');frames.append(b.getvalue());hashes.append(hashlib.sha256(im.tobytes('raw','BGRA')).hexdigest().upper())
                    name=f'flames-{view}-{v}';(root/(name+'.png')).write_bytes(assemble(frames,30));captures.append(dict(name=name,hashes=hashes))
            m=dict(captures=captures);r.paired(root,m,build=True);r.paired(root,m)
            m['captures'][0]['hashes'][1]='F'*64
            with self.assertRaises(ValueError):r.paired(root,m)


if __name__=='__main__':unittest.main()
