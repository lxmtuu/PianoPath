#!/usr/bin/env python3
"""Verify and serve native Confetti classic/candidate crops, never substitute synthetic renderings."""
import argparse,gzip,html,io
from pathlib import Path
from functools import partial
from http.server import ThreadingHTTPServer
import review_confetti_particle_art as review
from serve_flames_v3_review import Handler
from serve_spark_particle_art import PLAYER
NAMES=('burst.png','held.png','live-stage-burst.bgra.gz','live-stage-held.bgra.gz','ar-reference-burst.bgra.gz','ar-reference-held.bgra.gz')

ROOT=Path(__file__).resolve().parents[1]
def build(repo,output):
    from PIL import Image
    output.mkdir(parents=True,exist_ok=True)
    target=output/'manifest.json'
    if target.is_symlink():raise ValueError('Refusing to overwrite a manifest symlink')
    target.write_bytes(gzip.decompress((repo/'docs/evidence/confetti-art-v1/manifest.json.gz').read_bytes()))
    for name in NAMES:
        source=repo/'docs/previews/confetti-art-v1'/name;target=output/name
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong Confetti asset '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    rows=[c for c in m['captures'] if c['Palette']=='prism' and c['View']=='ar-reference' and c['Pitch']==60 and c['Phase']=='held' and c['Variant']!='off']
    metrics=' · '.join(f"{c['Variant']}: RGB L1={c['Signal']}; {c['VisiblePixels']} px ΔRGB>4" for c in rows)
    page='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Confetti · Stage / AR</title>
<style>body{font:16px system-ui;background:#10141b;color:#dde6f4;max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.7}a{color:#9bcfff}img{display:block;image-rendering:pixelated;width:640px;max-width:100%;margin:auto}figure{margin:24px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption{text-align:center;padding:12px}code{word-break:break-all}</style>
<h1>Confetti · mảnh giấy có màu</h1><p><b>Prototype review-only, chưa nghiệm thu.</b> Trái classic · phải V1 candidate; Stage trên, AR reference dưới. Cùng seed và raw state; không tăng size, số hạt hay lifetime. Stage và AR giữ nguyên một quad cho mỗi mảnh giấy vật lý. Chỉ intrinsic màu/alpha của mảnh giấy được đối chiếu ngang mode, không phải toàn bộ compositing/độ sáng.</p>
<p><a href="motion-review.html">→ Player nhấn / giữ–thả, cùng frame</a> · <a href="manifest.json">Manifest</a></p>
<h2>Nhấn · 0,05 s</h2><figure><figcaption>Classic trái · Confetti V1 phải</figcaption><img src="burst.png" alt="Native Confetti burst: Stage trên, AR dưới; classic trái, candidate phải"></figure>
<h2>Giữ · 0,60 s</h2><figure><figcaption>Classic trái · Confetti V1 phải</figcaption><img src="held.png" alt="Native Confetti held: Stage trên, AR dưới; classic trái, candidate phải"></figure>
<p>__METRICS__. Signal trong cùng cảnh, không phải luminance hay điểm chất lượng.</p>
<p>Crop native phóng nearest-neighbour; không chỉnh exposure/màu. Native CI kiểm 192 static / 720 motion; subset này kiểm 8 pane và 480 nửa frame lossless. Không có đủ original fulls/off frames ở đây để kiểm lại toàn bộ dataset. Browser playback, mọi palette/tier, camera/GPU thật vẫn cần kiểm. Settings menu để cuối.</p><p>Source <code>__SOURCE__</code>.</p></html>'''
    (output/'index.html').write_text(page.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__METRICS__',html.escape(metrics)),encoding='utf-8')
    player=PLAYER.replace('Embers','Confetti').replace('emitter sparks','emitter giấy')
    player=player.replace("$('pair').hidden=true;$('frame').value=n;", "$('release').textContent=mode.endsWith('burst')?'Pedal on: frame 12':'Thả phím: frame 12';$('pair').hidden=true;$('frame').value=n;")
    (output/'motion-review.html').write_text(player,encoding='utf-8')
    frames={}
    for t in m['motions']:
        decoded=[]
        for im in review.paired_frames(output,t):
            b=io.BytesIO();im.save(b,format='PNG');decoded.append(b.getvalue())
        frames[t['Id']]=decoded
    print('PASS native Confetti published subset: source hashes, 8 panes, 480 motion halves; full originals checked in CI, not locally.',flush=True)
    return frames

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8774);p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    Handler.frames=build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
