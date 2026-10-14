#!/usr/bin/env python3
"""Build an offline, frame-locked review player from real committed APNGs; no image synthesis/Pillow.
Output is deliberately outside docs/previews (and normally outside Git).
"""
import argparse
import base64
import html
import json
import struct
import subprocess
from pathlib import Path

from make_held_particle_clips import SIGNATURE, chunk, read_png

ROOT = Path(__file__).resolve().parents[1]


def frames_from_apng(data):
    header, _ = read_png(data)
    offset, frames, compressed, rate, count = 8, [], [], None, None
    def finish():
        if compressed:
            frames.append(SIGNATURE + chunk(b'IHDR', header) + chunk(b'IDAT', b''.join(compressed)) + chunk(b'IEND', b''))
            compressed.clear()
    while offset < len(data):
        size = struct.unpack_from('>I', data, offset)[0]
        kind = data[offset+4:offset+8]
        body = data[offset+8:offset+8+size]
        if kind == b'acTL':
            count, _ = struct.unpack('>II', body)
        elif kind == b'fcTL':
            finish()
            _, w, h, x, y, numerator, denominator, disposal, blend = struct.unpack('>IIIIIHHBB', body)
            if (w,h) != struct.unpack_from('>II',header) or x or y or disposal or blend or numerator != 1:
                raise ValueError('Player expects full-frame SOURCE APNGs, not delta frames')
            fps = denominator or 100
            if rate is not None and fps != rate:
                raise ValueError('Inconsistent frame rate')
            rate = fps
        elif kind == b'IDAT': compressed.append(body)
        elif kind == b'fdAT': compressed.append(body[4:])
        elif kind == b'IEND':
            finish()
            break
        offset += size + 12
    if count != len(frames) or count != 60 or rate != 30:
        raise ValueError('Expected 60 frames at 30 FPS')
    return frames


def source_bytes(relative, revision):
    if revision:
        return subprocess.check_output(['git','show',f'{revision}:{relative}'],cwd=ROOT)
    return (ROOT / relative).read_bytes()


def build(output, revision=None, label=None):
    clips = {}
    for family in ('wisps','flames'):
        for mode in ('live-stage','ar'):
            for version in ('classic','art'):
                name = f'{family}-{mode}-{version}'
                data = source_bytes(f'docs/previews/held-particle-art/{name}-motion.png', revision)
                clips[name] = ['data:image/png;base64,'+base64.b64encode(frame).decode('ascii') for frame in frames_from_apng(data)]
    title = f'Held particles — {label or revision or "workspace captures"}'
    page = TEMPLATE.replace('__TITLE__',html.escape(title)).replace('__DATA__',json.dumps(clips,separators=(',',':')))
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(page,encoding='utf-8')
    return output


