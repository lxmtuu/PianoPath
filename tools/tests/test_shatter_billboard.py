import copy,itertools,sys,tempfile,unittest,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_shatter_billboard as r

def row(id_,palette,view,direction,pitch,sample,variant,active):
    on=active and variant!='off';ar=view!='live-stage'
    return dict(Id=id_,Palette=palette,View=view,Direction=direction,Pitch=pitch,Sample=sample,Variant=variant,File=id_+'-'+variant+'.png',
        PixelSha256=('A' if not on else 'C' if ar and variant=='after' else 'B')*64,RawHash='D'*64,SelectedHash='E'*64,ColourHash='F'*64,
        GeometryHash=('B' if on and ar and variant=='after' else 'A')*64,UnchangedHash='1'*64,Count=int(on),MaxCornerError=0,Signal=int(on),VisiblePixels=int(on))
def plan():
    captures=[]
    for p in ('prism','indigo'):
        for v in r.VIEWS:
            for d,k,f,a in itertools.product(range(1 if v=='live-stage' else 4),(60,61),r.SAMPLES,r.VARIANTS):
                id_=f'{p}-{v}-d{d}-p{k}-f{f}';captures.append(row(id_,p,v,d,k,f,a,f!=89))
    motions=[]
    for v,p in itertools.product((r.VIEWS[1],r.VIEWS[3]),('burst','held')):
        id_=v+'-'+p;k,d=(60,0) if v=='ar-reference' else (61,2);tracks=[]
        for a in r.VARIANTS:
            frames=[row(id_,'prism',v,d,k,f,a,f<27) for f in range(60)]
            tracks.append(dict(variant=a,frames=frames,hashes=[x['PixelSha256'] for x in frames]))
        motions.append(dict(Id=id_,view=v,phase=p,pitch=k,direction=d,fps=30,count=60,pedalFrame=12,velocity=.45 if p=='held' else .9,
            crop=dict(X=1,Y=1,Width=10,Height=12),tracks=tracks))
    return dict(schema=1,family='shatter-billboard',reviewOnly=True,width=640,height=360,seed=20261014,staticFps=60,staticCount=624,lineage=64,sourceRevision='synthetic',
        samples=list(r.SAMPLES),sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),directions=[dict(TiltDegrees=t,AzimuthDegrees=a) for t,a in ((0,0),(45,-45),(90,90),(135,60))],captures=captures,motions=motions)
