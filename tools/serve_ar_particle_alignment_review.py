#!/usr/bin/env python3
"""Serve the two verified production AR direction quartets (not an interactive camera or motion capture)."""
import argparse
import html
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import review_ar_particle_alignment as review

ROOT=Path(__file__).resolve().parents[1]


def build(repo,output):
    output.mkdir(parents=True,exist_ok=True)
    files={'manifest.json':repo/'docs/evidence/ar-particle-alignment/manifest.json'}
    files.update({name:repo/'docs/previews/ar-particle-alignment'/name for name in ('default.png','diagonal-release.png')})
    for name,source in files.items():
        target=output/name
        if not target.is_symlink():target.symlink_to(source)
        elif target.resolve()!=source.resolve():raise ValueError('Wrong review symlink: '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    aligned=[c['MaxAxisErrorDegrees'] for c in m['captures'] if not c['Legacy']]
    page=TEMPLATE.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__MAX_ERROR__',f'{max(aligned):.6f}')
    (output/'index.html').write_text(page,encoding='utf-8')
    print('PASS 22 source hashes, 96 capture metadata records and 8 published native panes. Full 96 originals were verified in CI, NOT in this subset.')
    return m


TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>AR · hạt theo góc nốt</title>
<style>:root{color-scheme:dark;background:#10141b;color:#e1e9f3;font:16px system-ui}body{max-width:1340px;margin:24px auto;padding:16px}h1{font-size:27px}p{line-height:1.65;color:#b9c8dc}a{color:#9bcfff}select{font:inherit;color:inherit;background:#1b2637;border:1px solid #43536a;border-radius:7px;padding:9px;margin:5px}figure{margin:16px 0;background:#080a0e;border:1px solid #354055;border-radius:12px;overflow:hidden}figcaption{display:grid;grid-template-columns:1fr 1fr;text-align:center;padding:14px;color:#89e0d1}.scroll{overflow:auto}img{display:block;width:100%;height:auto;image-rendering:pixelated}.native img{width:1280px;max-width:none}.large img{width:2560px;max-width:none}output{display:block;padding:12px;background:#1b2637;border-radius:7px;font-variant-numeric:tabular-nums}.error{color:#ff9999}code{word-break:break-all}.note{border-left:3px solid #e7bc7b;padding-left:14px}</style>
<h1>AR · đường bay và thân hạt theo góc nốt</h1><p>Cùng seed, phím 60, camera, nốt MIDI, tuổi hạt và artwork. <b>Trái: hướng cũ · phải: đã sửa.</b> Hàng trên Wisps, hàng dưới Flames. Hai bản chỉ khác phép đặt hạt trong AR, không tăng density hoặc sửa shader để tạo khác biệt.</p>
<label>Cảnh <select id="scene"><option value="default">Góc nốt mặc định · AR reference · giữ phím</option><option value="diagonal-release">Góc nốt 45° / −45° · AR oblique · đã thả + pedal</option></select></label>
<label>Hiển thị <select id="zoom"><option value="fit">Vừa khung</option><option value="native">1× native · cuộn ngang</option><option value="large">2× nearest-neighbour · cuộn ngang</option></select></label>
<output id="status" aria-live="polite">Đang giải mã…</output><figure><figcaption><span>Hướng cũ — legacy, chỉ giữ làm đối chứng</span><span>Đã sửa — đường AR production</span></figcaption><div class="scroll" id="viewport"><img id="sheet" hidden width="1280" height="720" alt="Wisps và Flames, trước/sau sửa hướng AR"></div></figure>
<p id="recipe"></p><p class="note">Đây là hai ảnh tĩnh ghép lossless từ tám full frames native 640×360. Không phải camera thật hoặc playback liên tục. Cảnh diagonal còn có khác camera/phase so với cảnh default; chỉ so <b>trái/phải trong cùng hàng/cảnh</b>, không dùng hai cảnh để tách tác động riêng của góc.</p>
<p>CI kiểm 96 full frames: 2 families × 3 poses × 4 directions × hold/release × trước/sau. Trong các aligned captures, sai lệch lớn nhất giữa trục dài quad thực tế và hướng nốt chiếu tại vị trí hạt: <b>__MAX_ERROR__°</b>. Lệch ngang/turbulence của từng hạt vẫn được giữ; không ép tất cả hạt đi trên một đường thẳng. Khi trục hướng thẳng vào camera không có một góc 2D duy nhất, dùng fallback như nốt.</p>
<p>Artwork Wisps/Flames giữ nguyên; demo dùng các material v3 đã duyệt. Production alignment áp dụng cả classic và held-art, không tự bật artwork candidate mặc định. Live Stage không đổi. Source <code>__SOURCE__</code> · <a href="manifest.json">Manifest / matrix 96 captures</a>.</p>
<script>const $=id=>document.getElementById(id);let generation=0;
async function show(){const gen=++generation,scene=$('scene').value;$('sheet').hidden=true;$('status').className='';$('status').textContent='Đang giải mã…';$('recipe').textContent='';
try{const im=new Image();im.src=scene+'.png';await im.decode();if(gen!==generation)return;$('sheet').src=im.src;await $('sheet').decode();if(gen!==generation)return;$('sheet').hidden=false;
$('status').textContent=scene==='default'?'Tilt 0° / azimuth 0° · frame 11 · t=0,400 s':'Tilt 45° / azimuth −45° · frame 18 · t=0,633 s';
$('recipe').textContent=scene==='default'?'Giữ phím, pedal off; cùng một AR reference ở cả hai cột.':'Physical release trước step 12; frame 18/pedal on, cùng một AR oblique ở cả hai cột. Heat-tail Flames giữ nguyên recipe cũ.';
}catch(e){if(gen!==generation)return;$('sheet').hidden=true;$('status').className='error';$('status').textContent='Không mở được ảnh: '+e.message;}}
$('scene').onchange=show;$('zoom').onchange=()=>{$('viewport').className='scroll '+$('zoom').value;};show();</script></html>'''


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8769);p.add_argument('--verify-only',action='store_true')
    a=p.parse_args();build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:
        print(f'Serving production AR direction comparison on 0.0.0.0:{a.port}',flush=True)
        ThreadingHTTPServer(('0.0.0.0',a.port),partial(SimpleHTTPRequestHandler,directory=str(a.output.resolve()))).serve_forever()


if __name__=='__main__':main()
