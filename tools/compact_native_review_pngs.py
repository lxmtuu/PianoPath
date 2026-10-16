#!/usr/bin/env python3
"""Losslessly compact an explicit native still-image allowlist; never change alpha, pixels or animation."""
import argparse,hashlib,io,json
from pathlib import Path

TARGETS=(
 'held-light-isolation/wisps-normal-prism-ar-reference-on.png',
 'held-light-isolation/wisps-low-prism-ar-reference-on.png',
 'held-light-isolation/wisps-low-indigo-ar-near-overhead-on.png',
 'held-light-isolation/wisps-normal-prism-ar-near-overhead-on.png',
 'held-light-isolation/wisps-low-prism-ar-near-overhead-on.png',
 'held-particle-controls/flames-indigo-chord-ar-oblique.png',
 'held-light-isolation/flames-normal-indigo-ar-reference-off.png',
 'held-light-isolation/wisps-low-indigo-ar-reference-off.png',
 'held-light-isolation/wisps-normal-indigo-ar-reference-off.png',
 'held-particle-controls/flames-amber-chord-ar-oblique.png',
 'held-particle-controls/wisps-indigo-chord-ar-oblique.png',
 'held-particle-controls/flames-prism-chord-ar-oblique.png',
 'held-particle-controls/wisps-amber-chord-ar-oblique.png',
 'held-light-isolation/flames-normal-indigo-ar-near-overhead-off.png',
 'held-light-isolation/wisps-low-indigo-ar-near-overhead-off.png',
 'held-light-isolation/wisps-normal-indigo-ar-near-overhead-off.png',
 'held-particle-controls/wisps-prism-chord-ar-oblique.png',
 'held-light-isolation/wisps-normal-prism-ar-reference-off.png',
 'held-light-isolation/flames-normal-prism-ar-reference-off.png',
 'held-light-isolation/wisps-low-prism-ar-reference-off.png',
 'held-light-isolation/wisps-low-prism-ar-near-overhead-off.png',
 'held-light-isolation/flames-normal-prism-ar-near-overhead-off.png',
 'held-light-isolation/wisps-normal-prism-ar-near-overhead-off.png',
 'held-particle-controls/flames-indigo-white-ar-reference.png',
 'held-particle-controls/flames-amber-white-ar-reference.png',
 'held-particle-controls/flames-indigo-white-ar-near-overhead.png',
 'held-particle-controls/flames-amber-white-ar-near-overhead.png',
 'held-particle-controls/flames-indigo-chord-ar-reference.png',
 'held-particle-controls/flames-amber-chord-ar-reference.png',
 'held-particle-controls/wisps-indigo-chord-ar-reference.png',
 'held-particle-controls/wisps-amber-chord-ar-reference.png',
 'held-particle-controls/flames-prism-chord-ar-reference.png',
 'held-particle-controls/wisps-prism-chord-ar-reference.png',
 'held-particle-controls/flames-indigo-chord-ar-near-overhead.png',
 'held-particle-controls/flames-amber-chord-ar-near-overhead.png',
 'held-particle-controls/wisps-indigo-chord-ar-near-overhead.png',
 'held-particle-controls/wisps-amber-chord-ar-near-overhead.png',
 'held-particle-controls/flames-prism-chord-ar-near-overhead.png',
 'held-particle-controls/wisps-prism-chord-ar-near-overhead.png',
 'held-light-isolation/flames-normal-indigo-ar-reference-on.png',
 'held-light-isolation/flames-normal-prism-ar-reference-on.png',
 'held-light-isolation/wisps-normal-indigo-ar-reference-on.png',
 'held-light-isolation/flames-normal-indigo-ar-near-overhead-on.png',
 'held-light-isolation/wisps-low-indigo-ar-reference-on.png',
 'held-light-isolation/flames-normal-prism-ar-near-overhead-on.png',
 'held-light-isolation/wisps-normal-indigo-ar-near-overhead-on.png',
 'held-particle-controls-scenes/wisps-prism-chord-ar-near-overhead.png',
 'held-light-isolation/wisps-low-prism-ar-near-overhead-full.png',
 'prism-flow-p0/concert-gold-live-stage.png',
 'held-particle-controls-scenes/flames-prism-chord-ar-oblique.png',
 'held-particle-controls-scenes/wisps-prism-chord-ar-oblique.png',
 'held-light-isolation/wisps-low-prism-ar-oblique-full.png',
 'prism-flow-p0/neon-violet-live-stage.png',
 'prism-flow-p0/ice-crystal-live-stage.png',
 'ar-particle-alignment/default.png',
 'prism-flow-p3a-aux/wisps-60-ar-active.png',
 'held-particle-controls-scenes/flames-prism-chord-ar-near-overhead.png',
 'held-particle-controls-scenes/wisps-prism-chord-ar-reference.png',
 'held-light-isolation/wisps-low-prism-ar-reference-full.png',
 'held-particle-art/flames-61-ar-art.png',
 'held-particle-art/flames-60-ar-art.png',
 'held-particle-art/wisps-61-ar-art.png',
 'prism-flow-p3a-aux/flames-61-ar-active.png',
 'held-particle-art/wisps-60-ar-art.png',
 'prism-flow-p3a-aux/wisps-61-ar-active.png',
 'prism-flow-p3a-aux/flames-60-ar-active.png',
 'prism-flow-p0/concert-gold-ar.png','prism-flow-p0/neon-violet-ar.png','prism-flow-p0/ice-crystal-ar.png',
 'prism-flow-p1/ar-candidate.png','prism-flow-p1/ar-texture-off.png','prism-flow-p1/ar-bloom-off.png',
 'held-particle-controls-scenes/flames-indigo-chord-ar-reference.png',
 'held-particle-controls-scenes/flames-amber-chord-ar-reference.png',
 'held-particle-controls-scenes/flames-prism-chord-ar-reference.png',
 'flames-v3/flames-normal-prism-ar-reference-full.png',
 'held-particle-controls-scenes/wisps-indigo-chord-ar-reference.png',
 'held-particle-controls-scenes/wisps-amber-chord-ar-reference.png',
 'prism-flow-p3a/embers-ar-burst.png',
 'prism-flow-p3a/embers-ar-held.png',
 'prism-flow-p3a/splash-ar-burst.png',
 'prism-flow-p3a/splash-ar-held.png',
 'prism-flow-p3a/fireworks-ar-burst.png',
 'prism-flow-p3a/fireworks-ar-held.png',
 'prism-flow-p3a/confetti-ar-burst.png',
 'prism-flow-p3a/confetti-ar-held.png',
 'prism-flow-p3a/dust-ar-burst.png',
 'prism-flow-p3a/dust-ar-held.png',
 'prism-flow-p3a-aux/release-smoke-60-ar-active.png',
 'prism-flow-p3a-aux/release-smoke-61-ar-active.png',
 'prism-flow-p3a-aux/release-dissolve-60-ar-active.png',
 'prism-flow-p3a-aux/release-dissolve-61-ar-active.png',
 'prism-flow-p3a-aux/morph-bounce-60-ar-active.png',
 'prism-flow-p3a-aux/morph-bounce-61-ar-active.png',
 'prism-flow-p3a-aux/morph-melt-60-ar-active.png',
 'prism-flow-p3a-aux/morph-melt-61-ar-active.png',
 'prism-flow-p3a-aux/morph-shatter-60-ar-active.png',
 'prism-flow-p3a-aux/morph-shatter-61-ar-active.png',
 'prism-flow-p3a-aux/release-float-up-60-ar-active.png',
 'prism-flow-p3a-aux/release-float-up-61-ar-active.png',
 'wisps-v3/wisps-low-prism-ar-reference-full.png',
 'wisps-v3/wisps-low-prism-ar-reference-on.png',
 'wisps-v3/wisps-low-indigo-ar-reference-on.png',
 'wisps-v3/wisps-low-indigo-ar-reference-off.png',
 'flames-v3/flames-normal-prism-ar-reference-on.png',
 'flames-v3/flames-normal-indigo-ar-reference-on.png',
 'wisps-v3/wisps-low-prism-ar-near-overhead-on.png',
 'wisps-v3/wisps-low-indigo-ar-near-overhead-on.png',
 'wisps-v3/wisps-low-indigo-ar-near-overhead-off.png',
 'flames-v3/flames-normal-prism-ar-near-overhead-on.png',
 'flames-v3/flames-normal-indigo-ar-near-overhead-on.png',
 'wisps-v3/wisps-low-prism-ar-reference-off.png',
 'wisps-v3/wisps-low-prism-ar-near-overhead-off.png',
 'flames-v3/flames-normal-indigo-ar-oblique-on.png',
 'flames-v3/flames-normal-prism-ar-oblique-on.png',
 'wisps-v3/wisps-low-indigo-ar-oblique-on.png',
 'wisps-v3/wisps-low-prism-ar-oblique-on.png',
 'wisps-v3/wisps-low-indigo-ar-oblique-off.png',
 'wisps-v3/wisps-low-prism-ar-oblique-off.png',
 'flames-v3/flames-normal-prism-live-stage-full.png',
 'wisps-v3/wisps-low-prism-live-stage-full.png',
 'flames-v3/flames-normal-indigo-live-stage-on.png',
 'flames-v3/flames-normal-prism-live-stage-on.png',
 'wisps-v3/wisps-low-prism-live-stage-on.png',
 'wisps-v3/wisps-low-indigo-live-stage-on.png',
 'wisps-v3/wisps-low-indigo-live-stage-off.png',
 'wisps-v3/wisps-low-prism-live-stage-off.png',
)

