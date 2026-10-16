#!/usr/bin/env python3
"""Verify and serve native Release classic/candidate crops, never substitute synthetic renderings."""
import argparse,gzip,html,io
from pathlib import Path
from functools import partial
from http.server import ThreadingHTTPServer
import review_release_particle_art as review
from serve_flames_v3_review import Handler
from serve_spark_particle_art import PLAYER
NAMES=('burst.png','held.png','live-stage-burst.bgra.gz','live-stage-held.bgra.gz','ar-reference-burst.bgra.gz','ar-reference-held.bgra.gz')

ROOT=Path(__file__).resolve().parents[1]
def build(repo,output,family):
    from PIL import Image
    output.mkdir(parents=True,exist_ok=True)
    target=output/'manifest.json'
    if target.is_symlink():raise ValueError('Refusing to overwrite a manifest symlink')
    target.write_bytes(gzip.decompress((repo/'docs/evidence'/('release-'+family+'-v1')/'manifest.json.gz').read_bytes()))
    for name in NAMES:
        source=repo/'docs/previews'/('release-'+family+'-v1')/name;target=output/name
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong Release asset '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    rows=[c for c in m['captures'] if c['Palette']=='prism' and c['View']=='ar-reference' and c['Pitch']==60 and c['Phase']=='held' and c['Variant']!='off']
    metrics=' · '.join(f"{c['Variant']}: RGB L1={c['Signal']}; {c['VisiblePixels']} px ΔRGB>4" for c in rows)
    page='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Release · Stage / AR</title>
<style>body{font:16px system-ui;background:#10141b;color:#dde6f4;max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.7}a{color:#9bcfff}img{display:block;image-rendering:pixelated;width:640px;max-width:100%;margin:auto}figure{margin:24px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption{text-align:center;padding:12px}code{word-break:break-all}</style>
<h1>Release · Float Up / Dissolve / Smoke · nhả phím</h1><p><b>Prototype review-only, chưa nghiệm thu.</b> Trái classic · phải V1 candidate; Stage trên, AR reference dưới. Cùng seed và raw state; không tăng size, số hạt hay lifetime. Giữ nguyên packet vật lý: Float Up/Dissolve: Stage halo+body+glint; Smoke: một body, AR một quad. Intrinsic màu/alpha dùng chung; pigment dịu, coverage riêng từng hiệu ứng, không lõi trắng, không thêm component hay đổi chuyển động. Stage có thử nghiệm độ sâu riêng, không phải material-only. Giọt Stage có ba component, Smoke có một, AR một body; quad giữ nguyên. Không coi toàn bộ compositing/độ sáng là giống nhau.</p>
<p><a href="motion-review.html">→ Player nhả phím · pedal on/off, cùng frame</a> · <a href="manifest.json">Manifest</a></p>
<p><b>Chỉ release:</b> Stage giữ ba component cho giọt, một cho Smoke, AR giữ một body quad, không ép số packet bằng nhau. Giữ nguyên vị trí, stretch và quad cũ; riêng Stage candidate thử depth .01 thay .6 để giọt không bị phím che. Có 16 đối chứng material-only depth .6; AR không đổi depth. material cục bộ/RGBA dùng chung, không khẳng định bằng nhau về compositing/độ sáng. Tag nguồn riêng phân biệt cả ba release, Splash/Melt và Dust. Không đổi birth, size, lifetime hay chuyển động.</p>
<h2>Sau nhả 2 frame · sample 23 ở 60 Hz</h2><figure><figcaption>Classic trái · Release V1 phải</figcaption><img src="burst.png" alt="Native Release burst: Stage trên, AR dưới; classic trái, candidate phải"></figure>
<h2>Sau nhả 10 frame · sample 31 ở 60 Hz</h2><figure><figcaption>Classic trái · Release V1 phải</figcaption><img src="held.png" alt="Native Release mid-impact: Stage trên, AR dưới; classic trái, candidate phải"></figure>
<p>__METRICS__. Signal trong cùng cảnh, không phải luminance hay điểm chất lượng. Vùng sáng chung ở cả hai nửa không phải tín hiệu riêng của Release. Hạt và smoke còn nhỏ; độ rõ trên phím sáng chưa được nghiệm thu.</p>
<p>Crop native phóng nearest-neighbour; không chỉnh exposure/màu. Native CI kiểm 192 main + 16 depth + 64 pedal + 32 before-release / 720 motion, mỗi hiệu ứng; subset này kiểm 8 pane và 480 nửa frame lossless. Không có đủ original fulls/off frames ở đây để kiểm lại toàn bộ dataset. Không bảo đảm cadence realtime; mọi palette/tier, camera/GPU thật vẫn cần kiểm. Settings menu để cuối.</p><p>Hiệu ứng <b>__FAMILY__</b> · Source <code>__SOURCE__</code>.</p></html>'''
    (output/'index.html').write_text(page.replace('__FAMILY__',html.escape(family)).replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__METRICS__',html.escape(metrics)),encoding='utf-8')
    player=PLAYER.replace('Embers','Release').replace('emitter sparks','emitter Release')
    player=player.replace('Stage · giữ → thả','Stage · pedal on').replace('AR · giữ → thả','AR · pedal on').replace('Stage · burst','Stage · pedal off').replace('AR · burst','AR · pedal off')
    player=player.replace('Thả phím: frame 12','Nhả phím: frame 12')
    start=player.index('<p>Held track:');end=player.index('</p>',start)+4
    player=player[:start]+'<p>Note On f0, giữ đến f11, Note Off trước step f12. Hai track cùng nhả phím; held là pedal on từ f12, burst là pedal off. Không impact, không held emitter. Crop union chung cho đủ 60 frame; số component cũ giữ nguyên, Stage có thử nghiệm depth riêng; AR giữ hình học cũ. Chỉ native review, chưa nghiệm thu nghệ thuật.</p>'+player[end:]
    status="mode.endsWith('burst')?(n<12?'Burst đã phát · không held · pedal off':'Không held · pedal on'):(n<12?'Giữ phím · pedal off':'Đã thả phím · pedal on')"
    player=player.replace(status,"(n<12?'Đang giữ phím · chưa phát release':'Đã nhả phím · release')+' · '+(n>=12&&mode.endsWith('held')?'pedal on':'pedal off')")
    (output/'motion-review.html').write_text(player,encoding='utf-8')
    frames={}
    for t in m['motions']:
        decoded=[]
        for im in review.paired_frames(output,t):
            b=io.BytesIO();im.save(b,format='PNG');decoded.append(b.getvalue())
        frames[t['Id']]=decoded
    print('PASS native Release published subset: source hashes, 8 panes, 480 motion halves; full originals checked in CI, not locally.',flush=True)
    return frames

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8781);p.add_argument('--verify-only',action='store_true');p.add_argument('--family',required=True,choices=('float-up','dissolve','smoke'));a=p.parse_args()
    # One process per family: Handler.frames is static and must not be shared between family datasets.
    Handler.frames=build(a.repo.resolve(),a.output.resolve(),a.family)
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
