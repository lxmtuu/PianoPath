#!/usr/bin/env python3
"""Validate the published Flames subset and serve frame-locked native review (Pillow required).
No full-frame/original motion PNG verification claim: those files live in the CI artifact.
"""
import argparse
import hashlib
import html
import json
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

import review_flames_v3 as flames
from review_held_particle_art import frames_from_apng

ROOT=Path(__file__).resolve().parents[1]


def sources(manifest,root):
    for name,digest in manifest['sourceHashes'].items():
        raw=(root/name).read_bytes()
        if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
        if hashlib.sha256(raw).hexdigest().upper()!=digest:raise ValueError('Source differs: '+name)


def build(repo,output):
    read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
    m=read(repo/'docs/evidence/flames-v3/manifest.json');b=read(repo/'docs/evidence/held-light-isolation/manifest.json')
    mm=read(repo/'docs/evidence/flames-v3/motion-manifest.json')
    before=flames.shared.validate(m,b,m['sourceRevision'],family='flames');sources(m,repo);sources(mm,repo)
    if mm['sourceRevision']!=m['sourceRevision']:raise ValueError('Static/motion source mismatch')
    if (not mm['flameV3ForReview'] or mm.get('wispV3ForReview',False) or mm['seed']!=20261014
        or mm['pitches']!=[60,61,64,67] or mm['wispAmount']!=32 or mm['flameIntensity']!=70):
        raise ValueError('Wrong published motion input')
    if set(mm['sourceHashes'])!={'Diagnostics/HeldParticleArtReview.cs','Diagnostics/AuxParticleTimeline.cs','Gpu/GpuRenderLoop.cs'}:
        raise ValueError('Incomplete published motion provenance')
    for c in mm['captures']:
        ar=c['name'].startswith('flames-ar-')
        if ((c['fps'],c['frames'],c['width'],c['height'],c['cropWidth'],c['cropHeight'],c['cropX'],c['cropY'],c['heldFrames'],c['releaseFrame'],c['pedal'])
            !=(30,60,640,360,140,160,235 if ar else 240,90 if ar else 180,11,12,True)
            or len(c['hashes'])!=60 or abs(c['firstReleasedSampleSeconds']-13/30)>1e-12):
            raise ValueError('Wrong published motion timing/crop')
    asset=repo/'docs/previews/flames-v3';baseline=repo/'docs/previews/held-light-isolation'
    flames.paired(asset,mm)
    output.mkdir(parents=True,exist_ok=True)
    for name,path in [('v2',baseline),('v3',asset),('manifest.json',repo/'docs/evidence/flames-v3/manifest.json')]:
        target=output/name
        if not target.is_symlink():target.symlink_to(path,target_is_directory=path.is_dir())
        elif target.resolve()!=path.resolve():raise ValueError('Wrong viewer symlink: '+name)
    data={}
    for s in m['scenes']:
        for scene,directory in [(before[s['Id']],baseline),(s,asset)]:
            for q in scene['sheets']:
                # Candidate off quartets are not duplicated in Git. Their hashes must match v2.
                path=(directory if q['particles'] else baseline)/Path(q['sheetFile']).name
                flames.shared.image_pixels(path,(768,720),q['sheetPixelSha256'])
        data[s['Id']]={'images':[['v2/'+Path(q['sheetFile']).name for q in before[s['Id']]['sheets']],
                                [('v3/' if q['particles'] else 'v2/')+Path(q['sheetFile']).name for q in s['sheets']]],
                       'geometry':[c['Geometry'] for c in s['captures']]}
    for view in ('live-stage','ar-reference'):
        name=f'flames-normal-prism-{view}';s=next(s for s in m['scenes'] if s['Id']==name)
        flames.shared.image_pixels(asset/(name+'-full.png'),(1280,720),s['captures'][7]['PixelSha256'])
    template=flames.shared.TEMPLATE.replace('Wisps','Flames').replace('wisps-low-','flames-normal-').replace('76 hạt/19 mỗi phím','83 hạt/21–21–20–21 theo phím').replace('thân ribbon có shoulder/ridge màu, không warm-white core','mantle màu + inner body và tongue phụ thuôn').replace('candidate dưới cap','candidate material')
    clips='<p><a href="motion-review.html">Mở player cùng frame: phát/dừng, bước frame và nhảy tới release</a> · <a href="scenes.html">Hai full scene v3 để xem bối cảnh</a> · <a href="manifest.json">Manifest</a></p><p>Local subset: 8 active candidate quartets + 16 paired v2 quartets, 2 fulls và 120 paired frames đã kiểm native hashes. 64 original fulls/16 candidate crops và 240 original motion PNGs được kiểm trong CI, không nằm đủ trong subset này. Không phải camera thật/hardware acceptance.</p>'
    if m.get('legacyParticleAxesForReview',False):
        template=template.replace('<h1>','<p><strong>Đối chứng artwork với geometry lịch sử.</strong> Viewer này cố ý giữ legacy axes; xem bản so trước/sau sửa hướng ở Live Preview “AR · hạt theo góc nốt”. Không dùng viewer artwork cũ để kiểm đường bay AR production mới.</p><h1>',1)
    (output/'index.html').write_text(template.replace('__DATA__',json.dumps(data,separators=(',',':'))).replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__MOTION__',clips),encoding='utf-8')
    (output/'motion-review.html').write_text(PLAYER,encoding='utf-8')
    full='<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Flames full context</title><style>body{font:16px system-ui;background:#10141b;color:#e1e9f3;max-width:1300px;margin:24px auto;padding:16px}a{color:#9bcfff}img{max-width:100%;height:auto}</style><h1>Flames v3 / Prism / combined</h1><p><a href="/">← So v2/v3 bằng native quartets</a>. Hai ảnh dưới chỉ là full scene candidate; không có v2 fulls trong subset. Nhấn ảnh mở native 1280×720, không chỉnh exposure/colour.</p>'
    for view in ('live-stage','ar-reference'):
        src=f'v3/flames-normal-prism-{view}-full.png';full+=f'<h2>{view}</h2><a href="{src}"><img src="{src}" alt="{view} v3 full"></a>'
    (output/'scenes.html').write_text(full+'</html>',encoding='utf-8')
    frames={view:frames_from_apng((asset/f'flames-{view}-paired.png').read_bytes()) for view in ('live-stage','ar')}
    print('PASS published subset hashes, 19 static/3 motion source hashes and 240 native motion halves; generated synchronized player. Full dataset checked in CI, NOT locally.')
    return frames


PLAYER='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Flames v2 ↔ v3 · cùng frame</title>
<style>:root{font:16px system-ui;color-scheme:dark;background:#10141b;color:#e1e9f3}body{max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.6;color:#b3c2d5}button,select{font:inherit;background:#1b2637;color:inherit;border:1px solid #43536a;border-radius:8px;padding:9px;margin:4px}a{color:#9bcfff}input{width:100%}figure{margin:16px 0;background:#080a0e;border:1px solid #354055;padding:12px;border-radius:12px}img{width:840px;max-width:100%;height:auto;image-rendering:pixelated;display:block;margin:auto}figcaption{display:grid;grid-template-columns:1fr 1fr;text-align:center;color:#89e0d1;padding-bottom:12px}output{display:block;padding:12px;background:#1b2637;font-variant-numeric:tabular-nums}.error{color:#ff9999}</style>
<h1>Flames · v2 ↔ v3, cùng frame</h1><p><a href="/">← Static quartets</a> · Hai nửa nằm trong <b>cùng một PNG</b>, v2 trái / v3 phải; crop native 140×160 mỗi nửa từ output 640×360. Phóng nearest-neighbour, không chỉnh màu/độ sáng.</p>
<label>Mode <select id="mode"><option value="live-stage">Live Stage</option><option value="ar">AR</option></select></label><label>Tốc độ <select id="speed"><option value="0.5">0.5×</option><option value="1" selected>1×</option></select></label>
<button id="play">Phát</button><button id="previous">← Frame</button><button id="next">Frame →</button><button id="release">Thả phím: frame 12</button>
<output id="status" aria-live="polite">Đang giải mã…</output><input id="frame" aria-label="Frame" type="range" min="0" max="59" value="10">
<figure><figcaption><span>V2 baseline</span><span>V3 candidate</span></figcaption><img id="pair" alt="Flames v2 bên trái và v3 bên phải, cùng frame" width="280" height="160" hidden></figure>
<p>0–11: nhấn/giữ; từ frame 12: physical release, pedal bật. Flames tiếp tục sinh trong lúc heat nguội theo baseline; không thêm burst hoặc lifetime mới. Chord 60/61/64/67, intensity 70, Prism, seed 20261014. Mỗi lần đổi mode tạm dừng và giữ frame.</p><p>Player chờ decode xong mới hiện ảnh; browser playback không bảo đảm realtime 30 FPS. Hai giây/30 FPS, không âm thanh; không cover overhead/oblique motion, camera/MIDI thật hoặc hardware acceptance.</p>
<script>const $=id=>document.getElementById(id),cache=new Map();let index=10,playing=false,generation=0,timer=null;
function stop(){playing=false;clearTimeout(timer);$('play').textContent='Phát';}
function image(url){if(!cache.has(url)){const im=new Image();im.src=url;cache.set(url,im.decode().then(()=>im));}return cache.get(url);}
async function show(){const gen=++generation,n=index,mode=$('mode').value;$('pair').hidden=true;$('frame').value=n;$('status').className='';$('status').textContent='Đang giải mã…';
try{const im=await image('motion/'+mode+'/'+n+'.png');if(gen!==generation)return false;$('pair').src=im.src;await $('pair').decode();if(gen!==generation)return false;$('pair').hidden=false;$('status').textContent=`${mode} · Frame ${n}/59 · t=${((n+1)/30).toFixed(3)} s · ${n<12?'Giữ phím · pedal off':'Đã thả phím · pedal on'}`;return true;
}catch(e){if(gen!==generation)return false;stop();$('pair').hidden=true;$('status').className='error';$('status').textContent='Không giải mã được ảnh; thử tải lại trang.';return false;}}
async function tick(){const shown=await show();if(playing&&shown)timer=setTimeout(()=>{index=(index+1)%60;tick();},1000/30/Number($('speed').value));}
$('play').onclick=()=>{if(playing)stop();else{playing=true;$('play').textContent='Dừng';tick();}};
$('frame').oninput=()=>{stop();index=Number($('frame').value);show();};$('previous').onclick=()=>{stop();index=Math.max(0,index-1);show();};$('next').onclick=()=>{stop();index=Math.min(59,index+1);show();};$('release').onclick=()=>{stop();index=12;show();};$('mode').onchange=()=>{stop();show();};show();</script></html>'''


class Handler(SimpleHTTPRequestHandler):
    frames={}
    def do_GET(self):
        parts=urlsplit(self.path).path.strip('/').split('/')
        if parts[0]=='motion':
            try:
                if len(parts)!=3 or parts[1] not in self.frames or not parts[2].endswith('.png'):raise ValueError()
                n=int(parts[2][:-4])
                if not 0<=n<60:raise ValueError()
                raw=self.frames[parts[1]][n]
            except (ValueError,KeyError):self.send_error(404);return
            self.send_response(200);self.send_header('Content-Type','image/png');self.send_header('Content-Length',str(len(raw)));self.end_headers();self.wfile.write(raw);return
        super().do_GET()


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8768);p.add_argument('--verify-only',action='store_true')
    a=p.parse_args();Handler.frames=build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:
        print(f'Serving Flames native review on 0.0.0.0:{a.port}',flush=True)
        ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()


if __name__=='__main__':main()
