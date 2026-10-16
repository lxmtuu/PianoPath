"""Synthetic fixtures validate accounting/guards, never presented as native art evidence."""
import copy,gzip,hashlib,itertools,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_key_particle_ensemble as r

def fixture():
    rows=[]
    for recipe,palette,light,view,phase,variant in itertools.product((x['Id'] for x in r.RECIPES),r.PALETTES,r.LIGHTS,r.VIEWS,r.CHECKPOINTS,r.VARIANTS):
        k=(recipe,palette,light,view,phase,variant);active=variant!='off' and phase!='rest'
        population=[dict(Pitch=60,Origin=1 if phase=='release-pedal' else 2,Route=3 if phase=='release-pedal' else 0,Count=1)] if active else []
        raw=hashlib.sha256((recipe+palette+phase+str(active)).encode()).hexdigest().upper()
        rows.append(dict(Id=r.identifier(k),Recipe=recipe,Palette=palette,Light=light,View=view,Phase=phase,Frame=r.CHECKPOINTS[phase],Variant=variant,File=r.identifier(k)+'.png',
            PixelSha256='A'*64,RawHash=raw,GeometryHash='B'*64,PacketHash='C'*64,KeyHash='D'*64,RawParticles=int(active),Packets=int(active),Population=population,
            ArSelected=population if view=='ar-reference' else [],Signal=int(active)*30,VisiblePixels=0,ClippedDeltaPixels=0,StageNotes=int(view=='live-stage' and phase!='rest'),ArLiveBars=int(view=='ar-reference' and phase!='rest')))
    return dict(schema=1,family='key-particle-ensemble',reviewOnly=True,width=640,height=360,fps=30,seed=20261016,legacyParticleAxesForReview=False,
        sourceRevision='synthetic',sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),recipes=r.RECIPES,notes=r.NOTES,checkpoints=[dict(Id=k,Frame=v) for k,v in r.CHECKPOINTS.items()],
        variants=r.VARIANTS,lights=r.LIGHTS,palettes=r.PALETTES,pedalOnFrame=24,pedalOffFrame=54,materialPolicy=r.POLICY,captures=rows)

class EnsembleTests(unittest.TestCase):
    def test_complete_matrix_and_publication_subset(self):
        m=fixture();rows=r.validate(m,'synthetic');self.assertEqual(1120,len(rows))
        sheets=list(r.sheets(rows));self.assertEqual(6,len(sheets));self.assertEqual(24,sum(len(s['panes']) for s in sheets))
        self.assertEqual({'mixed','front-control'},{rows[r.key(c)]['Variant'] for c in rows.values() if any(c['Id']==p['id'] for s in sheets for p in s['panes'])})
    def test_incomplete_forged_policy_state_geometry_pedal_and_metrics_rejected(self):
        def row(m):return next(c for c in m['captures'] if c['Variant']=='mixed' and c['Phase']=='release-pedal' and c['View']=='ar-reference' and c['Light']==100)
        for tamper in (lambda m:m['captures'].pop(),lambda m:m['sourceHashes'].clear(),lambda m:m.__setitem__('pedalOffFrame',36),
            lambda m:m.__setitem__('materialPolicy','promote everything'),lambda m:row(m).__setitem__('GeometryHash','F'*64),
            lambda m:row(m).__setitem__('RawHash','F'*64),lambda m:row(m).__setitem__('PixelSha256','F'*64),
            lambda m:row(m).__setitem__('Signal',float('nan')),lambda m:row(m).__setitem__('File','../false.png'),
            lambda m:row(m)['Population'][0].__setitem__('Count',2),lambda m:row(m).__setitem__('ClippedDeltaPixels',1)):
            m=copy.deepcopy(fixture());tamper(m)
            with self.assertRaises(ValueError):r.validate(m)
        with self.assertRaises(ValueError):r.validate(fixture(),'wrong-source')
    def test_pixels_metrics_publication_and_tamper_roundtrip(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow required for pixel tests')
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp);m=fixture()
            # Only two unique fake images; hardlinks bound IO while exercising every original path.
            files={}
            for c in m['captures']:
                active=c['RawParticles']>0;im=Image.new('RGBA',(640,360),(20,20,20,255))
                if active:im.putpixel((0,0),(30,30,30,255))
                c['PixelSha256']=r.digest(im);c['Signal']=30 if active else 0;c['VisiblePixels']=int(active)
                dest=path/c['File']
                if active in files:dest.hardlink_to(files[active])
                else:im.save(dest);files[active]=dest
            (path/'manifest.json').write_text(json.dumps(m));out=r.verify(path)
            (path/'manifest.json').unlink();r.verify(path,published_only=True)
            out['sheets'][0]['panes'][0]['box']=[0,0,220,160]
            (path/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(out).encode()))
            with self.assertRaisesRegex(ValueError,'provenance'):r.verify(path,published_only=True)
    def test_production_materials_and_geometry_not_silently_promoted(self):
        root=Path(__file__).resolve().parents[2]
        source=(root/'Diagnostics/KeyParticleEnsembleReview.cs').read_text()
        for flag in ('WithWispV3ForReview(art)','WithFlameV3ForReview(art)','WithReleaseArtForReview(art)','WithReleaseFrontLayerForReview(variant=="front-control")'):
            self.assertIn(flag,source)
        for flag in ('WithMelt','WithBounce','WithShatter','WithSpark','WithSplash','WithDust','WithConfetti','WithFirework'):
            self.assertNotIn(flag,source)
        self.assertNotIn('LegacyParticleAxesForReview=true',source)
        self.assertIn('feed.LiveNote(n.Pitch,false,n.Strength)',source)
        self.assertIn('feed.Impact(n.Pitch,n.Strength)',source)
if __name__=='__main__':unittest.main()
