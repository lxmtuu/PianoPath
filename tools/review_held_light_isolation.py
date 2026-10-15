#!/usr/bin/env python3
"""Verify tint/spill/particle WARP evidence and build an offline native-quartet viewer.
Counts/age coverage are actual packing notifications, not screen-area or visibility measures.
"""
import argparse
import base64
import hashlib
import html
import itertools
import json
from pathlib import Path

from review_held_particle_controls import image_pixels

LOADS=(('wisps','normal'),('wisps','low'),('flames','normal'))
PALETTES=('prism','indigo')
VIEWS=('live-stage','ar-reference','ar-oblique','ar-near-overhead')
PITCHES=[60,61,64,67]
VARIANTS=[(f'{p}-{light}',p=='on',t,s) for p in ('off','on')
          for light,t,s in (('neither',False,False),('tint',True,False),('spill',False,True),('both',True,True))]
SOURCES={'Gpu/StageShaders.hlsl','Gpu/GpuStageRenderer.cs','Gpu/GpuStageSimulation.cs','Gpu/GpuStageSimulation.Effects.cs',
         'Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs','Gpu/GpuLook.cs','Gpu/GpuStageFeed.cs','Gpu/HeldParticleAppearance.cs',
         'Camera/ArCameraPose.cs','Camera/ArParticleBillboard.cs','Stage/PianoStage.cs','Stage/PrismFlowPalette.cs',
         'Diagnostics/HeldParticleControlPlan.cs','Diagnostics/HeldParticleControlReview.cs','Diagnostics/AuxParticleFixtures.cs',
         'Diagnostics/KeyParticleFixtures.cs','docs/samples/ar-keyboard-topdown.png','Diagnostics/HeldLightIsolationPlan.cs'}


def coverage_rows(rows):
    if [r['Pitch'] for r in rows]!=PITCHES:
        raise ValueError('Missing/reordered pitch coverage')
    for r in rows:
        if any(type(r[k]) is not int or r[k]<0 for k in ('Count','Young','Middle','Older')):
            raise ValueError('Invalid coverage count')
        if r['Count']!=sum(r[k] for k in ('Young','Middle','Older')):
            raise ValueError('Age bins do not partition coverage')
        fields=[r[k] for k in ('MinAge','MaxAge','MeanAge','MeanProgress')]
        if not r['Count']:
            if fields!=[None]*4:raise ValueError('Zero coverage must have null age statistics')
        elif not (all(type(x) in (int,float) for x in fields) and
                  0<=r['MinAge']<=r['MeanAge']<=r['MaxAge']<=.60001 and 0<=r['MeanProgress']<1):
            raise ValueError('Invalid live-particle age/progress statistics')
    return sum(r['Count'] for r in rows)


