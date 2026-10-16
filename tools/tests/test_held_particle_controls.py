import copy
import hashlib
import itertools
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_held_particle_controls as review


def plan():
    # Contract fixture only; these are not captured/rendered pixels.
    sources=['Gpu/StageShaders.hlsl','Gpu/GpuStageRenderer.cs','Gpu/GpuStageSimulation.cs','Gpu/GpuStageSimulation.Effects.cs',
             'Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs','Gpu/GpuLook.cs','Gpu/GpuStageFeed.cs','Gpu/HeldParticleAppearance.cs',
             'Camera/ArCameraPose.cs','Camera/ArParticleBillboard.cs','Stage/PianoStage.cs','Stage/PrismFlowPalette.cs',
             'Diagnostics/HeldParticleControlPlan.cs','Diagnostics/HeldParticleControlReview.cs',
             'Diagnostics/AuxParticleFixtures.cs','Diagnostics/KeyParticleFixtures.cs','docs/samples/ar-keyboard-topdown.png',
             'Diagnostics/HeldLightIsolationPlan.cs']
    scenes=[]
    for family,palette,pattern,view in itertools.product(review.FAMILIES,review.PALETTES,review.PATTERNS,review.VIEWS):
        name='-'.join((family,palette,pattern,view))
        scenes.append({'Id':name,'Family':family,'palette':palette,'pattern':pattern,'view':view,
            'pitches':review.PATTERNS[pattern][:],'crop':{'X':0,'Y':0,'Width':384,'Height':360},
            'sheetFile':f'sheets/{name}.png','cameraSha256':None if view=='live-stage' else 'A'*64,
            'captures':[{'Variant':v,'Particles':p,'KeyLight':k,'File':f'frames/{name}/{v}.png','PixelSha256':'A'*64, 'ArKeys':0,'ArLights':0,'ArParticles':0,'StageKeys':0,'StageSprites':0}
                        for v,p,k in review.VARIANTS],
            'comparisons':[{'id':i,'after':a,'before':b} for i,a,b in review.COMPARISONS]})
    return dict(schema=1,captureCount=288,sceneCount=72,width=1280,height=720,seed=20261014,fps=60,frames=36,
                time=.6,pedal=False,heldArt=True,sourceRevision='test-source',sourceHashes=dict.fromkeys(sources,'A'*64),scenes=scenes)


class HeldControlTests(unittest.TestCase):
    def test_complete_factorial_plan_is_accepted(self):
        self.assertEqual(72,len(review.validate_plan(plan(),'test-source')))

    def test_missing_duplicate_relabelled_or_reordered_scenes_fail(self):
        for change in (lambda p:p['scenes'].pop(),lambda p:p['scenes'].__setitem__(1,copy.deepcopy(p['scenes'][0])),
                       lambda p:p['scenes'][0].__setitem__('view','ar-reference'),
                       lambda p:p['scenes'][0]['captures'].reverse()):
            data=plan();change(data)
            with self.assertRaises(ValueError):review.validate_plan(data)

    def test_wrong_input_time_source_revision_or_empty_provenance_fail(self):
        for change in (lambda p:p.__setitem__('time',.61),lambda p:p['sourceHashes'].clear(),
                       lambda p:p['scenes'][0].__setitem__('pitches',[61])):
            data=plan();change(data)
            with self.assertRaises(ValueError):review.validate_plan(data)
        with self.assertRaises(ValueError):review.validate_plan(plan(),'another-source')

    def test_controls_are_not_silently_repaired_or_added(self):
        data=plan();data['scenes'][0]['comparisons'][0]['before']='light-only'
        with self.assertRaises(ValueError):review.validate_plan(data)
        data=plan();data['scenes'][36]['captures'][0]['PixelSha256']='B'*64
        with self.assertRaises(ValueError):review.validate_plan(data)

    def test_inactive_mode_counters_cannot_inherit_the_previous_output(self):
        data=plan();data['scenes'][0]['captures'][0]['ArParticles']=76
        with self.assertRaises(ValueError):review.validate_plan(data)
        data=plan();data['scenes'][1]['captures'][0]['StageKeys']=88
        with self.assertRaises(ValueError):review.validate_plan(data)

    def test_out_of_bounds_crop_and_unexpected_paths_fail(self):
        for change in (lambda p:p['scenes'][0]['crop'].__setitem__('X',897),
                       lambda p:p['scenes'][0].__setitem__('sheetFile','../image.png'),
                       lambda p:p['scenes'][0]['captures'][0].__setitem__('File','/image.png')):
            data=plan();change(data)
            with self.assertRaises(ValueError):review.validate_plan(data)

    def test_viewer_waits_for_decode_and_rejects_stale_or_failed_selection(self):
        self.assertIn('await image.decode()',review.TEMPLATE)
        self.assertIn('await sheet.decode()',review.TEMPLATE)
        self.assertIn('if(gen!==generation)return',review.TEMPLATE)
        self.assertIn("catch(e){if(gen!==generation)return;sheet.hidden=true",review.TEMPLATE)
        self.assertIn('sheet.hidden=true;details.replaceChildren()',review.TEMPLATE)

    def test_png_raw_bgra_hash_size_and_crc_guards(self):
        try:from PIL import Image
        except ImportError:self.skipTest('Pillow review decoder not installed in static-only lane')
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'test.png';image=Image.new('RGBA',(2,2),(11,22,33,255));image.save(path)
            digest=hashlib.sha256(image.tobytes('raw','BGRA')).hexdigest().upper()
            self.assertEqual(image.tobytes(),review.image_pixels(path,(2,2),digest).tobytes())
            for size,hash_ in [((3,2),digest),((2,2),'B'*64)]:
                with self.assertRaises(ValueError):review.image_pixels(path,size,hash_)
            data=bytearray(path.read_bytes());data[20]^=1;path.write_bytes(data)
            with self.assertRaises(ValueError):review.image_pixels(path,(2,2),digest)


if __name__=='__main__':unittest.main()
