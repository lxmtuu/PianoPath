#!/usr/bin/env python3
"""Verify and serve native Dust V1/V2 crops, never substitute synthetic renderings."""
import argparse,gzip,html,io
from pathlib import Path
from functools import partial
from http.server import ThreadingHTTPServer
import review_dust_particle_art_v2 as review
from serve_flames_v3_review import Handler
from serve_spark_particle_art import PLAYER
NAMES=('burst.png','held.png','live-stage-burst.bgra.gz','live-stage-held.bgra.gz','ar-reference-burst.bgra.gz','ar-reference-held.bgra.gz')

ROOT=Path(__file__).resolve().parents[1]
def build(repo,output):
    from PIL import Image
    output.mkdir(parents=True,exist_ok=True)
    target=output/'manifest.json'
    if target.is_symlink():raise ValueError('Refusing to overwrite a manifest symlink')
    target.write_bytes(gzip.decompress((repo/'docs/evidence/dust-art-v2/manifest.json.gz').read_bytes()))
    for name in NAMES:
        source=repo/'docs/previews/dust-art-v2'/name;target=output/name
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong Dust asset '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    rows=[c for c in m['captures'] if c['Palette']=='prism' and c['View']=='ar-reference' and c['Pitch']==60 and c['Phase']=='held' and c['Variant']!='off']
    metrics=' · '.join(f"{('V1' if c['Variant']=='classic' else 'V2')}: RGB L1={c['Signal']}; {c['VisiblePixels']} px ΔRGB>4" for c in rows)
    page='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Dust V2 · Stage / AR</title>
<style>body{font:16px system-ui;background:#10141b;color:#dde6f4;max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.7}a{color:#9bcfff}img{display:block;image-rendering:pixelated;width:640px;max-width:100%;margin:auto}figure{margin:24px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption{text-align:center;padding:12px}code{word-break:break-all}</style>
<h1>Dust V2 · giữ bụi mềm, phục hồi held</h1><p><b>Prototype review-only, chưa nghiệm thu.</b> Trái V1 · phải V2 candidate; Stage trên, AR reference dưới. Cùng seed và raw state; không tăng size, số hạt hay lifetime. Stage và AR giữ nguyên một quad cho mỗi hạt bụi vật lý. Intrinsic màu/alpha dùng chung; bề mặt Gaussian không lõi trắng, không thêm lớp hay đổi chuyển động. Quad Stage/AR giữ kích thước cũ khác nhau, còn bán kính vật liệu tính theo cùng đơn vị size của hạt. Không coi toàn bộ compositing/độ sáng là giống nhau.</p>
<p><a href="motion-review.html">→ Player nhấn / giữ–thả, cùng frame</a> · <a href="manifest.json">Manifest</a></p>
<p><b>V2 kiểm phản hồi birth-glow:</b> tăng khả năng nhìn của bụi sinh ra với glow yếu, vẫn giữ Gaussian, màu và fade theo tuổi như V1. Burst full-glow phải giống V1 từng pixel; không tăng sáng toàn cục. Đây vẫn là review-only, cần xem Stage và AR cùng lúc.</p>
<h2>Nhấn · 0,05 s</h2><figure><figcaption>Dust V1 trái · Dust V2 phải</figcaption><img src="burst.png" alt="Native Dust burst: Stage trên, AR dưới; V1 trái, candidate phải"></figure>
<h2>Giữ · 0,60 s</h2><figure><figcaption>Dust V1 trái · Dust V2 phải</figcaption><img src="held.png" alt="Native Dust held: Stage trên, AR dưới; V1 trái, candidate phải"></figure>
<p>__METRICS__. Signal trong cùng cảnh, không phải luminance hay điểm chất lượng.</p>
<p>Crop native phóng nearest-neighbour; không chỉnh exposure/màu. Native CI kiểm 192 static / 720 motion; subset này kiểm 8 pane và 480 nửa frame lossless. Không có đủ original fulls/off frames ở đây để kiểm lại toàn bộ dataset. Không bảo đảm cadence realtime; mọi palette/tier, camera/GPU thật vẫn cần kiểm. Settings menu để cuối.</p><p>Source <code>__SOURCE__</code>.</p></html>'''
    (output/'index.html').write_text(page.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__METRICS__',html.escape(metrics)),encoding='utf-8')
    player=PLAYER.replace('Embers','Dust').replace('emitter sparks','emitter bụi')
    player=player.replace('classic ↔ v1','V1 ↔ V2').replace('classic trái / v1 phải','V1 trái / V2 phải').replace('<span>Classic</span>','<span>Dust V1</span>').replace('Dust v1 · review only','Dust V2 · review only').replace('Dust classic bên trái và v1 bên phải','Dust V1 bên trái và V2 bên phải')
    player=player.replace("$('pair').hidden=true;$('frame').value=n;", "$('release').textContent=mode.endsWith('burst')?'Pedal on: frame 12':'Thả phím: frame 12';$('pair').hidden=true;$('frame').value=n;")
    (output/'motion-review.html').write_text(player,encoding='utf-8')
    frames={}
    for t in m['motions']:
        decoded=[]
        for im in review.paired_frames(output,t):
            b=io.BytesIO();im.save(b,format='PNG');decoded.append(b.getvalue())
        frames[t['Id']]=decoded
    print('PASS native Dust published subset: source hashes, 8 panes, 480 motion halves; full originals checked in CI, not locally.',flush=True)
    return frames

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8776);p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    Handler.frames=build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
