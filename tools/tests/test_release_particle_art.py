import copy
import hashlib
import itertools
import json
import sys
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_release_particle_art as r


def plan():
    captures=[]
    for palette,view,pitch,phase,variant in itertools.product(('prism','indigo'),r.VIEWS,(60,61),r.PHASES,('off','classic','art')):
        id_=f'{palette}-{view}-p{pitch}-{phase}';active=variant!='off' and phase!='expired';visible=active
        captures.append(dict(Id=id_,Palette=palette,View=view,Pitch=pitch,Phase=phase,Variant=variant,File=f'{id_}-{variant}.png',
            ColourHash='1'*64,PixelSha256=('A' if not visible else 'B' if variant=='classic' else 'C')*64,RawHash='D'*64,PacketHash='E'*64,
            RawParticles=int(active),PackedParticles=int(active)))
    motions=[]
    for view,phase in itertools.product(r.VIEWS[:2],('burst','held')):
        tracks=[]
        for variant in ('off','classic','art'):
            tracks.append(dict(variant=variant,hashes=[('A' if variant=='off' or not 12<=f<45 else 'B' if variant=='classic' else 'C')*64 for f in range(60)],
                frames=[dict(ColourHash='1'*64,RawHash='D'*64,PacketHash='E'*64,RawParticles=int(variant!='off' and 12<=f<45),PackedParticles=int(variant!='off' and 12<=f<45),fullPixelSha256='F'*64) for f in range(60)]))
        motions.append(dict(emission='physical Note Off f12; pedal '+('on' if phase=='held' else 'off')+'; no impact/held emitter',Id=f'{view}-{phase}',view=view,phase=phase,fps=30,count=60,releaseFrame=12,crop=dict(X=1,Y=1,Width=10,Height=12),tracks=tracks))
    controls=[dict(c,Variant='material-only',File=c['Id']+'-material-only.png') for c in captures if c['View']=='live-stage' and c['Variant']=='art']
    pedal=[dict(c,Variant='pedal-off',File=c['Id']+'-pedal-off.png') for c in captures if c['Variant']=='art']
    pre=[]
    for c in captures:
        if c['Variant']=='art' and c['Phase']=='burst':
            pre.append({k:dict(c,Variant=v,File=c['Id']+'-'+v+'.png',RawParticles=0,PackedParticles=0,PixelSha256='A'*64) for k,v in (('Candidate','before-release'),('Off','before-off'))})
    return dict(pedalControls=pedal,preReleaseControls=pre,stageDepthControls=controls,stageDepthTrial='candidate Stage depth .01; material-only control depth .6; AR unchanged',schema=4,releaseOffsets=[2,10,26,89],staticSampleFrames=[23,31,47,110],motionPressVelocities=[.9,.9],family='release-float-up',reviewOnly=True,legacyParticleAxesForReview=False,sourceRevision='synthetic',width=640,height=360,
        seed=20261014,captureCount=192,staticFps=60,staticReleaseFrame=21,sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),captures=captures,motions=motions)


