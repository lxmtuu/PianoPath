#!/usr/bin/env python3
"""Serve the verified native Embers subset and synchronized frame player; no synthetic substitute images."""
import argparse
import html
import io
import gzip
from functools import partial
from http.server import ThreadingHTTPServer
from pathlib import Path
import review_spark_particle_art as review
from serve_flames_v3_review import Handler,PLAYER as BASE_PLAYER

ROOT=Path(__file__).resolve().parents[1]
NAMES=('burst.png','held.png','live-stage-burst.png','live-stage-held.png','ar-reference-burst.png','ar-reference-held.png')


def build(repo,output,version=1):
    from PIL import Image
    output.mkdir(parents=True,exist_ok=True)
    if version not in (1,2):raise ValueError('Unknown Embers version')
    if version==2:
        if (output/'manifest.json').is_symlink():raise ValueError('Use a separate v2 output directory; refusing to overwrite a manifest symlink')
        source=repo/'docs/evidence/embers-art-v2/manifest.json.gz'
        (output/'manifest.json').write_bytes(gzip.decompress(source.read_bytes()))
    for name in (('manifest.json',) if version==1 else ())+NAMES:
        source=repo/(f'docs/evidence/embers-art-v{version}' if name=='manifest.json' else f'docs/previews/embers-art-v{version}')/name
        target=output/name
        if not target.is_symlink():target.symlink_to(source)
        elif target.resolve()!=source.resolve():raise ValueError('Wrong Embers symlink: '+name)
    m=review.verify(output,source_root=repo,published_only=True)
    if m['schema']!=version:raise ValueError('Viewer/schema mismatch')
    page=TEMPLATE;player=PLAYER
    if version==2:
        page=page.replace('Embers v1','Embers v2').replace('Trái classic · phải candidate','Trái v1 · phải v2').replace('<span>Classic</span>','<span>V1 baseline</span>')
        page=page.replace('v1 giảm lõi trắng nhưng AR reference còn quá nhẹ/tối ở một số samples. Đây là đối chứng cho lượt cân contrast tiếp theo, không phải bản đã nghiệm thu.', 'v2 dùng chung màu/alpha giữa hai mode và tăng material coverage trong quad cũ. Cần xem native để chốt độ rõ; không phải camera thật/hardware acceptance.')
        page=page.replace('chỉ đổi shader material','v2 đổi shared intrinsic colour/alpha và shader coverage, không đổi geometry')
        rows=[c for c in m['captures'] if c['Palette']=='prism' and c['View']=='ar-reference' and c['Pitch']==60 and c['Phase']=='held' and c['Variant']!='off']
        metrics=' · '.join(f"{('V1' if c['Variant']=='classic' else 'V2')}: {c['VisiblePixels']} px ΔRGB>4; RGB L1={c['Signal']}" for c in rows)
        page=page.replace('<h2>Giữ phím', '<p>AR reference / held / cùng camera và phase: '+metrics+'. Đây là signal metric, không phải điểm chất lượng hoặc phép so ngang Stage/AR.</p><h2>Giữ phím')
        player=player.replace('classic ↔ v1','v1 ↔ v2').replace('classic trái / v1 phải','v1 trái / v2 phải').replace('<span>Classic</span>','<span>V1 baseline</span>').replace('Embers v1 · review only','Embers v2 · review only')
        player=player.replace('Embers classic bên trái và v1 bên phải','Embers v1 bên trái và v2 bên phải')
    (output/'index.html').write_text(page.replace('__SOURCE__',html.escape(m['sourceRevision'])),encoding='utf-8')
    (output/'motion-review.html').write_text(player,encoding='utf-8')
    frames={}
    for t in m['motions']:
        decoded=[]
        with Image.open(output/t['pairedFile']) as im:
            for f in range(60):
                im.seek(f);buffer=io.BytesIO();im.convert('RGBA').save(buffer,format='PNG');decoded.append(buffer.getvalue())
        frames[t['Id']]=decoded
    print('PASS 18 source hashes, 8 published native static crops and 480 motion halves; 192 original fulls / 720 original motion crops checked in CI, NOT locally.')
    return frames


TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Embers · Stage + AR</title>
<style>:root{font:16px system-ui;color-scheme:dark;background:#10141b;color:#e1e9f3}body{max-width:1000px;margin:24px auto;padding:16px}p{line-height:1.7;color:#b9c8dc}a{color:#9bcfff}figure{margin:24px 0;background:#080a0e;border:1px solid #354055;border-radius:12px;padding:12px;overflow:auto}figcaption{display:grid;grid-template-columns:1fr 1fr;text-align:center;color:#89e0d1;padding:12px}img{display:block;width:640px;max-width:100%;height:auto;image-rendering:pixelated;margin:auto}code{word-break:break-all}.note{border-left:3px solid #e7bc7b;padding-left:14px}</style>
<h1>Embers · thân tia và hạt có màu</h1><p class="note"><b>Prototype chưa chốt:</b> v1 giảm lõi trắng nhưng AR reference còn quá nhẹ/tối ở một số samples. Đây là đối chứng cho lượt cân contrast tiếp theo, không phải bản đã nghiệm thu.</p><p><b>Trái classic · phải candidate.</b> Hàng trên Live Stage, dưới AR production đã sửa hướng. Cùng seed, phím 60, recipe và raw state/actual packet geometry; chỉ đổi shader material. Không tăng density, size, lifetime hoặc ép packing. Mẫu này tắt bloom và mask tint/spill ở cả hai version để đọc thân hạt; không phải đổi default sản phẩm.</p>
<p><a href="motion-review.html">→ Player cùng frame: burst / giữ–thả / pedal</a> · <a href="manifest.json">Manifest / matrix / source hashes</a></p>
<h2>Nhấn một lần · 0,05 s</h2><figure><figcaption><span>Classic</span><span>Embers v1 · review only</span></figcaption><a href="burst.png"><img src="burst.png" alt="Native burst crops: Stage trên, AR dưới"></a></figure>
<h2>Giữ phím · 0,60 s</h2><figure><figcaption><span>Classic</span><span>Embers v1 · review only</span></figcaption><a href="held.png"><img src="held.png" alt="Native held crops: Stage trên, AR dưới"></a></figure>
<p class="note">Hai ảnh là <b>crop native, không phải full camera</b>. Mỗi hàng dùng bounding box chung của off-difference; có thể khác crop size giữa các mode/phase. Không suy ra hai mode ngang độ sáng từ hình ghép. Crop nhỏ được phóng nearest-neighbour, không chỉnh màu hoặc exposure.</p>
<p>CI: 192 full statics / 64 paired state + normalized actual-packet guards; 720 native motion crops. Subset ở đây kiểm lại 8 published static crops và 480 synchronized APNG halves; không có đủ original fulls/off-motion crops để re-verify độc lập toàn bộ dataset. Source <code>__SOURCE__</code>.</p>
<p>Embers v1 default-off, chưa nghiệm thu hình ảnh/camera/GPU thật. Wisps/Flames đã duyệt và note-aligned AR giữ nguyên; không phải tất cả particle families đã hoàn tất. Settings menu để cuối.</p></html>'''

PLAYER=BASE_PLAYER.replace('Flames v2 ↔ v3','Embers classic ↔ v1').replace('Flames · v2 ↔ v3','Embers · classic ↔ v1')
PLAYER=PLAYER.replace('v2 trái / v3 phải; crop native 140×160 mỗi nửa từ output 640×360','classic trái / v1 phải; crop native từ output 640×360, bbox chung theo từng track').replace('Flames v2 bên trái và v3 bên phải','Embers classic bên trái và v1 bên phải').replace('V2 baseline','Classic').replace('V3 candidate','Embers v1 · review only')
PLAYER=PLAYER.replace('<option value="live-stage">Live Stage</option><option value="ar">AR</option>', '<option value="live-stage-held">Stage · giữ → thả</option><option value="ar-reference-held">AR · giữ → thả</option><option value="live-stage-burst">Stage · burst</option><option value="ar-reference-burst">AR · burst</option>')
start=PLAYER.index('<p>0–11:');end=PLAYER.index('<script>',start)
PLAYER=PLAYER[:start]+'''<p>Held track: giữ f0–11, thả trước step 12; không impact. Burst track: chỉ một impact f0, không held emitter. Cả hai bật pedal từ f12; pedal không giữ emitter sparks. Từng track crop chung từ union off-difference của đủ 60 frames, giữ nguyên pixel. Seed 20261014, Prism, phím 60, bloom/tint/spill isolated.</p><p>Chờ giải mã xong mới hiện ảnh; browser playback không bảo đảm realtime 30 FPS. Hai giây, không âm thanh; không cover oblique/overhead motion, mọi palette, MIDI/camera thật hoặc hardware acceptance.</p>'''+PLAYER[end:]
PLAYER=PLAYER.replace("n<12?'Giữ phím · pedal off':'Đã thả phím · pedal on'", "mode.endsWith('burst')?(n<12?'Burst đã phát · không held · pedal off':'Không held · pedal on'):(n<12?'Giữ phím · pedal off':'Đã thả phím · pedal on')")
PLAYER=PLAYER.replace("$('pair').hidden=false;$('status')", "$('pair').hidden=false;$('pair').width=im.naturalWidth;$('pair').height=im.naturalHeight;$('status')")


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8770);p.add_argument('--version',type=int,choices=(1,2),default=1);p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    Handler.frames=build(a.repo.resolve(),a.output.resolve(),a.version)
    if not a.verify_only:
        print(f'Serving native Embers comparison on 0.0.0.0:{a.port}',flush=True)
        ThreadingHTTPServer(('0.0.0.0',a.port),partial(Handler,directory=str(a.output.resolve()))).serve_forever()


if __name__=='__main__':main()
