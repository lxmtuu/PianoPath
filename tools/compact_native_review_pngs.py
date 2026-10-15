#!/usr/bin/env python3
"""Losslessly compact an explicit native still-image allowlist; never change alpha, pixels or animation."""
import argparse,hashlib,io,json
from pathlib import Path

TARGETS=(
 'prism-flow-p0/concert-gold-ar.png','prism-flow-p0/neon-violet-ar.png','prism-flow-p0/ice-crystal-ar.png',
 'prism-flow-p1/ar-candidate.png','prism-flow-p1/ar-texture-off.png','prism-flow-p1/ar-bloom-off.png',
 'held-particle-controls-scenes/flames-indigo-chord-ar-reference.png',
 'held-particle-controls-scenes/flames-amber-chord-ar-reference.png',
 'held-particle-controls-scenes/flames-prism-chord-ar-reference.png',
 'flames-v3/flames-normal-prism-ar-reference-full.png',
 'held-particle-controls-scenes/wisps-indigo-chord-ar-reference.png',
 'held-particle-controls-scenes/wisps-amber-chord-ar-reference.png')

def compact(path):
    from PIL import Image
    original=path.read_bytes()
    with Image.open(io.BytesIO(original)) as im:
        if getattr(im,'n_frames',1)!=1:raise ValueError('Never rewrite animation: '+str(path))
        before=im.convert('RGBA').tobytes('raw','BGRA');size=im.size
        if im.convert('RGBA').getchannel('A').getextrema()!=(255,255):raise ValueError('Nonopaque image: '+str(path))
        out=io.BytesIO();im.convert('RGB').save(out,format='PNG',compress_level=9,optimize=True)
    result=out.getvalue()
    with Image.open(io.BytesIO(result)) as im:
        if im.size!=size or im.convert('RGBA').tobytes('raw','BGRA')!=before:raise ValueError('Native pixels changed')
    saved=max(0,len(original)-len(result))
    if saved:path.write_bytes(result)
    return dict(file=str(path),saved=saved,nativeBgraSha256=hashlib.sha256(before).hexdigest().upper())

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);a=p.parse_args()
    rows=[compact(a.root/'docs/previews'/name) for name in TARGETS]
    print(json.dumps(dict(saved=sum(r['saved'] for r in rows),files=rows),indent=2))
if __name__=='__main__':main()
