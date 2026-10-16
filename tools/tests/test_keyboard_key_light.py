"""Synthetic contract tests only; Windows captures, not these pixels, are visual evidence."""
import copy,hashlib,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_keyboard_key_light as r

def fixture():
    rows=[]
    for e,s,t,p,v in sorted(r.plan()):
        mean=20 if s=='ar' else 20+p+(15 if e=='gpu' and v=='legacy' and p<25 else 0)
        id_=f'{e}-{s}-{t}-p{p}-{v}'
        rows.append(dict(Id=id_,Engine=e,Scene=s,State=t,Percent=p,Variant=v,File=id_+'.png',Width=640,Height=360 if e=='gpu' else 128,
            PixelSha256=hashlib.sha256((e+s+t+str(mean)).encode()).hexdigest().upper(),RawHash='A'*64,PacketHash='B'*64,KeyboardMean=mean))
    return dict(schema=1,family='keyboard-key-light',levels=list(r.LEVELS),gpuWidth=640,gpuHeight=360,softwareWidth=640,softwareHeight=128,
        curve=r.CURVE,sourceRevision='synthetic',sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),captures=rows)

class KeyboardKeyLightTests(unittest.TestCase):
    def test_complete_native_contract(self):self.assertEqual(154,len(r.validate(fixture(),'synthetic')))

    def test_response_reference_is_continuous_monotone_and_preserves_usual_settings(self):
        gain=lambda t:t*(4.05-5.6*t) if t<.25 else .35+1.25*t
        self.assertEqual(0,gain(0));last=0
        for i in range(1,20001):
            value=gain(i/10000);self.assertGreater(value,last);last=value
        self.assertAlmostEqual(2.85,last)
        self.assertAlmostEqual(gain(.25-1e-8),gain(.25+1e-8),places=6)
        for t in (.25,.5,.7,.92,1,2):self.assertEqual(.35+1.25*t,gain(t))
        root=Path(__file__).resolve().parents[2]
        shader=(root/'Gpu/StageShaders.hlsl').read_text()
        self.assertIn('KeyA.x * (4.05 - 5.6 * KeyA.x)',shader)
        self.assertIn('float softbox = 0.35 + 1.25 * KeyA.x',shader)
        self.assertIn('if (NoteD.y > 1.5) softbox = 0.0',shader)
        self.assertIn('NoteD.y < 0.5 && KeyA.x < 0.25',shader)
        self.assertIn('look.KeyboardSoftboxReferenceForReview',(root/'Gpu/GpuStageRenderer.cs').read_text())

    def test_missing_reference_pixel_drift_false_ramp_or_other_source_change_is_rejected(self):
        def row(m,e='gpu',s='isolated',p=0,v='fixed'):return next(c for c in m['captures'] if (c['Engine'],c['Scene'],c['State'],c['Percent'],c['Variant'])==(e,s,'idle',p,v))
        for change in (lambda m:m['captures'].pop(),lambda m:row(m).__setitem__('PixelSha256','C'*64),
            lambda m:row(m,p=25).__setitem__('PixelSha256','C'*64),lambda m:row(m,p=10).__setitem__('KeyboardMean',0),
            lambda m:row(m,s='ar',p=100).__setitem__('PixelSha256','C'*64),lambda m:row(m).__setitem__('PacketHash','C'*64),
            lambda m:row(m).__setitem__('File','../false.png'),lambda m:row(m).__setitem__('KeyboardMean',float('nan')),
            lambda m:m['sourceHashes'].clear(),lambda m:m.__setitem__('curve','just subtract the floor')):
            m=fixture();change(m)
            with self.assertRaises(ValueError):r.validate(m)
        with self.assertRaises(ValueError):r.validate(fixture(),'not-the-native-source')

    def test_exact_native_crop_publication_and_tamper_guards(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow needed for image round trip')
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder);m=fixture()
            for c in m['captures']:
                im=Image.new('RGBA',(c['Width'],c['Height']),(20,20,20,255));v=c['KeyboardMean']
                box=(0,240,640,360) if c['Engine']=='gpu' else (0,0,640,128)
                im.paste((v,v,v,255),box);c['PixelSha256']=r.digest(im);im.save(path/c['File'])
            (path/'manifest.json').write_text(json.dumps(m))
            r.verify(path,revision='synthetic')
            (path/'manifest.json').unlink();m=r.verify(path,published_only=True)
            self.assertEqual(10,len(m['sheets']))
            # Viewer-derived pairs/ramps remain exact crops of the publication (not render evidence).
            from unittest.mock import patch
            import shutil,serve_keyboard_key_light as viewer
            repo=path/'repo';e=repo/'docs/evidence/keyboard-key-light-v1';v=repo/'docs/previews/keyboard-key-light-v1'
            e.mkdir(parents=True);v.mkdir(parents=True)
            shutil.copyfile(path/'manifest.json.gz',e/'manifest.json.gz')
            for sheet in m['sheets']:shutil.copyfile(path/sheet['file'],v/sheet['file'])
            with patch.object(viewer.review,'verify',return_value=m):viewer.build(repo,path/'viewer')
            for sheet in m['sheets']:
                if '-ar-' in sheet['file']:continue
                pair=Image.open(path/'viewer/pairs'/sheet['file'])
                for i,pane in enumerate(sheet['panes'][:2]):
                    self.assertEqual(pane['hash'],r.digest(pair.crop((i*220,0,(i+1)*220,pane['height']))))
            page=(path/'viewer/index.html').read_text()
            self.assertNotIn('__DATA__',page);self.assertIn('Ép đèn tắt',page);self.assertIn('gen!==generation',page)

            changed=copy.deepcopy(m);changed['sheets'][0]['panes'][0]['box']=[0,0,220,120]
            (path/'manifest.json').write_text(json.dumps(changed))
            with self.assertRaisesRegex(ValueError,'provenance'):r.verify(path,published_only=True)
            (path/'manifest.json').unlink();sheet=m['sheets'][0]
            im=Image.open(path/sheet['file']).convert('RGBA');im.putpixel((0,0),(255,0,0,255));im.save(path/sheet['file'])
            with self.assertRaisesRegex(ValueError,'Sheet changed'):r.verify(path,published_only=True)

if __name__=='__main__':unittest.main()
