#!/usr/bin/env python3
"""Verify and serve native Bounce classic/candidate crops, never substitute synthetic renderings."""
import argparse,gzip,html,io
from pathlib import Path
from functools import partial
from http.server import ThreadingHTTPServer
import review_bounce_particle_art as review
from serve_flames_v3_review import Handler
from serve_spark_particle_art import PLAYER
NAMES=('burst.png','held.png','live-stage-burst.bgra.gz','live-stage-held.bgra.gz','ar-reference-burst.bgra.gz','ar-reference-held.bgra.gz')

ROOT=Path(__file__).resolve().parents[1]
def build(repo,output):
    from PIL import Image
    output.mkdir(parents=True,exist_ok=True)
    target=output/'manifest.json'
    if target.is_symlink():raise ValueError('Refusing to overwrite a manifest symlink')
    target.write_bytes(gzip.decompress((repo/'docs/evidence/bounce-art-v1/manifest.json.gz').read_bytes()))
    for name in NAMES:
        source=repo/'docs/previews/bounce-art-v1'/name;target=output/name
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong Bounce asset '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    rows=[c for c in m['captures'] if c['Palette']=='prism' and c['View']=='ar-reference' and c['Pitch']==60 and c['Phase']=='held' and c['Variant']!='off']
    metrics=' · '.join(f"{c['Variant']}: body-only RGB L1={c['BodySignal']}; {c['BodyVisiblePixels']} px ΔRGB>4" for c in rows)
    page='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Bounce · Stage / AR</title>
<style>body{font:16px system-ui;background:#10141b;color:#dde6f4;max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.7}a{color:#9bcfff}img{display:block;image-rendering:pixelated;width:640px;max-width:100%;margin:auto}figure{margin:24px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption{text-align:center;padding:12px}code{word-break:break-all}</style>
<h1>Bounce · vệt bật tròn đầu</h1><p><b>Prototype review-only, chưa nghiệm thu.</b> Trái classic · phải V1 candidate; Stage trên, AR reference dưới. Cùng seed và raw state; không tăng size, số hạt hay lifetime. Giữ số component cũ: Stage một body, AR một quad. Intrinsic màu/alpha dùng chung; đầu bo tròn và đuôi thuôn cùng tông, không lõi trắng, không thêm component hay đổi chuyển động. Giữ độ sâu; có thử nghiệm AR billboard riêng theo hướng vận tốc Stage, không tăng kích thước. Vòng xung đi kèm giữ nguyên. Stage có một body, AR một body; Stage giữ quad cũ, AR thử đổi hướng mặt quad, không đổi half-extents. Không coi toàn bộ compositing/độ sáng là giống nhau.</p>
<p><a href="motion-review.html">→ Player nhấn nhẹ / mạnh, cùng frame</a> · <a href="manifest.json">Manifest</a></p>
<p><b>Chỉ Bounce press:</b> Stage giữ một body, AR giữ một body quad, không ép số packet bằng nhau. Giữ vị trí tâm, stretch, độ sâu và kích thước; riêng AR đổi hướng mặt quad trong nhánh billboard default-off. Có 64 đối chứng material-only với mặt quad cũ; Stage không đổi hình học. cả hai giữ một body mỗi hạt. Có 64 đối chứng body-off giữ nguyên vòng xung, nên vòng không thể thay tín hiệu body. material cục bộ/RGBA dùng chung, không khẳng định bằng nhau về compositing/độ sáng. Tag nguồn riêng tránh đổi Embers/Fireworks cùng shape. Chuyển động bật sẵn có giữ nguyên; không thêm va chạm hay lần nảy mới.</p>
<h2>Nhấn mạnh · sample 2 ở 60 Hz</h2><figure><figcaption>Classic trái · Bounce V1 phải</figcaption><img src="burst.png" alt="Native Bounce burst: Stage trên, AR dưới; classic trái, candidate phải"></figure>
<h2>Giữa chuyển động · sample 10 ở 60 Hz</h2><figure><figcaption>Classic trái · Bounce V1 phải</figcaption><img src="held.png" alt="Native Bounce mid-impact: Stage trên, AR dưới; classic trái, candidate phải"></figure>
<p>__METRICS__. Signal trong cùng cảnh, không phải luminance hay điểm chất lượng. Vùng sáng chung ở cả hai nửa không phải tín hiệu riêng của Bounce. Vệt vẫn nhỏ; vòng xung và ánh sáng chung có thể che chi tiết. Chưa nghiệm thu nghệ thuật.</p>
<p>Crop native phóng nearest-neighbour; không chỉnh exposure/màu. Native CI kiểm 192 static + 64 body-off + 64 material-only / 720 motion; subset này kiểm 8 pane và 480 nửa frame lossless. Không có đủ original fulls/off frames ở đây để kiểm lại toàn bộ dataset. Không bảo đảm cadence realtime; mọi palette/tier, camera/GPU thật vẫn cần kiểm. Settings menu để cuối.</p><p>Source <code>__SOURCE__</code>.</p></html>'''
    (output/'index.html').write_text(page.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__METRICS__',html.escape(metrics)),encoding='utf-8')
    player=PLAYER.replace('Embers','Bounce').replace('emitter sparks','emitter Bounce')
    player=player.replace('Stage · giữ → thả','Stage · nhấn nhẹ').replace('AR · giữ → thả','AR · nhấn nhẹ').replace('Stage · burst','Stage · nhấn mạnh').replace('AR · burst','AR · nhấn mạnh')
    player=player.replace('Thả phím: frame 12','Pedal on: frame 12')
    start=player.index('<p>Held track:');end=player.index('</p>',start)+4
    player=player[:start]+'<p>Bounce duy nhất: nhấn nhẹ (.45) hoặc mạnh (.9) ở f0, không giữ emitter, không phát release. Pedal bật ở f12 chỉ là đối chứng. Crop union chung cho đủ 60 frame; Stage giữ một body, AR giữ một body quad; AR candidate dùng thử billboard theo vận tốc Stage. Storage lane held nghĩa là nhấn nhẹ, không phải giữ phím.</p>'+player[end:]
    status="mode.endsWith('burst')?(n<12?'Burst đã phát · không held · pedal off':'Không held · pedal on'):(n<12?'Giữ phím · pedal off':'Đã thả phím · pedal on')"
    player=player.replace(status,"(mode.endsWith('burst')?'Nhấn mạnh .9':'Nhấn nhẹ .45')+' · không held/release · '+(n<12?'pedal off':'pedal on')")
    (output/'motion-review.html').write_text(player,encoding='utf-8')
    frames={}
    for t in m['motions']:
        decoded=[]
        for im in review.paired_frames(output,t):
            b=io.BytesIO();im.save(b,format='PNG');decoded.append(b.getvalue())
        frames[t['Id']]=decoded
    print('PASS native Bounce published subset: source hashes, 8 panes, 480 motion halves; full originals checked in CI, not locally.',flush=True)
    return frames

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8780);p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    Handler.frames=build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
