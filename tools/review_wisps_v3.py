#!/usr/bin/env python3
"""Verify paired, sub-cap Wisps v2/v3 evidence. No art/projection/hardware acceptance implied."""
import argparse
import base64
import copy
import hashlib
import html
import json
import math
from pathlib import Path

import review_held_light_isolation as light
from review_held_particle_controls import image_pixels
from make_held_particle_clips import assemble


def geometry(g, count):
    if g['Count'] != count or len(g['Hash']) != 64 or any(c not in '0123456789ABCDEF' for c in g['Hash']):
        raise ValueError('Invalid actual-geometry count/hash')
    keys=('MinWidth','MaxWidth','MeanWidth','MinHeight','MaxHeight','MeanHeight','CenterYMin','CenterYMax')
    if not count:
        if any(g[k] is not None for k in keys):raise ValueError('Empty geometry must have null dimensions')
    elif not (all(type(g[k]) in (int,float) and math.isfinite(g[k]) for k in keys)
              and 0<g['MinWidth']<=g['MeanWidth']<=g['MaxWidth']
              and 0<g['MinHeight']<=g['MeanHeight']<=g['MaxHeight'] and g['CenterYMin']<=g['CenterYMax']):
        raise ValueError('Invalid output-pixel geometry dimensions')


def validate(m,b,revision=None):
    light.validate_plan(b,revision)
    expected={s['Id'] for s in b['scenes'] if s['Family']=='wisps' and s['load']=='low'}
    if (m['schema'],m['sceneCount'],m['captureCount'],m['wispV3ForReview'])!=(3,8,64,True):
        raise ValueError('Wrong v3 plan')
    if m['sourceRevision']!=b['sourceRevision'] or m['sourceHashes']!=b['sourceHashes']:
        raise ValueError('V2/v3 must use the same source run')
    if len(m['scenes'])!=8 or {s['Id'] for s in m['scenes']}!=expected:
        raise ValueError('Missing/duplicate low-Wisps scene')
    combined=copy.deepcopy(m);combined.update(schema=2,sceneCount=24,captureCount=192)
    candidates={s['Id']:s for s in m['scenes']}
    combined['scenes']=[candidates.get(s['Id'],s) for s in b['scenes']]
    light.validate_plan(combined,revision)
    before={s['Id']:s for s in b['scenes']}
    for s in m['scenes']:
        prior=before[s['Id']]
        if any(s[k]!=prior[k] for k in ('crop','pitches','view','palette','cameraSha256','wispAmount')):
            raise ValueError('Candidate changed input/camera/crop')
        for c,p in zip(s['captures'],prior['captures']):
            if c['Coverage']!=p['Coverage']:raise ValueError('Candidate changed particle samples/ages')
            count=sum(r['Count'] for r in c['Coverage']['Packed'])
            geometry(c['Geometry'],count);geometry(p['Geometry'],count)
            if c['Geometry']!=p['Geometry']:raise ValueError('Candidate changed packed corners/size')
            if (c['PixelSha256']==p['PixelSha256'])==c['Particles']:
                raise ValueError('Active must change; particle-off must stay pixel-identical')
        if any(x['fullFrame']['OverFourPixels']<=0 for x in s['comparisons'] if x['id'].startswith('particles-')):
            raise ValueError('Candidate has no active particle signal')
    return before


def historical_guard(current,path):
    old=json.loads(path.read_text(encoding='utf-8-sig'))
    light.validate_plan(old)
    if [s['Id'] for s in old['scenes']]!=[s['Id'] for s in current['scenes']]:raise ValueError('Historical matrix changed')
    for a,b in zip(old['scenes'],current['scenes']):
        if ([c['PixelSha256'] for c in a['captures']]!=[c['PixelSha256'] for c in b['captures']]
            or [q['sheetPixelSha256'] for q in a['sheets']]!=[q['sheetPixelSha256'] for q in b['sheets']]
            or a['comparisons']!=b['comparisons']):
            raise ValueError('V3 branch changed retained v2 baseline pixels/metrics')


