#!/usr/bin/env python3
"""Serve only verified native Shatter billboard before/after evidence, never synthetic substitutes."""
import argparse,gzip,html,io
from functools import partial
from http.server import ThreadingHTTPServer
from pathlib import Path
import review_shatter_billboard as review
from serve_flames_v3_review import Handler
from serve_shatter_particle_art import PLAYER
ROOT=Path(__file__).resolve().parents[1]
NAMES=('early.png','mid.png')+tuple(t+'.bgra.gz' for t in sorted(review.TRACKS))

def build(repo,output):
    output.mkdir(parents=True,exist_ok=True)
    target=output/'manifest.json'
    if target.is_symlink():raise ValueError('Refusing manifest symlink')
    target.write_bytes(gzip.decompress((repo/'docs/evidence/shatter-billboard-v1/manifest.json.gz').read_bytes()))
    for name in NAMES:
        source=repo/'docs/previews/shatter-billboard-v1'/name;target=output/name
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong native asset '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    page='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Shatter · AR billboard</title>
<style>body{font:16px system-ui;background:#10141b;color:#dde6f4;max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.7}a{color:#9bcfff}img{display:block;image-rendering:pixelated;width:640px;max-width:100%;margin:auto}figure{margin:24px 0;padding:12px;border:1px solid #354055;border-radius:12px}code{word-break:break-all}</style>
<h1>Shatter · cùng nhịp xoay, billboard AR</h1><p><b>Geometry prototype, default-off, chưa nghiệm thu.</b> Cả hai bên cùng material V1. Trái: AR velocity-plane cũ; phải: camera-facing tangent plane, xoay theo age/phase của Stage. Stage vẫn body + glint, AR một body quad. Không tăng size, density, lifetime hay exposure; không thêm quad. Không ép góc screen-space giống nhau qua mọi perspective/mirror.</p>
<p><a href="motion-review.html">→ Player AR reference / near-overhead · nhấn nhẹ / mạnh</a> · <a href="manifest.json">Manifest</a></p>
<p>Ba hàng: Stage control trên, AR reference giữa, AR near-overhead dưới. Stage phải giữ nguyên pixel; không phải art mới cho Stage. Mẫu giữa chuyển động/phím 61 chứa hai lỗ hổng cũ (Prism và Indigo trong matrix); cả 156 active candidate statics phải có off-delta, không còn ngoại lệ raster-empty. Đó không phải bảo đảm mọi camera/tier/size đều nhìn rõ.</p>
<h2>Nhấn mạnh · phím 60 · .05s</h2><figure><figcaption>V1 trước trái · V1 + billboard phải</figcaption><img src="early.png" alt="Native Shatter early: Stage, AR reference, AR near-overhead; before trái, after phải"></figure>
<h2>Giữa chuyển động · phím 61 · .1833s</h2><figure><figcaption>V1 trước trái · V1 + billboard phải</figcaption><img src="mid.png" alt="Native Shatter formerly empty black-key mid-sample, before trái after phải"></figure>
<p>Native CI: 624 static / 720 motion crops, bốn NoteDirection cho ba AR poses, Stage controls, 64 frozen V1 pixel controls. Uploaded corners/weights kiểm độc lập; state, ordered identities, material và non-body packets giữ nguyên trong matrix dưới cap. Subset này kiểm 22 source hashes, 12 static panes, 480 native frame halves. Full original/off frames ở CI artifacts, không re-check toàn bộ locally. Crop native phóng nearest-neighbour; không chỉnh màu.</p>
<p>Không claim realtime FPS, physical GPU/MIDI/camera, dense/cap/delay hay user art approval. Melt/Bounce/releases và settings chưa thay đổi. Source <code>__SOURCE__</code>.</p></html>'''
    (output/'index.html').write_text(page.replace('__SOURCE__',html.escape(m['sourceRevision'])),encoding='utf-8')
    # Start from the established player, then replace every inherited held/release and variant label.
    player=PLAYER.replace('Embers','Shatter billboard').replace('classic ↔ v1','V1 trước ↔ billboard').replace('Static quartets','Static comparison')
    player=player.replace('<label>Mode', '<p><b>Geometry prototype · default-off · chưa nghiệm thu.</b> Cùng material V1 ở hai bên; chỉ đổi billboard/hướng xoay AR, không tăng size hay số hạt.</p><label>Mode')
    player=player.replace('Classic</span>','V1 trước · material V1</span>').replace('Shatter billboard v1 · review only','Billboard · cùng material V1')
    player=player.replace('Stage · giữ → thả','AR near-overhead · nhấn nhẹ').replace('AR · giữ → thả','AR reference · nhấn nhẹ').replace('Stage · burst','AR near-overhead · nhấn mạnh').replace('AR · burst','AR reference · nhấn mạnh')
    player=player.replace('live-stage-held','ar-near-overhead-held').replace('live-stage-burst','ar-near-overhead-burst')
    player=player.replace('Thả phím: frame 12','Pedal on: frame 12').replace('classic trái / v1 phải','V1 trước trái / cùng V1 material + billboard phải').replace('classic bên trái và v1 bên phải','V1 trước bên trái và cùng V1 material + billboard bên phải')
    start=player.index('<p>Held track:');end=player.index('</p>',start)+4
    player=player[:start]+'<p>Một impact f0: nhấn nhẹ .45 hoặc mạnh .9; không held/release emitter. Pedal bật f12 chỉ là đối chứng. AR reference phím 60, near-overhead phím 61; cùng frame và material ở hai bên. Crop union chung của đủ 60 frames; không thả phím để phát hạt.</p>'+player[end:]
    player=player.replace('không cover oblique/overhead motion','không cover oblique motion')
    old="mode.endsWith('burst')?(n<12?'Burst đã phát · không held · pedal off':'Không held · pedal on'):(n<12?'Giữ phím · pedal off':'Đã thả phím · pedal on')"
    player=player.replace(old,"(mode.endsWith('burst')?'Nhấn mạnh .9':'Nhấn nhẹ .45')+' · không held/release · '+(n<12?'pedal off':'pedal on')")
    (output/'motion-review.html').write_text(player,encoding='utf-8')
    frames={}
    for t in m['motions']:
        decoded=[]
        for im in review.paired_frames(output,t):
            b=io.BytesIO();im.save(b,format='PNG');decoded.append(b.getvalue())
        frames[t['Id']]=decoded
    print('PASS native Shatter billboard published subset: 22 sources, 12 panes, 480 motion halves; not local full-original verification.',flush=True)
    return frames

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8778);p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    Handler.frames=build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