class ShatterBillboardTests(unittest.TestCase):
    def test_matrix_and_no_raster_empty_exceptions(self):
        self.assertEqual(208,len(r.validate(plan(),'synthetic')))
        m=plan();c=next(c for c in m['captures'] if c['View']=='ar-near-overhead' and c['Pitch']==61 and c['Sample']==10 and c['Variant']=='after')
        c['PixelSha256']='A'*64
        with self.assertRaisesRegex(ValueError,'raster-empty'):r.validate(m)
    def test_geometry_is_the_only_allowed_delta(self):
        for field,value in (('RawHash','2'*64),('SelectedHash','2'*64),('ColourHash','2'*64),('UnchangedHash','2'*64),('Count',2),('MaxCornerError',.00031),('MaxCornerError',float('nan')),('GeometryHash','A'*64)):
            m=plan();c=next(c for c in m['captures'] if c['Variant']=='after' and c['View']=='ar-reference' and c['Sample']==10);c[field]=value
            with self.assertRaises(ValueError):r.validate(m)
        m=plan();m['captures'][2]['PixelSha256']='C'*64
        with self.assertRaisesRegex(ValueError,'Stage pixels'):r.validate(m)
    def test_matrix_provenance_and_motion_contract(self):
        for change in (lambda m:m['captures'].pop(),lambda m:m['captures'].__setitem__(1,m['captures'][0]),lambda m:m['sourceHashes'].clear(),
            lambda m:m['captures'][0].__setitem__('File','../bad.png'),lambda m:m['samples'].__setitem__(1,35),lambda m:m['directions'][1].__setitem__('TiltDegrees',46),
            lambda m:m['motions'][0].__setitem__('pedalFrame',13),lambda m:m['motions'][0].__setitem__('velocity',1),lambda m:m['motions'][0]['tracks'][2]['frames'][2].__setitem__('ColourHash','2'*64),
            lambda m:m['motions'][0]['tracks'][2]['hashes'].pop(),lambda m:m['motions'][0]['crop'].__setitem__('Width',10000)):
            m=plan();change(m)
            with self.assertRaises(ValueError):r.validate(m)
    def test_scope_source_and_default_off_gate(self):
        root=Path(__file__).resolve().parents[2];source=(root/'Gpu/GpuArLayer.Effects.cs').read_text()
        self.assertIn('!legacyAxes && ShatterParticleAppearance.Eligible(look.ShatterBillboardForReview && look.ImpactMorph=="Shatter"',source)
        self.assertIn('BillboardAxes(faceX,faceY,particle.Age,particle.Phase',source)
        self.assertIn('halfY = size * .42;',source)
        self.assertNotIn('ShatterBillboardForReview { get; private set; } = true',(root/'Gpu/GpuLook.cs').read_text())
        native=(root/'Diagnostics/ShatterBillboardReview.cs').read_text()
        self.assertIn('CheckActualCorners(a)',native);self.assertIn('lineage!=64',native);self.assertIn('sample!=89 && after.Record.Signal==0',native)
    def test_viewer_labels_do_not_inherit_stage_tracks_or_release_input(self):
        import gzip,contextlib,io
        from unittest.mock import patch
        import serve_shatter_billboard as s
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);repo=root/'repo';output=root/'viewer';evidence=repo/'docs/evidence/shatter-billboard-v1';evidence.mkdir(parents=True)
            (evidence/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(plan()).encode()))
            for name in s.NAMES:
                p=repo/'docs/previews/shatter-billboard-v1'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'contract-only')
            with patch.object(s.review,'verify',return_value=plan()),patch.object(s.review,'paired_frames',return_value=iter(())),contextlib.redirect_stdout(io.StringIO()):
                s.build(repo,output)
            player=(output/'motion-review.html').read_text()
            self.assertNotIn('live-stage-held',player);self.assertNotIn('live-stage-burst',player)
            self.assertNotIn('Thả phím: frame 12',player);self.assertNotIn('Giữ phím · pedal off',player)
            for label in ('AR near-overhead · nhấn nhẹ','AR reference · nhấn mạnh','Pedal on: frame 12','không held/release','cùng V1 material + billboard phải'):
                self.assertIn(label,player)

    def test_exact_native_codec_publication_and_tamper(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow required')
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);m=plan()
            # Synthetic contract fixtures only, NOT native art/render evidence.
            for c in m['captures']:
                im=Image.new('RGBA',(640,360),(20,30,40,255))
                if c['Count']:im.putpixel((295,140),(110 if c['Variant']=='after' and c['View']!='live-stage' else 150,70,30,255))
                im.save(root/c['File']);c['PixelSha256']=r.digest(im)
            for t in m['motions']:
                for v in t['tracks']:
                    for f,c in enumerate(v['frames']):
                        im=Image.new('RGBA',(10,12),(20,30,40,255))
                        if c['Count']:im.putpixel((f%10,f%12),(110 if v['variant']=='after' else 150,70,30,255))
                        path=root/'motion'/t['Id']/v['variant']/f'{f:03}.png';path.parent.mkdir(parents=True,exist_ok=True);im.save(path);v['hashes'][f]=r.digest(im)
            (root/'manifest.json').write_text(json.dumps(m));m=r.verify(root,revision='synthetic');self.assertEqual(2,len(m['sheets']))
            (root/'manifest.json').unlink();self.assertEqual(4,len(r.verify(root,published_only=True)['motions']))
            t=m['motions'][0];p=root/t['pairedFile'];raw=p.read_bytes();p.write_bytes(raw[:-5])
            with self.assertRaises((ValueError,EOFError)):r.verify(root,published_only=True)
            p.write_bytes(raw);sheet=m['sheets'][0];im=Image.open(root/sheet['file']).convert('RGBA');im.putpixel((8,8),(255,0,0,255));im.save(root/sheet['file']);sheet['hash']=r.digest(im)
            (root/'manifest.json').write_text(json.dumps(m))
            with self.assertRaisesRegex(ValueError,'pane changed'):r.verify(root,published_only=True)
if __name__=='__main__':unittest.main()