class ReleaseArtTests(unittest.TestCase):
    def test_rejects_held_release_recipe_masquerading_as_single_press(self):
        for field,value in (('schema',3),('staticSampleFrames',[2,35,35,149]),('motionPressVelocities',[.9,.45])):
            m=plan();m[field]=value
            with self.assertRaises(ValueError):r.validate(m)

    def test_matrix(self):self.assertEqual(64,len(r.validate(plan(),'synthetic')))

    def test_every_active_candidate_requires_native_off_delta(self):
        m=plan()
        for c in m['captures']:
            if c['Variant']=='art' and c['Phase']!='expired':
                bad=copy.deepcopy(m)
                next(x for x in bad['captures'] if x['File']==c['File'])['PixelSha256']='A'*64
                with self.assertRaisesRegex(ValueError,'off-delta'):r.validate(bad)

    def test_removed_candidate_is_not_accepted_as_a_material_change(self):
        m=plan();m['captures'][2]['PixelSha256']=m['captures'][0]['PixelSha256']
        with self.assertRaisesRegex(ValueError,'off-delta'):r.validate(m)
        m=plan();m['motions'][0]['tracks'][2]['hashes']=m['motions'][0]['tracks'][0]['hashes'][:]
        with self.assertRaisesRegex(ValueError,'Blank release'):r.validate(m)

    def test_full_motion_pedal_state_held_emission_and_expiration_are_guarded(self):
        changes=(lambda m:m['motions'][0]['tracks'][2]['frames'][13].__setitem__('ColourHash','2'*64),
            lambda m:[t['tracks'][2]['frames'][0].__setitem__('RawParticles',1) for t in m['motions']],
            lambda m:[t['tracks'][2]['frames'][12].__setitem__('PackedParticles',0) for t in m['motions']],
            lambda m:[t['tracks'][2]['frames'][46].update(RawParticles=1,PackedParticles=1) for t in m['motions']],
            lambda m:[t['tracks'][2]['frames'][59].update(RawParticles=1,PackedParticles=1) for t in m['motions']])
        for change in changes:
            m=plan();change(m)
            with self.assertRaises(ValueError):r.validate(m)

    def test_release_sources_are_distinct_and_note_off_is_real(self):
        root=Path(__file__).resolve().parents[2];producer=(root/'Diagnostics/ReleaseParticleArtReview.cs').read_text()
        for family in ('float-up','dissolve','smoke'):self.assertIn('"'+family+'"',producer)
        self.assertIn('if(f==0)feed.LiveNote(pitch,true,.9)',producer)
        self.assertIn('if(f==release)feed.LiveNote(pitch,false,.9)',producer)
        self.assertNotIn('feed.Impact(',producer)
        self.assertIn('f<release?new HashSet<int>{pitch}',producer)
        self.assertIn('frontLayer:false',producer)
        self.assertEqual(21,len(r.SOURCES))

    def test_pedal_and_pre_release_cannot_be_ignored_or_mislabeled(self):
        for change in (lambda m:m['pedalControls'].pop(),lambda m:m['pedalControls'][0].__setitem__('PacketHash','2'*64),
            lambda m:m['preReleaseControls'].pop(),lambda m:m['preReleaseControls'][0]['Candidate'].__setitem__('RawParticles',1),
            lambda m:m['preReleaseControls'][0]['Off'].__setitem__('PixelSha256','2'*64),
            lambda m:m['motions'][0].__setitem__('emission','held emission'),lambda m:m.__setitem__('family','melt')):
            m=plan();change(m)
            with self.assertRaises(ValueError):r.validate(m)
        for family in ('release-float-up','release-dissolve','release-smoke'):
            m=plan();m['family']=family;self.assertEqual(64,len(r.validate(m)))

    def test_rejects_provenance_geometry_and_timeline_drift(self):
        for change in (lambda m:m['captures'].pop(),lambda m:m['captures'].__setitem__(1,m['captures'][0]),
            lambda m:m['captures'][2].__setitem__('PacketHash','F'*64),lambda m:m['captures'][2].__setitem__('RawHash','F'*64),
            lambda m:m['captures'][2].__setitem__('File','../bad.png'),lambda m:m['captures'][0].__setitem__('PackedParticles',1),
            lambda m:m['motions'][0].__setitem__('releaseFrame',13),lambda m:m['motions'][0]['tracks'][2]['frames'][3].__setitem__('RawHash','F'*64),
            lambda m:m['captures'][2].__setitem__('RawParticles',float('nan')),lambda m:m['motions'][0]['tracks'][1]['frames'][0].__setitem__('PackedParticles',-1),
            lambda m:m['stageDepthControls'].pop(),lambda m:m['stageDepthControls'][0].__setitem__('ColourHash','2'*64),lambda m:m['sourceHashes'].clear(),lambda m:m.__setitem__('legacyParticleAxesForReview',True)):
            m=plan();change(m)
            with self.assertRaises(ValueError):r.validate(m)
        with self.assertRaises(ValueError):r.validate(plan(),'other')

    def test_v2_requires_identical_actual_colour_payloads_across_outputs(self):
        m=plan();m['schema']=4
        for c in m['captures']:c['ColourHash']='1'*64
        for t in m['motions']:
            for v in t['tracks']:
                for f in v['frames']:f['ColourHash']='1'*64
        self.assertEqual(64,len(r.validate(m)))
        for field in ('ColourHash','RawHash','PackedParticles'):
            bad=copy.deepcopy(m)
            c=next(c for c in bad['captures'] if c['Variant']=='art' and c['View']=='ar-reference' and c['Phase']=='held')
            c[field]='2'*64 if field!='PackedParticles' else 0
            with self.assertRaises(ValueError):r.validate(bad)

    def test_native_crop_and_lossless_frame_bundle_round_trip_then_tamper(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow is review-only')
        # Synthetic contract pixels, never render evidence.
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);m=plan()
            for c in m['captures']:
                im=Image.new('RGBA',(640,360),(20,30,40,255))
                if c['PackedParticles']:
                    im.putpixel((295,140),(150 if c['Variant']=='classic' else 110,70,30,255))
                im.save(root/c['File']);c['PixelSha256']=r.digest(im)
            for c in m['stageDepthControls']:
                a=next(x for x in m['captures'] if x['Id']==c['Id'] and x['Variant']=='art')
                (root/c['File']).write_bytes((root/a['File']).read_bytes());c['PixelSha256']=a['PixelSha256']
            for c in m['pedalControls']:
                a=next(x for x in m['captures'] if x['Id']==c['Id'] and x['Variant']=='art')
                (root/c['File']).write_bytes((root/a['File']).read_bytes());c['PixelSha256']=a['PixelSha256']
            for pair in m['preReleaseControls']:
                for c in pair.values():
                    a=next(x for x in m['captures'] if x['Id']==c['Id'] and x['Variant']=='off')
                    (root/c['File']).write_bytes((root/a['File']).read_bytes());c['PixelSha256']=a['PixelSha256']
            for t in m['motions']:
                for v in t['tracks']:
                    for f in range(60):
                        im=Image.new('RGBA',(10,12),(20,30,40,255))
                        if v['variant']!='off' and 12<=f<45:im.putpixel((f%10,f%12),(150 if v['variant']=='classic' else 110,70,30,255))
                        file=root/'motion'/t['Id']/v['variant']/f'{f:03}.png';file.parent.mkdir(parents=True,exist_ok=True);im.save(file);v['hashes'][f]=r.digest(im)
            (root/'manifest.json').write_text(json.dumps(m));r.verify(root,revision='synthetic')
            published=r.verify(root,published_only=True);self.assertEqual(4,len(published['motions']))
            t=published['motions'][0]
            decoded=list(r.paired_frames(root,t));self.assertEqual(60,len(decoded))
            self.assertEqual((20,12),decoded[59].size)
            self.assertEqual((30,60),(t['fps'],t['count']))
            import gzip
            bundle=root/t['pairedFile'];original=bundle.read_bytes()
            for broken in (gzip.compress(b''),gzip.compress(gzip.decompress(original)+b'x')):
                bundle.write_bytes(broken)
                with self.assertRaisesRegex(ValueError,'bundle length'):list(r.paired_frames(root,t))
            bundle.write_bytes(original)
            # Exercise schema-2 geometry/colour split and published gzip loading too.
            published['schema']=4
            for c in published['captures']:c['ColourHash']='1'*64
            for track in published['motions']:
                for variant in track['tracks']:
                    for f in variant['frames']:f['ColourHash']='1'*64
            (root/'manifest.json').write_text(json.dumps(published));r.verify(root,revision='synthetic')
            (root/'manifest.json').unlink();published=r.verify(root,published_only=True)
            self.assertEqual(4,published['schema'])
            sheet=published['sheets'][0];im=Image.open(root/sheet['file']).convert('RGBA');im.putpixel((8,8),(255,0,0,255));im.save(root/sheet['file'])
            sheet['hash']=r.digest(im);(root/'manifest.json').write_text(json.dumps(published))
            with self.assertRaisesRegex(ValueError,'pane changed'):r.verify(root,published_only=True)

    def test_delta_animation_preserves_alpha_changes_and_identical_tail(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow is review-only')
        import io
        frames=[Image.new('RGBA',(3,4),(25,50,75,255)) for _ in range(60)]
        frames[4].putpixel((1,1),(25,50,75,20))
        with Image.open(io.BytesIO(r.assemble_delta(frames))) as im:
            self.assertEqual(60,im.n_frames)
            for f,source in enumerate(frames):im.seek(f);self.assertEqual(r.digest(source),r.digest(im))

    def test_group_http_handler_cannot_mix_family_frames(self):
        import contextlib,io,threading,urllib.request,urllib.error
        from functools import partial
        from http.server import ThreadingHTTPServer
        import serve_release_particle_art as viewer
        class Quiet(viewer.Handler):
            def log_message(self,*args):pass
        Quiet.families={family:{'live-stage-held':[(family+'/'+str(n)).encode() for n in range(60)]} for family in viewer.FAMILIES}
        with tempfile.TemporaryDirectory() as folder:
            server=ThreadingHTTPServer(('127.0.0.1',0),partial(Quiet,directory=folder))
            thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
            try:
                base='http://127.0.0.1:'+str(server.server_port)
                for family in viewer.FAMILIES:
                    self.assertEqual((family+'/14').encode(),urllib.request.urlopen(base+'/'+family+'/motion/live-stage-held/14.png').read())
                for path in ('/smoke/motion/live-stage-held/60.png','/smoke/motion/ar-reference-held/14.png','/unknown/motion/live-stage-held/14.png','/smoke/motion/live-stage-held/-1.png'):
                    with self.assertRaises(urllib.error.HTTPError):urllib.request.urlopen(base+path)
            finally:server.shutdown();thread.join();server.server_close()


if __name__=='__main__':unittest.main()
