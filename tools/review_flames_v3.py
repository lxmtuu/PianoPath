#!/usr/bin/env python3
"""Paired Flames v3 review, preserving v2 and the accepted Wisps candidate.
Published motion pairs losslessly pack both native crops horizontally to avoid duplicated backgrounds.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path

import review_wisps_v3 as shared
from make_held_particle_clips import assemble


def retained(current, prior, motion_current, motion_prior):
    for key in ('schema','sceneCount','captureCount'):
        if current[key]!=prior[key]:raise ValueError('Retained Wisps plan changed')
    if [s['Id'] for s in current['scenes']]!=[s['Id'] for s in prior['scenes']]:raise ValueError('Retained Wisps matrix changed')
    for a,b in zip(current['scenes'],prior['scenes']):
        if ([c['PixelSha256'] for c in a['captures']]!=[c['PixelSha256'] for c in b['captures']]
            or [q['sheetPixelSha256'] for q in a['sheets']]!=[q['sheetPixelSha256'] for q in b['sheets']]
            or a['comparisons']!=b['comparisons']):raise ValueError('Accepted Wisps pixels/metrics changed')
        if [(c['Coverage'],c['Geometry']) for c in a['captures']]!=[(c['Coverage'],c['Geometry']) for c in b['captures']]:raise ValueError('Accepted Wisps population/geometry changed')
    if [(c['name'],c['hashes']) for c in motion_current['captures']]!=[(c['name'],c['hashes']) for c in motion_prior['captures']]:
        raise ValueError('Accepted Wisps motion changed')


def paired(directory,manifest,build=False,require_motion_law=False):
    # The static quartets isolate light with the accepted v2 geometry, so the W2 candidate can only be
    # reviewed in the motion clips. Those clips are worthless as evidence if they silently ran the old
    # constant-rise integrator, so the manifest has to say which law drew them.
    if require_motion_law and not manifest.get('flameMotionV3ForReview'):
        raise ValueError('Flames motion was captured without the v3 motion law')
    from PIL import Image
    captures={c['name']:c for c in manifest['captures']}
    if set(captures)!={f'flames-{view}-{v}' for view in ('live-stage','ar') for v in ('v2','v3')}:
        raise ValueError('Wrong Flames motion pairs')
    for view in ('live-stage','ar'):
        output=directory/f'flames-{view}-paired.png'
        if build:
            frames=[]
            with Image.open(directory/f'flames-{view}-v2.png') as left,Image.open(directory/f'flames-{view}-v3.png') as right:
                for f in range(60):
                    left.seek(f);right.seek(f)
                    im=Image.new('RGBA',(280,160));im.paste(left.convert('RGBA'),(0,0));im.paste(right.convert('RGBA'),(140,0))
                    buffer=io.BytesIO();im.save(buffer,format='PNG',compress_level=9);frames.append(buffer.getvalue())
            output.write_bytes(assemble(frames,30))
        with Image.open(output) as pair:
            if pair.n_frames!=60 or pair.size!=(280,160):raise ValueError('Wrong paired dimensions/frame count')
            for f in range(60):
                pair.seek(f)
                if abs(pair.info.get('duration',0)-1000/30)>.001:raise ValueError('Wrong paired frame timing')
                im=pair.convert('RGBA')
                for k,v in enumerate(('v2','v3')):
                    pixels=im.crop((k*140,0,(k+1)*140,160)).tobytes('raw','BGRA')
                    if hashlib.sha256(pixels).hexdigest().upper()!=captures[f'flames-{view}-{v}']['hashes'][f]:
                        raise ValueError('Paired APNG half differs from native frame')
    return manifest


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--baseline',type=Path,required=True)
    p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--historical',type=Path)
    p.add_argument('--wisps',type=Path,required=True);p.add_argument('--prior-wisps',type=Path,required=True);p.add_argument('--prior-wisps-motion',type=Path,required=True)
    a=p.parse_args();read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
    shared.run(a.directory,a.baseline,a.directory/'review.html',source_root=a.source_root,revision=a.expected_revision,historical=a.historical,family='flames')
    retained(read(a.wisps/'manifest.json'),read(a.prior_wisps),read(a.wisps/'motion/manifest.json'),read(a.prior_wisps_motion))
    paired(a.directory/'motion',read(a.directory/'motion/manifest.json'),build=True,require_motion_law=True)
    print('PASS Flames v3: 64 full hashes/16 native crops, paired population/geometry, 240 PNG/APNG frames + 120 lossless paired frames; retained v2 and accepted Wisps unchanged. No art/hardware acceptance.')


if __name__=='__main__':main()
