#!/usr/bin/env python3
"""Verify and serve exact native AR fixed-cap trial crops and temporal slices."""
import argparse,gzip,html,io,json
from functools import partial
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
import review_ar_particle_budget as review
ROOT=Path(__file__).resolve().parents[1]

def build(repo,output):
    evidence=repo/'docs/evidence/ar-particle-budget-v1/manifest.json.gz';m=review.read(evidence);output.mkdir(parents=True,exist_ok=True)
    for s in m['sheets']+m['motions']:
        source=repo/'docs/previews/ar-particle-budget-v1'/s['file'];target=output/s['file']
        if not target.exists() and not target.is_symlink():target.symlink_to(source)
        elif not target.is_symlink() or target.resolve()!=source.resolve():raise ValueError('Wrong native budget asset')
    for name,data in [('manifest.json.gz',evidence.read_bytes()),('manifest.json',gzip.decompress(evidence.read_bytes()))]:
        if (output/name).is_symlink():raise ValueError('Refusing manifest symlink')
        (output/name).write_bytes(data)
    m=review.verify(output,source_root=repo,published_only=True)
    for t in m['motions']:
        folder=output/'motion'/t['id'];folder.mkdir(parents=True,exist_ok=True)
        for i,im in enumerate(review.motion_frames(output,t)):im.save(folder/f'{i}.png',compress_level=9)
    page=TEMPLATE.replace('__SOURCE__',html.escape(m['sourceRevision'])).replace('__DATA__',json.dumps(m,separators=(',',':')).replace('<','\\u003c'))
    (output/'index.html').write_text(page,encoding='utf-8')
    print('PASS native budget publication: 904 records, 24 static panes, 64 temporal halves, exact source/pixels/selection guards. Not hardware/FPS or default promotion.',flush=True)
    return m