def motion(directory,sheets_only=False,source_root=None,revision=None):
    from PIL import Image
    m=json.loads((directory/'manifest.json').read_text(encoding='utf-8-sig'))
    if revision and m['sourceRevision']!=revision:raise ValueError('Wrong motion revision')
    if set(m['sourceHashes'])!={'Diagnostics/HeldParticleArtReview.cs','Diagnostics/AuxParticleTimeline.cs','Gpu/GpuRenderLoop.cs'}:
        raise ValueError('Incomplete motion source guards')
    if source_root:
        for name,digest in m['sourceHashes'].items():
            data=(source_root/name).read_text(encoding='utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(data).hexdigest().upper()!=digest:raise ValueError('Motion source differs: '+name)
    names={f'wisps-{view}-{version}' for view in ('live-stage','ar') for version in ('v2','v3')}
    if not m['wispV3ForReview'] or m['seed']!=20261014 or m['pitches']!=[60,61,64,67] or m['wispAmount']!=8:
        raise ValueError('Wrong motion input')
    if len(m['captures'])!=4 or {c['name'] for c in m['captures']}!=names:raise ValueError('Missing motion pairs')
    captures={c['name']:c for c in m['captures']}
    for c in m['captures']:
        if (c['fps'],c['frames'],c['width'],c['height'],c['cropWidth'],c['cropHeight'],c['heldFrames'],c['releaseFrame'],c['pedal'])!=(30,60,640,360,140,160,11,12,True) or len(c['hashes'])!=60 or abs(c['firstReleasedSampleSeconds']-13/30)>1e-12:
            raise ValueError('Wrong motion timing/crop')
        output=directory/(c['name']+'.png')
        if not sheets_only:
            files=[directory/c['name']/f'{f:03}.png' for f in range(60)]
            for f,p in enumerate(files):image_pixels(p,(140,160),c['hashes'][f])
            output.write_bytes(assemble([p.read_bytes() for p in files],30))
        with Image.open(output) as im:
            if im.n_frames!=60 or im.size!=(140,160):raise ValueError('Wrong APNG frames/dimensions')
            for f,digest in enumerate(c['hashes']):
                im.seek(f)
                if hashlib.sha256(im.convert('RGBA').tobytes('raw','BGRA')).hexdigest().upper()!=digest:
                    raise ValueError('APNG differs from native motion frame')
                if abs(im.info.get('duration',0)-1000/30)>.001:raise ValueError('Wrong APNG timing')
    for view in ('live-stage','ar'):
        a=captures[f'wisps-{view}-v2'];b=captures[f'wisps-{view}-v3']
        if a['hashes'][-1]!=b['hashes'][-1] or a['hashes']==b['hashes']:
            raise ValueError('Motion must change while active and converge after expiry')
    return m


TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Wisps v2 ↔ v3</title>
<style>body{font:16px system-ui;background:#10141b;color:#e1e9f3;max-width:1100px;margin:24px auto;padding:16px}p{line-height:1.6}select{font:inherit;padding:8px;margin:4px}img{max-width:100%;height:auto}a{color:#9bcfff}figure{margin:20px 0}figcaption{padding:8px;background:#1b2637}.error{color:#ff9999}</style>
<h1>Wisps v2 ↔ v3 — candidate dưới cap</h1><p>Cùng 76 hạt/19 mỗi phím, seed/input/lifetime/geometry/camera/tint/spill. V3 chỉ đổi material: thân ribbon có shoulder/ridge màu, không warm-white core. Không sửa projection hoặc cap, chưa nghiệm thu artwork. Source __SOURCE__.</p>
<label>Màu <select id="palette"><option>prism</option><option>indigo</option></select></label><label>Góc <select id="view"><option>live-stage</option><option>ar-reference</option><option>ar-oblique</option><option>ar-near-overhead</option></select></label><label>Hạt <select id="particles"><option value="on">Bật</option><option value="off">Tắt</option></select></label>
<p>Mỗi quartet: ↖ neither · ↗ tint only · ↙ spill only · ↘ both. Stage tint bao gồm emission. Pane 384×360 native; trình duyệt có thể scale theo viewport.</p><p id="status" aria-live="polite"></p>
<figure><figcaption>V2 — baseline</figcaption><img id="before" hidden width="768" height="720" alt="V2 lighting quartet"></figure><figure><figcaption>V3 — candidate</figcaption><img id="after" hidden width="768" height="720" alt="V3 lighting quartet"></figure><p id="geometry"></p>
<h2>Motion, Prism / chord low / 30 FPS</h2><p>Frame-locked 60 frames, hold/release/pedal rồi expiry; crop 140×160 từ output 640×360. Không phải camera thật hay chứng nhận temporal readability ở mọi pose.</p>__MOTION__
<script>const DATA=__DATA__;let generation=0;const ids=['palette','view','particles'],before=document.getElementById('before'),after=document.getElementById('after'),status=document.getElementById('status'),geometry=document.getElementById('geometry');
async function show(){const gen=++generation;before.hidden=after.hidden=true;geometry.textContent='';status.className='';status.textContent='Đang giải mã…';try{const key='wisps-low-'+ids.slice(0,2).map(k=>document.getElementById(k).value).join('-'),on=document.getElementById('particles').value==='on',d=DATA[key],imgs=[new Image(),new Image()];imgs.forEach((im,k)=>im.src=d.images[k][on?1:0]);await Promise.all(imgs.map(im=>im.decode()));if(gen!==generation)return;before.src=imgs[0].src;after.src=imgs[1].src;await Promise.all([before.decode(),after.decode()]);if(gen!==generation)return;before.hidden=after.hidden=false;status.textContent=key+' / '+(on?'on':'off')+' / 0,600 s';const g=d.geometry[on?7:3];geometry.textContent=g.Count?g.Count+' packed; mean quad width × height: '+g.MeanWidth.toFixed(3)+' × '+g.MeanHeight.toFixed(3)+' px; centre-Y range '+g.CenterYMin.toFixed(3)+'…'+g.CenterYMax.toFixed(3)+' px. Geometry v2/v3 hash-identical; quad bounds ≠ visible pixel area.':'0 particles; v2/v3 must be pixel-identical.';}catch(e){if(gen!==generation)return;before.hidden=after.hidden=true;status.className='error';status.textContent='Không mở được ảnh: '+e.message;}}
ids.forEach(k=>document.getElementById(k).addEventListener('change',show));show();</script></html>'''


def run(directory,baseline,output,sheets_only=False,source_root=None,revision=None,historical=None):
    b=light.verify(baseline,sheets_only=sheets_only,source_root=source_root,expected_revision=revision)
    m=json.loads((directory/'manifest.json').read_text(encoding='utf-8-sig'));before=validate(m,b,revision)
    if historical:historical_guard(b,historical)
    for s in m['scenes']:
        for q in s['sheets']:
            im=image_pixels(directory/q['sheetFile'],(768,720),q['sheetPixelSha256'])
            if not sheets_only:
                x,y=s['crop']['X'],s['crop']['Y']
                for pane,k in enumerate(q['indices']):
                    c=s['captures'][k];frame=image_pixels(directory/c['File'],(1280,720),c['PixelSha256'])
                    if frame.crop((x,y,x+384,y+360)).tobytes()!=im.crop(((pane%2)*384,(pane//2)*360,(pane%2+1)*384,(pane//2+1)*360)).tobytes():raise ValueError('V3 sheet is not native frame crop')
    mm=motion(directory/'motion',sheets_only,source_root,m['sourceRevision'])
    uri=lambda p:'data:image/png;base64,'+base64.b64encode(p.read_bytes()).decode()
    data={s['Id']:{'images':[[uri(baseline/q['sheetFile']) for q in before[s['Id']]['sheets']],[uri(directory/q['sheetFile']) for q in s['sheets']]],'geometry':[c['Geometry'] for c in s['captures']]} for s in m['scenes']}
    clips=''.join(f'<figure><figcaption>{html.escape(c["name"])}</figcaption><img width="140" height="160" src="{uri(directory/"motion"/(c["name"]+".png"))}" alt="{html.escape(c["name"])}"></figure>' for c in mm['captures'])
    output.write_text(TEMPLATE.replace('__DATA__',json.dumps(data,separators=(',',':')).replace('<','\\u003c')).replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__MOTION__',clips),encoding='utf-8')
    return m


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--baseline',type=Path,required=True);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--historical',type=Path);p.add_argument('--sheets-only',action='store_true');p.add_argument('--output',type=Path)
    a=p.parse_args();run(a.directory,a.baseline,a.output or a.directory/'review.html',a.sheets_only,a.source_root,a.expected_revision,a.historical)
    print('PASS Wisps v3: 64 records, 16 native quartet hashes, paired populations/geometry, 240 lossless APNG frames; '+('full frames NOT checked (sheets-only)' if a.sheets_only else '64 full hashes/crops and v2 baseline verified'))


if __name__=='__main__':main()