def display_chunks(data):
    """Preserve original colour-management and physical-resolution chunks, including their CRCs."""
    chunks=[];offset=8
    while offset<len(data):
        length=int.from_bytes(data[offset:offset+4],'big');kind=data[offset+4:offset+8]
        if kind in (b'sRGB',b'gAMA',b'cHRM',b'iCCP',b'pHYs'):chunks.append(data[offset:offset+12+length])
        offset+=12+length
    return b''.join(chunks)


def with_display_chunks(encoded,original):
    # Strip encoder-propagated copies (e.g. ICC), then restore original chunks verbatim exactly once.
    clean=bytearray(encoded[:8]);offset=8
    while offset<len(encoded):
        length=int.from_bytes(encoded[offset:offset+4],'big');kind=encoded[offset+4:offset+8]
        if kind not in (b'sRGB',b'gAMA',b'cHRM',b'iCCP',b'pHYs'):clean.extend(encoded[offset:offset+12+length])
        offset+=12+length
    return bytes(clean[:33])+display_chunks(original)+bytes(clean[33:])


def compact(path):
    from PIL import Image
    original=path.read_bytes()
    with Image.open(io.BytesIO(original)) as im:
        if getattr(im,'n_frames',1)!=1:raise ValueError('Never rewrite animation: '+str(path))
        before=im.convert('RGBA').tobytes('raw','BGRA');size=im.size
        if im.convert('RGBA').getchannel('A').getextrema()!=(255,255):raise ValueError('Nonopaque image: '+str(path))
        out=io.BytesIO();im.convert('RGB').save(out,format='PNG',compress_level=9,optimize=True)
    result=with_display_chunks(out.getvalue(),original)
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
