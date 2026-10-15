import copy
import itertools
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import review_ar_particle_alignment as r


def plan():
    # Synthetic contract data, not captured/visual evidence.
    captures=[]
    for family,view,(tilt,azi),frame,legacy in itertools.product(('wisps','flames'),r.VIEWS,r.DIRECTIONS,(11,18),(True,False)):
        id_=f'{family}-{view}-t{tilt}-a{azi}-f{frame}'
        captures.append(dict(Id=id_,Family=family,View=view,Tilt=tilt,Azimuth=azi,Frame=frame,Legacy=legacy,
            File=id_+('-legacy' if legacy else '-aligned')+'.png',Particles=19,Notes=1,MaxAxisErrorDegrees=.01,
            StateHash='A'*64,IntrinsicHash='B'*64,PixelSha256=('C' if legacy else 'D')*64))
    return dict(sourceRevision='contract',captureCount=96,width=640,height=360,fps=30,seed=20261014,releaseFrame=12,
                pitch=60,wispAmount=8,flameIntensity=70,sourceHashes=dict.fromkeys(r.SOURCES,'A'*64),captures=captures)


class ArParticleAlignmentTests(unittest.TestCase):
    def test_full_cartesian_matrix_and_pairs(self):self.assertEqual(48,len(r.validate(plan(),'contract')))

    def test_missing_duplicate_direction_and_wrong_revision_rejected(self):
        with self.assertRaises(ValueError):r.validate(plan(),'other')
        for mutate in (lambda m:m['captures'].pop(),lambda m:m['captures'].__setitem__(1,m['captures'][0]),
                       lambda m:m['captures'][1].__setitem__('Tilt',10),lambda m:m['sourceHashes'].clear()):
            m=plan();mutate(m)
            with self.assertRaises(ValueError):r.validate(m)

    def test_not_just_visible_but_geometrically_aligned_and_simulation_unchanged(self):
        for field,value in [('MaxAxisErrorDegrees',1.01),('MaxAxisErrorDegrees',float('nan')),('StateHash','E'*64),
                            ('IntrinsicHash','E'*64),('Particles',0),('Notes',0),('PixelSha256','C'*64),('File','../bad.png')]:
            m=plan();m['captures'][1][field]=value
            with self.assertRaises(ValueError):r.validate(m)


if __name__=='__main__':unittest.main()
