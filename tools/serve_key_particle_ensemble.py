#!/usr/bin/env python3
"""Serve verified native mixed-scene checkpoints; no interpolated/synthetic preview frames."""
import argparse,gzip,html,json
from functools import partial
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
import review_key_particle_ensemble as review
ROOT=Path(__file__).resolve().parents[1]

def build(repo,output):
    evidence=repo/'docs/evidence/key-particle-ensemble-v1/manifest.json.gz';m=review.read(evidence)
    output.mkdir(parents=True,exist_ok=True)
    for s in m['sheets']:
        source=repo/'docs/previews/key-particle-ensemble-v1'/s['file'];target=output/s['file']
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong ensemble asset')
    if (output/'manifest.json').is_symlink():raise ValueError('Refusing manifest symlink')
    if (output/'manifest.json.gz').is_symlink():raise ValueError('Refusing compressed manifest symlink')
    (output/'manifest.json.gz').write_bytes(evidence.read_bytes())
    (output/'manifest.json').write_bytes(gzip.decompress(evidence.read_bytes()))
    m=review.verify(output,source_root=repo,published_only=True)
    page=TEMPLATE.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__DATA__',json.dumps(m,separators=(',',':')).replace('<','\\u003c'))
    (output/'index.html').write_text(page,encoding='utf-8')
    print('PASS native mixed-scene publication: 1120 records, six sheets / 24 exact panes, source guards. Static subset, not continuous playback or hardware acceptance.',flush=True)
    return m

TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Phối hợp hạt · Stage / AR</title>
<style>:root{color-scheme:dark}body{font:16px system-ui;background:#10141b;color:#e1e9f3;max-width:1050px;margin:24px auto;padding:16px;line-height:1.65}h1,h2{line-height:1.3}a{color:#9bcfff}select{font:inherit;max-width:100%;padding:10px;background:#1b2637;color:inherit;border:1px solid #43536a;border-radius:8px;margin:5px 0}figure{margin:20px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption{display:grid;grid-template-columns:1fr 1fr;text-align:center;gap:12px;margin:12px 0}img{display:block;image-rendering:pixelated;max-width:100%;margin:auto;width:880px}code{word-break:break-all}.note{border-left:3px solid #e7bc7b;padding-left:14px}output{display:block;padding:16px;background:#1b2637;border-radius:10px}table{border-collapse:collapse;width:100%;font-size:14px}td,th{text-align:left;padding:8px;border-bottom:1px solid #354055}label{display:inline-block;max-width:100%;margin-right:12px}</style>
<h1>Hạt phối hợp · lần kiểm tra đầu</h1>
<p class="note">Không sửa artwork, emitter hay mặc định. Đây là checkpoint native từ Windows/WARP, không phải video chơi liên tục. Cột trái ghép Wisps/Flames v3 và release material đã duyệt, giữ depth Stage hiện hành .6. Cột phải là <b>đối chứng riêng release ở depth .01</b>, không phải lựa chọn mặc định mới và không tự coi là tốt hơn. AR phải giống nhau giữa hai cột.</p>
<p>Burst và morph trong cảnh vẫn dùng <b>bản shipping classic</b> để không tự bật các trial khác. Ba cụm hiển thị phủ hai held emitters, ba morph và cả ba release. Hai cụm Confetti/Dust, palette Indigo, Key light 0%, cùng các checkpoint khác đã chạy trong ma trận native nhưng không có ảnh trong subset nhỏ này.</p>
<p class="note"><b>Phát hiện cần xử lý tiếp:</b> ở checkpoint hợp âm giữ f33, AR dùng đủ cap 192 cho nhóm impact/morph (origin 0), chưa pack hạt held (origin 2) trong cả năm recipes, dù raw held còn sống. Đây là cảnh báo về ưu tiên chọn hạt khi phối hợp, không phải lý do tự tăng độ sáng hay đổi artwork. Chưa sửa cap/selection trong lượt này.</p>
<label>Cụm phối hợp<br><select id="recipe"><option value="embers-wisps">Embers · Wisps · Shatter · Float Up</option><option value="splash-flames">Splash · Flames · Melt · Dissolve</option><option value="fireworks-wisps">Fireworks · Wisps · Bounce · Smoke</option></select></label>
<label>Output<br><select id="view"><option value="live-stage">Live Stage</option><option value="ar-reference">AR · reference</option></select></label>
<h2>Giữ hợp âm → nhả phím khi pedal còn xuống</h2>
<figure><figcaption><span>Mixed · depth hiện hành</span><span>Đối chứng · release front-layer</span></figcaption><img id="sheet" alt="Hàng trên giữ hợp âm f33; hàng dưới đã thả phím f38 và pedal còn xuống; hai cột depth hiện hành / đối chứng"></figure>
<p><b>Hàng trên:</b> f33, đang giữ hợp âm tám nốt. <b>Hàng dưới:</b> f38, đã thả hợp âm ở f36 nhưng pedal còn xuống. Cả hai dùng Prism, Key light 100%, bloom 35, rim 62 và emission 149. Crop nguyên pixel ở giữa cảnh, không phải toàn khung và có thể cắt mất hạt/nốt ở ngoài vùng này; phóng nearest-neighbour, không đổi màu/exposure.</p>
<output id="metrics">Đang giải mã ảnh native…</output>
<h2>Chuỗi input chung</h2><p>Note On 60 ở f0 → Note Off f12; hợp âm 48/55/60/61/64/67/72/73 ở f18 → Note Off f36. Pedal xuống f24, lên f54. Mỗi Note On có một impact cùng pitch/velocity; held snapshot độc lập, release do LiveNote Off thật. Seed 20261016, 30 Hz. Không phải file MIDI nhập, MIDI thiết bị thật hay latency test.</p>
<p>Ma trận: 5 recipes × 2 palettes × Key light 0/100 × Stage/AR reference × 7 checkpoints × 4 variants = 1120 PNG gốc. Biến thể classic dùng held v2/release legacy, không phải ảnh trước mọi lần nâng cấp. Effect-off giữ notes/key lighting nhưng tắt particle channels. Bản gốc/off/classic và các tổ hợp chưa hiển thị nằm trong artifact CI.</p>
<p>Raw state, normalized geometry, key packets và main-light/AR guards đã được kiểm. Geometry hash cố ý bỏ RGBA và chuẩn hóa material tags; không phải bằng chứng mọi màu/compositing bằng nhau. Raw/AR-selected không bằng nhau có thể do alpha, projection hoặc cap; không tự kết luận mất nốt. Thống kê clip chỉ đếm pixel ΔRGB&gt;4 so effect-off có ít nhất một kênh ≥250, không phải điểm chất lượng hay luminance.</p>
<p>Đây chưa phải review mọi camera pose, palette/preset, hardware/FPS hoặc chốt Stage depth policy. Chưa đổi Settings; bước kế tiếp là xem toàn cảnh/chuyển động và pose khác nếu checkpoint này ổn.</p><p>Source <code>__SOURCE__</code> · <a href="manifest.json">Manifest đầy đủ</a></p>
<script>const m=__DATA__,$=id=>document.getElementById(id);let generation=0;
async function show(){const gen=++generation,r=$('recipe').value,v=$('view').value,im=$('sheet');im.hidden=true;$('metrics').dataset.id='';$('metrics').textContent='Đang giải mã ảnh native…';im.src=r+'-'+v+'.png';
try{await im.decode();if(gen!==generation)return;const rows=['chord-held','release-pedal'].map(phase=>m.captures.find(c=>c.Recipe===r&&c.Palette==='prism'&&c.Light===100&&c.View===v&&c.Phase===phase&&c.Variant==='mixed'));
$('metrics').replaceChildren();for(const c of rows){const control=m.captures.find(x=>x.Id===c.Id.replace(/mixed$/,'front-control'));const line=document.createElement('p');const selected=c.ArSelected.reduce((n,p)=>n+p.Count,0);const heldRaw=c.Population.filter(p=>p.Origin===2).reduce((n,p)=>n+p.Count,0),heldSelected=c.ArSelected.filter(p=>p.Origin===2).reduce((n,p)=>n+p.Count,0);line.textContent=`${c.Phase} · f${c.Frame}: ${c.RawParticles} raw particles; ${c.Packets} output packets`+(v==='ar-reference'?`; ${selected} AR-selected; held ${heldSelected}/${heldRaw} raw`:'')+`. ΔRGB>4: ${c.VisiblePixels} px; clipped-delta: ${c.ClippedDeltaPixels} px. Hai depth ${control.PixelSha256===c.PixelSha256?'giống từng pixel':'có khác pixel'}.`;$('metrics').append(line);}im.hidden=false;$('metrics').dataset.id=r+'-'+v;
}catch(e){if(gen===generation)$('metrics').textContent='Không giải mã được ảnh native; thử tải lại trang.';}}
for(const id of ['recipe','view'])$(id).onchange=show;show();</script></html>'''

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8783);p.add_argument('--verify-only',action='store_true');a=p.parse_args();build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(SimpleHTTPRequestHandler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