TEMPLATE = '''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>__TITLE__</title><style>
:root{color-scheme:dark;font:16px system-ui;background:#10141b;color:#e1e9f3}body{max-width:1100px;margin:32px auto;padding:0 20px}
h1{font-size:26px}p{line-height:1.6;color:#aebdd0}button,select{font:inherit;border:1px solid #43536a;border-radius:8px;background:#1b2637;color:inherit;padding:9px;margin:4px}button{cursor:pointer}input{width:100%;accent-color:#7cdcd1}.panes{display:grid;grid-template-columns:1fr 1fr;gap:18px}figure{margin:12px 0;background:#080a0e;border:1px solid #354055;border-radius:12px;padding:14px}figcaption{color:#89e0d1;padding-bottom:12px}img{display:block;width:min(100%,420px);height:auto;margin:auto;image-rendering:pixelated}output{display:block;margin:16px 0;padding:12px;background:#1b2637;font-variant-numeric:tabular-nums}.note{border-left:3px solid #e7bc7b;padding-left:14px}.busy{visibility:hidden}@media(max-width:600px){.panes{gap:8px}figure{padding:6px}}
</style><h1>__TITLE__</h1><p>Ảnh render thật, crop gốc 140 × 160 từ output 640 × 360. Hai bên dùng <b>cùng frame index</b>; phóng nearest-neighbour để đọc nét, không chỉnh màu/độ sáng.</p>
<label>Họ <select id="family"><option value="wisps">Wisps</option><option value="flames">Flames</option></select></label>
<label>Mode <select id="mode"><option value="live-stage">Live Stage</option><option value="ar">AR</option></select></label>
<label>Tốc độ <select id="speed"><option value="0.5">0.5×</option><option value="1" selected>1×</option></select></label>
<button id="play">Phát</button><button id="previous">← Frame</button><button id="next">Frame →</button><button id="release">Thả phím: frame 11</button>
<output id="status" aria-live="polite">Đang giải mã ảnh…</output><input id="frame" aria-label="Frame" type="range" min="0" max="59" value="10">
<div class="panes"><figure><figcaption>Classic</figcaption><img id="before" alt="Classic at selected frame"></figure><figure><figcaption>Art prototype</figcaption><img id="after" alt="Art at the same selected frame"></figure></div>
<p class="note">0–10: nhấn/giữ; từ frame 11: physical key đã thả, pedal bật. Flames có thể tiếp tục sinh khi heat nguội — không xem đó là lỗi pedal. Đây là một chuỗi 2 giây/30 FPS, pitch 60, seed 20261014; không phải camera hoặc MIDI device thật.</p>
<p>APNG được tách thành PNG lossless; player chờ giải mã xong cả hai frame rồi mới thay ảnh. Khi đổi cặp, giữ nguyên frame và tạm dừng. Canvas/camera crop không chứng nhận composition toàn cảnh, overhead thật, mọi tier/delay hoặc tốc độ GPU. Không có âm thanh.</p>
<script>
const clips=__DATA__,cache=new Map();const $=id=>document.getElementById(id);
let index=10,playing=false,generation=0,timer=null;
function stop(){playing=false;clearTimeout(timer);$('play').textContent='Phát';}
function image(url){if(!cache.has(url)){let img=new Image();let ready=new Promise((resolve,reject)=>{img.onload=()=>img.decode().then(()=>resolve(img),reject);img.onerror=reject;});img.src=url;cache.set(url,ready);}return cache.get(url);}
async function show(){const token=++generation,n=index,prefix=$('family').value+'-'+$('mode').value;
$('frame').value=n;let a,b;try{[a,b]=await Promise.all([image(clips[prefix+'-classic'][n]),image(clips[prefix+'-art'][n])]);}catch(error){if(token===generation){stop();$('status').textContent='Không giải mã được cặp ảnh; thử tải lại trang.';}return false;}
if(token!==generation)return false;$('before').src=a.src;$('after').src=b.src;
$('status').textContent=`Frame ${n}/59 · t=${((n+1)/30).toFixed(3)} s · ${n<11?'Giữ phím · pedal off':'Đã thả phím · pedal on'}`;return true;}
async function tick(){const shown=await show();if(playing && shown)timer=setTimeout(()=>{index=(index+1)%60;tick();},1000/30/Number($('speed').value));}
$('play').onclick=()=>{if(playing)stop();else{playing=true;$('play').textContent='Dừng';tick();}};
$('frame').oninput=()=>{stop();index=Number($('frame').value);show();};
$('previous').onclick=()=>{stop();index=Math.max(0,index-1);show();};$('next').onclick=()=>{stop();index=Math.min(59,index+1);show();};
$('release').onclick=()=>{stop();index=11;show();};
$('family').onchange=$('mode').onchange=()=>{stop();show();};show();
</script></html>'''


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--revision',help='Git revision containing the captures; omit to use workspace captures')
    parser.add_argument('--label',help='Evidence/source label displayed above the player')
    args=parser.parse_args()
    print(build(args.output,args.revision,args.label))