TEMPLATE='''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>AR · chia cap 192</title>
<style>:root{color-scheme:dark}body{font:16px system-ui;background:#10141b;color:#e1e9f3;max-width:1050px;margin:24px auto;padding:16px;line-height:1.65}h1,h2{line-height:1.3}a{color:#9bcfff}select,button{font:inherit;max-width:100%;padding:9px;background:#1b2637;color:inherit;border:1px solid #43536a;border-radius:8px;margin:4px 0}label{display:inline-block;max-width:100%;margin-right:14px}figure{margin:20px 0;padding:12px;border:1px solid #354055;border-radius:12px}figcaption{display:grid;grid-template-columns:1fr 1fr;text-align:center;gap:12px;margin:12px 0}img{display:block;image-rendering:pixelated;max-width:100%;margin:auto;width:880px}code{word-break:break-all}.note{border-left:3px solid #e7bc7b;padding-left:14px}output{display:block;padding:16px;background:#1b2637;border-radius:10px}input[type=range]{width:100%;margin:14px 0}</style>
<h1>AR · giữ cap 192, thử chia lại phần hạt</h1>
<p class="note"><b>Trial mặc định tắt.</b> Không thêm hạt, tăng cap, sửa physics, artwork, kích thước hay alpha. Trái: lựa chọn hiện hành ưu tiên impact/release trước held. Phải: luân phiên origin rồi pitch trên tập <b>đã qua alpha/projectability</b>, mượn phần còn trống; sau khi chọn vẫn giữ thứ tự vẽ cũ giữa các hạt được giữ. Đây là thay đổi selection/identities và compositing có chủ ý, không phải material-only hay bảo đảm hai output phải giống nhau.</p>
<p>Giới hạn chưa đầy: giữ đúng từng packet/pixel cũ. Đối chứng còn kiểm default fast path với bản legacy có inventory, rồi so với native ensemble đã đóng băng; Stage không đổi. Quét eligibility toàn tập và dùng queues có thêm chi phí CPU/allocations trong trial: chưa phải bản tối ưu hiệu năng/hardware.</p>
<label>Cụm<br><select id="recipe"><option value="embers-wisps">Embers · Wisps · Shatter · Float Up</option><option value="splash-flames">Splash · Flames · Melt · Dissolve</option></select></label>
<label>Camera pose<br><select id="view"><option value="ar-reference">AR · reference</option><option value="ar-oblique">AR · oblique</option><option value="ar-near-overhead">AR · gần overhead</option></select></label>
<h2>Hợp âm giữ f33 / nhả khi pedal còn xuống f38</h2>
<figure><figcaption><span>Hiện hành · cap 192</span><span>Trial · vẫn cap 192</span></figcaption><img id="sheet" alt="Native paired crop: hàng trên f33 held, hàng dưới f38 release-pedal; legacy trái và trial phải"></figure>
<output id="metrics">Đang giải mã ảnh native…</output>
<h2>16 frame native quanh lúc nhả · AR reference</h2>
<p>Phần chuyển động dưới đây <b>luôn là AR reference</b>, không đổi theo pose selector phía trên. Hai nửa cùng frame, không nội suy. f29–35 đang giữ; Note Off trước step f36, pedal còn xuống đến f53. Đây chỉ là 16 frame ở 30 Hz, không phải full lifecycle hoặc benchmark cadence của app.</p>
<figure><figcaption><span>Hiện hành</span><span>Trial · cùng frame</span></figcaption><img id="motion" alt="Native paired temporal frame, legacy trái và trial phải"></figure>
<div><button id="play">Phát</button> <button id="pause">Dừng</button> <button id="prev">← Frame</button> <button id="next">Frame →</button></div><input id="seek" type="range" min="0" max="15" value="4" aria-label="Frame"><output id="status"></output>
<p>Đọc số lượng theo eligible/selected, không suy từ raw rằng mọi hạt đều chiếu được hoặc nhìn rõ. Origin 0 = impact/morph; 1 = release; 2 = held. Không đánh đồng “có trong draw list” với chất lượng nghệ thuật hay đủ footprint trên mọi camera. Không tự promote trial hoặc đổi Stage depth/Settings.</p>
<p>904 captures: 840 static (5 recipes × 2 palettes × 4 views × 7 checkpoints × 3 modes) + 64 motion frames. 140 default captures đối chiếu frozen ensemble; nguyên quad/RGBA được kiểm với inventory và actual upload; non-particle quads giữ nguyên. Subset này: 2 recipes, Prism/Key light 100, ba AR poses, hai checkpoint và hai temporal slices AR reference. Các full frames/controls/inventories còn lại ở artifact CI, không giả vờ hiển thị ở đây.</p>
<p>Source <code>__SOURCE__</code> · <a href="manifest.json">Manifest</a></p>
<script>const m=__DATA__,$=id=>document.getElementById(id);let gen=0,mg=0,index=4,playing=false,timer=null;
const record=(r,v,phase,f,mode)=>m.captures.find(c=>c.Recipe===r&&c.Palette==='prism'&&c.View===v&&c.Phase===phase&&c.Frame===f&&c.Mode===mode);
function counts(c,role){return c.Selected.filter(i=>Math.floor(c.EligibleKeys[i]/128)===role).length;}
async function show(){const g=++gen,r=$('recipe').value,v=$('view').value,im=$('sheet');im.hidden=true;$('metrics').dataset.id='';$('metrics').textContent='Đang giải mã ảnh native…';im.src=r+'-'+v+'.png';
try{await im.decode();if(g!==gen)return;$('metrics').replaceChildren();for(const [phase,f] of [['chord-held',33],['release-pedal',38]]){const a=record(r,v,phase,f,'legacy'),b=record(r,v,phase,f,'fair'),p=document.createElement('p');p.textContent=`f${f}: ${a.EligibleKeys.length} eligible, ${a.RawParticles} raw. Selected ${a.Selected.length} → ${b.Selected.length}; impact ${counts(a,0)} → ${counts(b,0)}, release ${counts(a,1)} → ${counts(b,1)}, held ${counts(a,2)} → ${counts(b,2)}.`;$('metrics').append(p);}im.hidden=false;$('metrics').dataset.id=r+'-'+v;
}catch(e){if(g===gen)$('metrics').textContent='Không giải mã được ảnh native; thử tải lại.';}}
function stop(){playing=false;clearTimeout(timer);timer=null;}
async function frame(){const g=++mg,r=$('recipe').value,n=index,im=$('motion');im.hidden=true;$('status').dataset.id='';im.src=`motion/${r}/${n}.png`;
try{await im.decode();if(g!==mg)return;const f=n+29,a=record(r,'ar-reference','motion',f,'legacy'),b=record(r,'ar-reference','motion',f,'fair');im.hidden=false;$('seek').value=n;$('status').textContent=`Frame ${f} · ${f<36?'Đang giữ hợp âm':'Đã nhả phím · pedal on'} · held selected ${counts(a,2)} → ${counts(b,2)} · cap 192`;$('status').dataset.id=r+'-'+n;
if(playing)timer=setTimeout(()=>{index=(index+1)%16;frame();},1000/30);
}catch(e){if(g===mg){stop();$('status').textContent='Không giải mã được frame native.';}}}
$('recipe').onchange=()=>{stop();show();frame();};$('view').onchange=show;
$('seek').oninput=()=>{stop();index=+$('seek').value;frame();};$('prev').onclick=()=>{stop();index=Math.max(0,index-1);frame();};$('next').onclick=()=>{stop();index=Math.min(15,index+1);frame();};$('pause').onclick=stop;$('play').onclick=()=>{if(playing)return;playing=true;frame();};show();frame();</script></html>'''

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8784);p.add_argument('--verify-only',action='store_true');a=p.parse_args();build(a.repo.resolve(),a.output.resolve())
    if not a.verify_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(SimpleHTTPRequestHandler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
