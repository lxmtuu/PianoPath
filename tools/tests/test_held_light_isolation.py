import copy
import itertools
import sys
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_held_light_isolation as r


def rows(n):
    # Contract-only data, not captured/visual evidence.
    return [dict(Pitch=p,Count=n,Young=n,Middle=0,Older=0,MinAge=.1 if n else None,
                 MaxAge=.1 if n else None,MeanAge=.1 if n else None,MeanProgress=.1 if n else None) for p in r.PITCHES]


def plan():
    scenes=[]
    for (family,load),palette,view in itertools.product(r.LOADS,r.PALETTES,r.VIEWS):
        id_=f'{family}-{load}-{palette}-{view}';shots=[]
        for name,on,tint,spill in r.VARIANTS:
            normal=family=='wisps' and load=='normal';ar=view!='live-stage'
            eligible=76 if normal else 19;raw=80 if normal else 20;packed=48 if ar and normal else eligible
            cov=dict(Raw=rows(raw if on else 0),Eligible=rows(eligible if on else 0),Packed=rows(packed if on else 0),
                     RawHash='A'*64 if on else '0'*64,EligibleHash='B'*64 if on else '0'*64,
                     PackedHash=('C' if ar and normal else 'B')*64 if on else '0'*64)
            shots.append(dict(Variant=name,Particles=on,Tint=tint,Spill=spill,File=f'frames/{id_}/{name}.png',
                              PixelSha256='A'*64,StageKeys=0 if ar else 88,StageSprites=packed*4 if on and not ar else 0,
                              ArKeys=7 if ar and tint else 0,ArLights=22 if ar and spill else 0,
                              ArParticles=packed*4 if ar and on else 0,Coverage=cov))
        scenes.append(dict(Id=id_,Family=family,load=load,palette=palette,view=view,pitches=r.PITCHES[:],wispAmount=8 if load=='low' else 32,
                           crop=dict(X=0,Y=0,Width=384,Height=360),captures=shots,
                           sheets=[dict(particles=k==1,indices=list(range(k*4,k*4+4)),sheetFile=f"sheets/{id_}-{'on' if k else 'off'}.png",sheetPixelSha256='A'*64) for k in range(2)]))
    return dict(schema=2,captureCount=192,sceneCount=24,width=1280,height=720,fps=60,frames=36,seed=20261014,
                time=.6,pedal=False,heldArt=True,sourceRevision='test-only',sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),scenes=scenes)


class LightIsolationTests(unittest.TestCase):
    def test_full_plan_has_all_orthogonal_factors_and_subcap_controls(self):
        self.assertEqual(24,len(r.validate_plan(plan(),'test-only')))

    def test_wrong_revision_source_plan_or_duplicates_fail(self):
        with self.assertRaises(ValueError):r.validate_plan(plan(),'another-source')
        for mutate in (lambda m:m['sourceHashes'].clear(),lambda m:m.__setitem__('frames',35),
                       lambda m:m['scenes'].__setitem__(1,copy.deepcopy(m['scenes'][0])),
                       lambda m:m['scenes'][0].__setitem__('wispAmount',12)):
            m=plan();mutate(m)
            with self.assertRaises(ValueError):r.validate_plan(m)

    def test_factor_labels_or_quartet_order_cannot_be_swapped(self):
        for mutate in (lambda m:m['scenes'][0]['captures'][1].__setitem__('Spill',True),
                       lambda m:m['scenes'][0]['sheets'][0].__setitem__('indices',[0,2,1,3])):
            m=plan();mutate(m)
            with self.assertRaises(ValueError):r.validate_plan(m)

    def test_subcap_missing_pitch_or_particle_is_not_accepted(self):
        m=plan();s=next(s for s in m['scenes'] if s['load']=='low' and s['view']=='ar-reference')
        s['captures'][7]['Coverage']['PackedHash']='C'*64
        with self.assertRaises(ValueError):r.validate_plan(m)
        m=plan();s=next(s for s in m['scenes'] if s['load']=='low')
        s['captures'][7]['Coverage']['Packed'][0]=rows(0)[0]
        with self.assertRaises(ValueError):r.validate_plan(m)

    def test_stale_counters_or_tint_spill_coupling_fail(self):
        for mutate in (lambda m:m['scenes'][0]['captures'][0].__setitem__('ArLights',22),
                       lambda m:m['scenes'][1]['captures'][1].__setitem__('ArLights',22),
                       lambda m:m['scenes'][1]['captures'][2].__setitem__('ArKeys',7)):
            m=plan();mutate(m)
            with self.assertRaises(ValueError):r.validate_plan(m)

    def test_light_or_view_cannot_change_simulation_coverage(self):
        m=plan();m['scenes'][0]['captures'][5]['Coverage']['RawHash']='D'*64
        with self.assertRaises(ValueError):r.validate_plan(m)
        m=plan();m['scenes'][1]['captures'][4]['Coverage']['Raw'][0]['MaxAge']=.2
        with self.assertRaises(ValueError):r.validate_plan(m)

    def test_age_bins_nulls_boundaries_and_unknown_pitch_guard(self):
        for field,value in [('Young',-1),('Older',2),('MaxAge',1),('MeanProgress',float('nan')),('Pitch',62)]:
            data=rows(3);data[0][field]=value
            with self.assertRaises(ValueError):r.coverage_rows(data)
        data=rows(0);data[0]['MeanAge']=0
        with self.assertRaises(ValueError):r.coverage_rows(data)
        self.assertEqual(12,r.coverage_rows(rows(3)))

    def test_safe_paths_and_all_four_domains_are_required(self):
        m=plan();m['scenes'][0]['captures'][0]['File']='../out.png'
        with self.assertRaises(ValueError):r.validate_plan(m)
        m=plan();m['scenes'][0]['captures'][0]['Coverage']['Raw'].pop()
        with self.assertRaises(ValueError):r.validate_plan(m)

    def test_viewer_uses_decode_generation_and_error_guards(self):
        for token in ('await im.decode()','await sheet.decode()','if(gen!==generation)return',
                      'sheet.hidden=true;coverage.replaceChildren()',"catch(e){if(gen!==generation)return;sheet.hidden=true"):
            self.assertIn(token,r.TEMPLATE)


if __name__=='__main__':unittest.main()
