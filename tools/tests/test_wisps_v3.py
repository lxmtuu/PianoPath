import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_wisps_v3 as r
from test_held_light_isolation import plan


def pair():
    # Contract fixtures only, NOT captured particles/pixels or visual evidence.
    b=plan()
    for s in b['scenes']:
        s['cameraSha256']=None if s['view']=='live-stage' else 'A'*64
        s['comparisons']=[dict(id=f'particles-{k}',fullFrame=dict(OverFourPixels=10)) for k in range(4)]
        for c in s['captures']:
            n=sum(x['Count'] for x in c['Coverage']['Packed'])
            c['Geometry']=dict(Count=n,Hash='A'*64,MinWidth=3 if n else None,MaxWidth=3 if n else None,MeanWidth=3 if n else None,
                MinHeight=10 if n else None,MaxHeight=10 if n else None,MeanHeight=10 if n else None,CenterYMin=1 if n else None,CenterYMax=40 if n else None)
    m=copy.deepcopy(b);m.update(schema=3,sceneCount=8,captureCount=64,wispV3ForReview=True)
    m['scenes']=[s for s in m['scenes'] if s['Family']=='wisps' and s['load']=='low']
    for s in m['scenes']:
        for c in s['captures']:
            if c['Particles']:c['PixelSha256']='B'*64
    return m,b


class WispsV3Tests(unittest.TestCase):
    def test_complete_pairs_are_subcap_and_geometry_identical(self):
        m,b=pair();self.assertEqual(24,len(r.validate(m,b,'test-only')))

    def test_missing_matrix_revision_or_density_fails(self):
        for mutate in (lambda m:m['scenes'].pop(),lambda m:m.__setitem__('sourceRevision','wrong'),
                       lambda m:m['scenes'][0].__setitem__('wispAmount',32)):
            m,b=pair();mutate(m)
            with self.assertRaises(ValueError):r.validate(m,b)

    def test_no_simulation_or_size_change_is_hidden_in_material_candidate(self):
        for mutate in (lambda c:c['Coverage'].__setitem__('RawHash','C'*64),
                       lambda c:c['Geometry'].__setitem__('Hash','C'*64),
                       lambda c:c['Geometry'].__setitem__('MeanHeight',9)):
            m,b=pair();mutate(m['scenes'][0]['captures'][7])
            with self.assertRaises(ValueError):r.validate(m,b)

    def test_active_signal_and_unchanged_off_controls_required(self):
        for k,value in [(0,'B'*64),(7,'A'*64)]:
            m,b=pair();m['scenes'][0]['captures'][k]['PixelSha256']=value
            with self.assertRaises(ValueError):r.validate(m,b)
        m,b=pair();m['scenes'][0]['comparisons'][0]['fullFrame']['OverFourPixels']=0
        with self.assertRaises(ValueError):r.validate(m,b)

    def test_dimensions_are_finite_and_empty_is_not_zero_size(self):
        m,b=pair()
        for key,value in [('MeanHeight',float('nan')),('MinWidth',0),('CenterYMax',-10)]:
            g=copy.deepcopy(m['scenes'][0]['captures'][7]['Geometry']);g[key]=value
            with self.assertRaises(ValueError):r.geometry(g,76)
        g=m['scenes'][0]['captures'][0]['Geometry'];g['MeanWidth']=0
        with self.assertRaises(ValueError):r.geometry(g,0)

    def test_retained_v2_pixel_hashes_cannot_be_refreshed_silently(self):
        m,b=pair()
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'prior.json';p.write_text(json.dumps(b));r.historical_guard(b,p)
            b['scenes'][0]['captures'][7]['PixelSha256']='C'*64
            with self.assertRaises(ValueError):r.historical_guard(b,p)

    def test_paired_viewer_decode_and_generation_guards(self):
        for token in ('await Promise.all(imgs.map(im=>im.decode()))','await Promise.all([before.decode(),after.decode()])',
                      'if(gen!==generation)return','before.hidden=after.hidden=true',"geometry.textContent=''"):
            self.assertIn(token,r.TEMPLATE)


if __name__=='__main__':unittest.main()
