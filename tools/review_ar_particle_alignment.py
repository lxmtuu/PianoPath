#!/usr/bin/env python3
"""Verify production AR alignment captures against frozen geometry; publish two lossless quartets only."""
import argparse
import hashlib
import itertools
import json
from pathlib import Path
from review_held_particle_controls import image_pixels

VIEWS=('ar-reference','ar-oblique','ar-near-overhead')
DIRECTIONS=((0,0),(45,-45),(90,90),(135,60))
from review_held_light_isolation import SOURCES as HELD_SOURCES
SOURCES=HELD_SOURCES | {'Camera/Ar3DNoteProjection.cs','Diagnostics/ArParticleAlignmentReview.cs','Gpu/GpuRenderLoop.cs'}


def validate(m,revision=None):
    if revision and m['sourceRevision']!=revision:raise ValueError('Wrong alignment revision')
    if (m['captureCount'],m['width'],m['height'],m['fps'],m['seed'],m['releaseFrame'],m['pitch'],m['wispAmount'],m['flameIntensity'])!=(96,640,360,30,20261014,12,60,8,70):raise ValueError('Wrong alignment recipe')
    if set(m['sourceHashes'])!=SOURCES:raise ValueError('Incomplete alignment source guards')
    expected=set(itertools.product(('wisps','flames'),VIEWS,DIRECTIONS,(11,18),(True,False)))
    keys=[(c['Family'],c['View'],(c['Tilt'],c['Azimuth']),c['Frame'],c['Legacy']) for c in m['captures']]
    if len(keys)!=96 or set(keys)!=expected:raise ValueError('Missing/duplicate direction/pose/phase pair')
    pairs={}
    for c in m['captures']:
        id_=f"{c['Family']}-{c['View']}-t{c['Tilt']}-a{c['Azimuth']}-f{c['Frame']}"
        if c['Id']!=id_ or c['File']!=id_+('-legacy' if c['Legacy'] else '-aligned')+'.png':raise ValueError('Wrong capture provenance')
        if c['Particles']<=0 or c['Notes']<=0 or not 0<=c['MaxAxisErrorDegrees']<=(180 if c['Legacy'] else 1):raise ValueError('Missing particles/notes or bad packed angle')
        for k in ('PixelSha256','StateHash','IntrinsicHash'):
            if len(c[k])!=64 or any(x not in '0123456789ABCDEF' for x in c[k]):raise ValueError('Invalid alignment digest')
        pairs.setdefault(id_,{})[c['Legacy']]=c
    for pair in pairs.values():
        a,b=pair[True],pair[False]
        if any(a[k]!=b[k] for k in ('StateHash','IntrinsicHash','Particles','Notes')) or a['PixelSha256']==b['PixelSha256']:raise ValueError('Changed simulation/material/selection or invisible fix')
    return pairs


def verify(directory,source_root=None,revision=None,published_only=False):
    m=json.loads((directory/'manifest.json').read_text(encoding='utf-8-sig'));pairs=validate(m,revision)
    if source_root:
        for name,digest in m['sourceHashes'].items():
            raw=(source_root/name).read_bytes()
            if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(raw).hexdigest().upper()!=digest:raise ValueError('Alignment source differs: '+name)
    if published_only:
        for sheet in m['sheets']:
            im=image_pixels(directory/sheet['file'],(1280,720),sheet['hash'])
            for pane,id_ in enumerate(sheet['ids']):
                pixels=im.crop((pane%2*640,pane//2*360,pane%2*640+640,pane//2*360+360)).tobytes('raw','BGRA')
                c=pairs[id_][pane%2==0]
                if hashlib.sha256(pixels).hexdigest().upper()!=c['PixelSha256']:raise ValueError('Published quartet differs from native frame')
        return m
    for c in m['captures']:image_pixels(directory/c['File'],(640,360),c['PixelSha256'])
    from PIL import Image
    m['sheets']=[]
    for name,view,tilt,azi,frame in [('default','ar-reference',0,0,11),('diagonal-release','ar-oblique',45,-45,18)]:
        sheet=Image.new('RGBA',(1280,720));ids=[]
        for row,family in enumerate(('wisps','flames')):
            id_=f'{family}-{view}-t{tilt}-a{azi}-f{frame}'
            for col,legacy in enumerate((True,False)):
                c=pairs[id_][legacy]
                with Image.open(directory/c['File']) as im:sheet.paste(im.convert('RGBA'),(col*640,row*360))
                ids.append(id_)
        file=name+'.png';sheet.save(directory/file,optimize=True,compress_level=9)
        m['sheets'].append(dict(file=file,ids=ids,hash=hashlib.sha256(sheet.tobytes('raw','BGRA')).hexdigest().upper()))
    (directory/'manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    verify(directory,source_root,revision,published_only=True)
    return m


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--published-only',action='store_true')
    a=p.parse_args();m=verify(a.directory,a.source_root,a.expected_revision,a.published_only)
    print('PASS AR alignment: 96 native capture records, 48 paired state/intrinsic/count guards, actual packed angle errors <=1 degree; '+('8 published native panes verified, originals NOT checked locally' if a.published_only else '96 full-frame hashes + 8 lossless published panes verified'))
