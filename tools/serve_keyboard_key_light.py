#!/usr/bin/env python3
"""Verify and serve the bounded native key-light comparison; no simulated screenshots."""
import argparse,gzip,html,json
from functools import partial
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
import review_keyboard_key_light as review
ROOT=Path(__file__).resolve().parents[1]

def build(repo,output):
    from PIL import Image
    evidence=repo/'docs/evidence/keyboard-key-light-v1/manifest.json.gz'
    m=review.read(evidence);output.mkdir(parents=True,exist_ok=True)
    for s in m['sheets']:
        source=repo/'docs/previews/keyboard-key-light-v1'/s['file'];target=output/s['file']
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong native asset target')
    target=output/'manifest.json'
    if target.is_symlink():raise ValueError('Refusing manifest symlink overwrite')
    target.write_bytes(gzip.decompress(evidence.read_bytes()))
    m=review.verify(output,source_root=repo,published_only=True)
    for s in m['sheets']:
        if '-ar-' in s['file']:continue
        with Image.open(output/s['file']) as im:
            h=s['height']//2
            for folder,box in (('pairs',(0,0,440,h)),('ramps',(0,h,1100,h*2))):
                dest=output/folder/s['file'];dest.parent.mkdir(exist_ok=True);im.crop(box).save(dest,compress_level=9)
    source=html.escape(m['sourceRevision'])
    page=TEMPLATE.replace('__SOURCE__',source).replace('__DATA__',json.dumps(m,separators=(',',':')))
    (output/'index.html').write_text(page,encoding='utf-8')
    print('PASS native key-light publication: source hashes, 10 sheets / 60 native panes, zero references, monotone metrics and AR invariance; full original PNGs verified in CI, not downloaded locally.',flush=True)
    return m

TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Key light · đèn chính về 0</title>
<style>:root{color-scheme:dark}body{font:16px system-ui;background:#10141b;color:#e1e9f3;max-width:1120px;margin:24px auto;padding:16px}p{line-height:1.7}a{color:#9bcfff}select{font:inherit;padding:10px;background:#1b2637;color:inherit;border:1px solid #43536a;border-radius:8px;margin:4px}figure{margin:20px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption,.ramp{display:grid;text-align:center;gap:8px;margin:12px 0}.pair-label{grid-template-columns:1fr 1fr}.ramp{grid-template-columns:repeat(5,1fr)}img{display:block;image-rendering:pixelated;max-width:100%;margin:auto;width:1100px}#pair{width:880px}code{word-break:break-all}output{display:block;line-height:1.7;padding:16px;background:#1b2637;border-radius:10px}.note{border-left:3px solid #e7bc7b;padding-left:14px}</style>
<h1>Key light · 0% tắt đúng đèn chính</h1><p class="note">Bản thử sửa ánh sáng, chưa chốt bằng test của cậu. 0% không còn mức đèn chính ẩn. Vùng dưới 25% nối mượt, từ 25% trở lên giữ công thức cũ; Chỉ đổi đường đáp ứng đèn chính; giữ công thức ambient, Rim light, Key emission và Exposure cũ. Hệ số ambient vẫn phụ thuộc Key light như trước, chưa được tách thành điều khiển độc lập. Bàn phím vẫn có thể sáng ở 0% do những nguồn còn lại.</p>
<p>Đây là crop native qua Windows/WARP hoặc software Cinematic, không phải ảnh vẽ lại. Crop giữa bàn phím có cả phím trắng/đen. Mixed dùng các mức tilt 38, shadow 46, AO 52, gloss 92, rim 62, emission 149 từ ảnh cậu gửi, <b>không phải bản sao đầy đủ preset của cậu</b>. Isolated tắt rim/emission/spill để đọc riêng thay đổi đèn chính; vẫn giữ ambient. Bloom tắt trong cả hai để tránh nhiễu phép so.</p>
<label>Renderer <select id="engine"><option value="gpu">GPU · WARP</option><option value="software">Software · Cinematic</option></select></label>
<label>Cảnh <select id="scene"><option value="mixed">Mixed · các nguồn khác còn bật</option><option value="isolated">Isolated · chỉ đèn chính + ambient</option></select></label>
<label>Phím <select id="state"><option value="idle">Đang nghỉ</option><option value="held">Đang nhấn 60 + 61</option></select></label>
<h2>Mức 0%</h2><figure><figcaption class="pair-label"><span id="left">Trước sửa · 0%</span><span>Sau sửa · 0%</span></figcaption><img id="pair" alt="Đối chứng hai nửa native ở mức 0%"></figure>
<output id="metrics"></output>
<h2>Đường tăng sáng · cùng scene và thời điểm</h2><figure><div class="ramp"><span>0%</span><span>25%</span><span>50%</span><span>100%</span><span>200%</span></div><img id="ramp" alt="Năm crop native ở các mức 0, 25, 50, 100, 200 phần trăm"></figure>
<p>Software vốn đã tắt đúng đèn chính ở 0%, nên nửa trái của đối chứng software là <b>ép đèn tắt độc lập</b>, không phải lỗi GPU cũ. Hai nửa software 0% phải giống nhau. Không dùng độ sáng giữa hai renderer để chấm rằng chúng phải giống hệt.</p>
<h2>AR không đổi theo đèn của bàn phím ảo</h2><figure><figcaption class="pair-label"><span>0%</span><span>200%</span></figcaption><img id="ar" alt="Hai crop AR native phải giống nhau"></figure>
<p>154 captures trong CI, gồm 9 mức sáng, hai trạng thái phím, các reference độc lập và AR; local subset có 10 sheets / 60 panes. Số đo là trung bình RGB đã encode trong ROI cố định, không phải lux hay độ sáng tuyến tính và không so ngang GPU/software. Không sửa preset, màu/physics hạt hay camera exposure. Chưa kiểm UI/GPU/camera thật trên máy cậu.</p><p>Source <code>__SOURCE__</code> · <a href="manifest.json">Manifest</a></p>
<script>const m=__DATA__,$=id=>document.getElementById(id);let generation=0;
async function show(){
 const gen=++generation,e=$('engine').value,s=$('scene').value,t=$('state').value,name=`${e}-${s}-${t}.png`;
 const images=[$('pair'),$('ramp'),$('ar')];for(const im of images)im.hidden=true;
 $('metrics').textContent='Đang giải mã ảnh native…';$('metrics').dataset.id='';
 $('pair').src='pairs/'+name;$('ramp').src='ramps/'+name;$('ar').src='gpu-ar-'+t+'.png';
 $('left').textContent=e==='gpu'?'Trước sửa · 0%':'Ép đèn tắt · reference';
 try{await Promise.all(images.map(im=>im.decode()));if(gen!==generation)return;
  const get=(p,v)=>m.captures.find(c=>c.Engine===e&&c.Scene===s&&c.State===t&&c.Percent===p&&c.Variant===v);
  const a=get(0,e==='gpu'?'legacy':'forced-off'),b=get(0,'fixed');
  $('metrics').textContent=`${e} / ${s} / ${t} · Mean RGB ROI: ${a.KeyboardMean.toFixed(2)} → ${b.KeyboardMean.toFixed(2)}. `+(e==='gpu'?'Các mốc đã đo trong 25–200%: fixed/legacy có native pixel hash bằng nhau.':'Đèn chính software giữ nguyên mapping tuyến tính.');
  for(const im of images)im.hidden=false;$('metrics').dataset.id=e+'-'+s+'-'+t;
 }catch(error){if(gen===generation)$('metrics').textContent='Không giải mã được ảnh native; thử tải lại trang.';}
}for(const id of ['engine','scene','state'])$(id).onchange=show;show();
</script></html>'''

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8782);p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(SimpleHTTPRequestHandler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