def validate_plan(m,revision=None):
    if (m['schema'],m['captureCount'],m['sceneCount'],m['width'],m['height'],m['fps'],m['frames'],m['seed'],m['time'],m['pedal'],m['heldArt'])!=(2,192,24,1280,720,60,36,20261014,.6,False,True):
        raise ValueError('Unexpected isolation plan')
    if revision and m['sourceRevision']!=revision:raise ValueError('Wrong capture revision')
    if set(m['sourceHashes'])!=SOURCES:raise ValueError('Incomplete source guards')
    expected={f'{f}-{l}-{p}-{v}' for (f,l),p,v in itertools.product(LOADS,PALETTES,VIEWS)}
    if len(m['scenes'])!=24 or {s['Id'] for s in m['scenes']}!=expected:raise ValueError('Incomplete/duplicate scene matrix')
    populations={};off={}
    for scene in m['scenes']:
        if scene['Id']!='-'.join((scene['Family'],scene['load'],scene['palette'],scene['view'])) or scene['pitches']!=PITCHES:
            raise ValueError('Scene factors/input mismatch')
        if scene['wispAmount']!=(8 if scene['load']=='low' else 32):raise ValueError('Unexpected fixture density')
        if [(c['Variant'],c['Particles'],c['Tint'],c['Spill']) for c in scene['captures']]!=VARIANTS:
            raise ValueError('Incomplete/reordered three-factor experiment')
        if scene['crop']['Width']!=384 or scene['crop']['Height']!=360 or not(0<=scene['crop']['X']<=896 and 0<=scene['crop']['Y']<=360):
            raise ValueError('Invalid native crop')
        if len(scene['sheets'])!=2:raise ValueError('Missing off/on lighting quartets')
        for k,sheet in enumerate(scene['sheets']):
            if sheet['particles']!=(k==1) or sheet['indices']!=list(range(k*4,k*4+4)) or sheet['sheetFile']!=f"sheets/{scene['Id']}-{'on' if k else 'off'}.png":
                raise ValueError('Incorrect quartet provenance/order')
        for c in scene['captures']:
            if c['File']!=f"frames/{scene['Id']}/{c['Variant']}.png":raise ValueError('Unexpected frame path')
            ar=scene['view']!='live-stage'
            if (ar and (c['StageKeys'] or c['StageSprites'])) or (not ar and any(c[k] for k in ('ArKeys','ArLights','ArParticles'))):
                raise ValueError('Inactive-mode geometry counters')
            if ar and ((bool(c['ArKeys'])!=c['Tint']) or (bool(c['ArLights'])!=c['Spill'])):
                raise ValueError('AR tint/spill independence failed')
            cov=c['Coverage'];raw=coverage_rows(cov['Raw']);eligible=coverage_rows(cov['Eligible']);packed=coverage_rows(cov['Packed'])
            if packed!=(c['ArParticles'] if ar else c['StageSprites']) or not(0<=packed<=eligible<=raw):
                raise ValueError('Coverage/actual packing counters disagree')
            for r,e,p in zip(cov['Raw'],cov['Eligible'],cov['Packed']):
                if not p['Count']<=e['Count']<=r['Count']:raise ValueError('Per-pitch selection exceeds candidate count')
            if not c['Particles']:
                if raw or eligible or packed:raise ValueError('Particles survived emitter-off')
                key=(scene['palette'],scene['view'],c['Variant'])
                if key in off and off[key]!=c['PixelSha256']:raise ValueError('Off pixels depend on family/load')
                off[key]=c['PixelSha256']
            else:
                if not packed:raise ValueError('No active packed particles')
                if scene['load']=='low' or scene['Family']=='flames':
                    if eligible>=192 or cov['EligibleHash']!=cov['PackedHash'] or cov['Eligible']!=cov['Packed'] or any(r['Count']==0 for r in cov['Packed']):
                        raise ValueError('Sub-cap lost eligible particles/pitches')
                elif ar and (eligible<=192 or packed!=192):raise ValueError('Normal chord no longer tests AR cap')
                if not ar and (cov['EligibleHash']!=cov['PackedHash'] or cov['Eligible']!=cov['Packed']):
                    raise ValueError('Stage lost eligible particles')
            key=(scene['Family'],scene['load'],scene['palette'],c['Particles'])
            if key in populations and populations[key]!=(cov['RawHash'],cov['Raw']):raise ValueError('Light/view changed simulation population')
            populations[key]=(cov['RawHash'],cov['Raw'])
        for group in (scene['captures'][:4],scene['captures'][4:]):
            if len({json.dumps(c['Coverage'],sort_keys=True) for c in group})!=1:raise ValueError('Light changed candidate/packed populations')
    return m['scenes']


