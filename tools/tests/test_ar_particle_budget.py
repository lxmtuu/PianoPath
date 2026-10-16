"""Synthetic contract/codec tests only; never used as native render evidence."""
import copy,gzip,hashlib,itertools,json,random,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_ar_particle_budget as r

def fixture():
    rows=[]
    expected={(a,p,v,ph,f,mode) for a,p,v,(ph,f),mode in r.plan()}
    expected|={(a['Id'],'prism','ar-reference','motion',f,mode) for a,f,mode in itertools.product(r.RECIPES[:2],range(29,45),('legacy','fair'))}
    for k in sorted(expected):
        recipe,palette,view,phase,frame,mode=k;traced=view!='live-stage' and mode!='fast'
        keys=([60]*300+[61]*300+[60+256]*300+[61+256]*300) if phase in ('chord-held','motion') else [60,61,60+256]
        keys=keys if traced else [];selected=r.select(keys) if mode=='fair' else list(range(min(192,len(keys))))
        rows.append(dict(Id=r.identifier(k),Recipe=recipe,Palette=palette,View=view,Phase=phase,Frame=frame,Mode=mode,File=r.identifier(k)+'.png',
            PixelSha256=('F' if mode=='fair' and len(keys)>192 else 'A')*64,RawHash='B'*64,PacketHash=('E' if mode=='fair' and len(keys)>192 else 'C')*64,
            KeyHash='D'*64,NonParticleHash='E'*64,EligibleHash='F'*64,SelectedHash='A'*64,RawParticles=1200,Packets=300,
            EligibleKeys=keys,Selected=selected,InventoryFile=r.identifier(k)+'.json.gz' if traced else None,InventoryHash='B'*64 if traced else None,DrawMilliseconds=1))
    return dict(schema=1,family='ar-particle-budget',reviewOnly=True,defaultEnabled=False,cap=192,width=640,height=360,fps=30,seed=20261016,
        sourceRevision='synthetic',baselineRevision=r.BASELINE,policy=r.POLICY,recipes=r.RECIPES,palettes=['prism','indigo'],notes=r.NOTES,
        checkpoints=[dict(Id=p,Frame=f) for p,f in r.CHECKPOINTS.items()],pedalOnFrame=24,pedalOffFrame=54,sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),captures=rows)

class BudgetTests(unittest.TestCase):
    def test_roles_pitches_borrowing_stable_order_and_under_cap(self):
        keys=[p+128*o for o,p,_ in itertools.product(range(3),range(48,56),range(50))];s=r.select(keys)
        self.assertEqual(192,len(s));self.assertEqual(s,sorted(set(s)))
        for o,p in itertools.product(range(3),range(48,56)):self.assertEqual(8,sum(keys[i]==p+o*128 for i in s))
        self.assertEqual([0,1,2],r.select([316,60,188]));self.assertEqual([],r.select(keys,0))
        s=r.select([188]+[60]*300+[317]*300);self.assertIn(0,s);self.assertEqual(192,len(s))
        rng=random.Random(20261016)
        for _ in range(100):
            keys=[rng.randrange(384) for _ in range(rng.randrange(1500))];cap=rng.randrange(257);before=keys.copy();s=r.select(keys,cap)
            self.assertEqual(min(cap,len(keys)),len(s));self.assertEqual(sorted(set(s)),s);self.assertEqual(before,keys)
    def test_complete_matrix_and_tamper_guards(self):
        m=fixture();rows=r.validate(m,'synthetic');self.assertEqual(904,len(rows));self.assertEqual(6,len(list(r.sheets(rows))));self.assertEqual(2,len(list(r.motions(rows))))
        def target(m):return next(c for c in m['captures'] if c['Mode']=='fair' and c['Phase']=='chord-held' and c['View']=='ar-reference')
        for change in (lambda m:m['captures'].pop(),lambda m:m.__setitem__('defaultEnabled',True),lambda m:m.__setitem__('cap',256),lambda m:m['sourceHashes'].clear(),
            lambda m:target(m)['Selected'].append(1199),lambda m:target(m).__setitem__('Selected',list(range(192))),lambda m:target(m).__setitem__('RawHash','F'*64),
            lambda m:target(m).__setitem__('NonParticleHash','F'*64),lambda m:target(m).__setitem__('File','../false.png'),lambda m:target(m).__setitem__('EligibleHash','A'*64)):
            x=copy.deepcopy(m);change(x)
            with self.assertRaises(ValueError):r.validate(x)
    def test_exact_published_stills_motion_and_tampering(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow needed for codec roundtrip')
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp);m=fixture();rows=r.validate(m);m['sheets']=list(r.sheets(rows));m['motions']=list(r.motions(rows))
            for s in m['sheets']:
                im=Image.new('RGBA',(440,320),(20,30,40,255))
                for p in s['panes']:p['hash']=r.digest(im.crop((p['x'],p['y'],p['x']+220,p['y']+160)))
                s['hash']=r.digest(im);im.save(path/s['file'])
            for t in m['motions']:
                frames=[Image.new('RGBA',(440,160),(20+i,30,40,255)) for i in range(16)]
                t['hashes']=[[r.digest(im.crop((x*220,0,(x+1)*220,160))) for x in range(2)] for im in frames]
                (path/t['file']).write_bytes(r.pack_frames(frames))
            (path/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(m).encode()))
            r.verify(path,published_only=True)
            t=m['motions'][0];data=bytearray((path/t['file']).read_bytes());data[len(data)//2]^=5;(path/t['file']).write_bytes(data)
            with self.assertRaises((ValueError,OSError,EOFError)):r.verify(path,published_only=True)
    def test_only_review_flag_controls_the_new_policy(self):
        root=Path(__file__).resolve().parents[2]
        look=(root/'Gpu/GpuLook.cs').read_text();layer=(root/'Gpu/GpuArLayer.Effects.cs').read_text()
        self.assertIn('ArFairParticleBudgetForReview { get; private set; }',look)
        self.assertNotIn('ArFairParticleBudgetForReview = s.',look)
        self.assertIn('private const int MaxProjectedParticles = 192',layer)
        self.assertIn('look.ArFairParticleBudgetForReview || ObserveBudgetForReview is not null',layer)
        self.assertIn('selected.Sort()', (root/'Gpu/ArParticleBudget.cs').read_text())
if __name__=='__main__':unittest.main()