def verify(directory,sheets_only=False,source_root=None,expected_revision=None):
    m=json.loads((directory/'manifest.json').read_text(encoding='utf-8-sig'));scenes=validate_plan(m,expected_revision)
    if source_root:
        for relative,digest in m['sourceHashes'].items():
            data=(source_root/relative).read_bytes()
            if not relative.endswith('.png'):data=data.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(data).hexdigest().upper()!=digest:raise ValueError(f'Source differs: {relative}')
    for scene in scenes:
        for sheet in scene['sheets']:
            image=image_pixels(directory/sheet['sheetFile'],(768,720),sheet['sheetPixelSha256'])
            if not sheets_only:
                x,y=scene['crop']['X'],scene['crop']['Y']
                for pane,k in enumerate(sheet['indices']):
                    shot=scene['captures'][k];frame=image_pixels(directory/shot['File'],(1280,720),shot['PixelSha256'])
                    native=frame.crop((x,y,x+384,y+360));actual=image.crop(((pane%2)*384,(pane//2)*360,(pane%2+1)*384,(pane//2+1)*360))
                    if native.tobytes()!=actual.tobytes():raise ValueError('Sheet is not its native frame crop')
    return m


TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Tint / spill / coverage</title>
<style>:root{font:16px system-ui;color-scheme:dark;background:#10141b;color:#e1e9f3}body{max-width:1060px;margin:24px auto;padding:0 16px}p{line-height:1.6}select{padding:8px;margin:4px;font:inherit}img{max-width:100%;height:auto}.legend{max-width:768px;display:grid;grid-template-columns:1fr 1fr;gap:8px;padding:12px;background:#1b2637}table{border-collapse:collapse;font-size:14px}td,th{border-bottom:1px solid #43536a;text-align:left;padding:7px}.scroll{overflow:auto}.error{color:#ff9999}</style>
<h1>Đối chứng tint / spill và độ phủ hợp âm</h1><p>24 cảnh × 8 ảnh, shader v2 và AR cap giữ nguyên. Stage “tint” bao gồm tint + emission, không phải phép tách emission riêng. Chỉ WispAmount của fixture low đổi từ 32 → 8; không đổi preset sản phẩm.</p>
<label>Load <select id="load"><option value="wisps-normal">Wisps / normal</option><option value="wisps-low">Wisps / low</option><option value="flames-normal">Flames</option></select></label>
<label>Màu <select id="palette"><option>prism</option><option>indigo</option></select></label>
<label>Góc <select id="view"><option>live-stage</option><option>ar-reference</option><option>ar-oblique</option><option>ar-near-overhead</option></select></label>
<label>Hạt <select id="particles"><option value="on">Bật</option><option value="off">Tắt</option></select></label>
<p id="status" aria-live="polite"></p><div class="legend"><span>↖ Không tint/spill</span><span>↗ Chỉ tint (Stage: + emission)</span><span>↙ Chỉ spill</span><span>↘ Cả hai</span></div>
<img id="sheet" width="768" height="720" alt="Bốn crop nguyên pixel cùng input, chỉ thay tint và spill" hidden>
<div class="scroll" id="coverage"></div><p>Coverage là số instance được đóng gói thực tế theo pitch, không phải diện tích pixel hoặc độ rõ khi bị che. Bảng dùng trạng thái cả tint/spill; ba trạng thái còn lại được kiểm không đổi population. “Eligible” là cùng ngưỡng alpha surface &gt;0,003 của Stage, không phải danh sách chẩn đoán nguyên nhân reject của AR. Histogram tuổi tuyệt đối: [0;0,2), [0,2;0,4), [0,4;+∞) giây.</p>
<p>Seed 20261014, giữ 60/61/64/67 tại 0,6 s, pedal tắt, AR Ultra/Fit/70°/delay 0. Camera synthetic phẳng warp khớp calibration; không camera thật, không playback/hiệu năng hoặc nghiệm thu artwork. Source: __SOURCE__.</p>
<script>const DATA=__DATA__,ids=['load','palette','view','particles'];let generation=0;
const sheet=document.getElementById('sheet'),status=document.getElementById('status'),coverage=document.getElementById('coverage');
async function show(){const gen=++generation,key=ids.slice(0,3).map(id=>document.getElementById(id).value).join('-'),on=document.getElementById('particles').value==='on';sheet.hidden=true;coverage.replaceChildren();status.className='';status.textContent='Đang giải mã…';try{const d=DATA[key];if(!d)throw Error('Thiếu cảnh');const im=new Image();im.src=d.images[on?1:0];await im.decode();if(gen!==generation)return;sheet.src=im.src;await sheet.decode();if(gen!==generation)return;sheet.hidden=false;status.textContent=key+' / particles '+(on?'on':'off')+' / 0,600 s';const cov=d.captures[on?7:3].Coverage;const table=document.createElement('table');table.innerHTML='<tr><th>Pitch</th><th>Raw / eligible / packed</th><th>Packed age bins</th><th>Mean age eligible → packed (s)</th></tr>';for(let k=0;k<4;k++){const r=cov.Raw[k],e=cov.Eligible[k],p=cov.Packed[k],row=table.insertRow();for(const value of [p.Pitch,r.Count+' / '+e.Count+' / '+p.Count,p.Young+' / '+p.Middle+' / '+p.Older,(e.MeanAge===null?'—':e.MeanAge.toFixed(4))+' → '+(p.MeanAge===null?'—':p.MeanAge.toFixed(4))])row.insertCell().textContent=value;}coverage.append(table);}catch(e){if(gen!==generation)return;sheet.hidden=true;status.className='error';status.textContent='Không mở được ảnh: '+e.message;}}
ids.forEach(id=>document.getElementById(id).addEventListener('change',show));show();</script></html>'''


def build(directory,output,**options):
    m=verify(directory,**options)
    data={s['Id']:{'images':['data:image/png;base64,'+base64.b64encode((directory/q['sheetFile']).read_bytes()).decode() for q in s['sheets']],
                   'captures':s['captures']} for s in m['scenes']}
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(TEMPLATE.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__DATA__',json.dumps(data,separators=(',',':')).replace('<','\\u003c')),encoding='utf-8')
    return m


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--output',type=Path)
    p.add_argument('--sheets-only',action='store_true');p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision')
    a=p.parse_args();build(a.directory,a.output or a.directory/'review.html',sheets_only=a.sheets_only,source_root=a.source_root,expected_revision=a.expected_revision)
    print('PASS 24 scenes / 192 records / 48 quartet hashes / independent factor and coverage guards; '+('full frames NOT checked (sheets-only)' if a.sheets_only else 'all full-frame hashes/native crops checked'))


if __name__=='__main__':main()
