'use strict';
// Deliberately disconnected from WPF, MIDI, devices and persisted app settings.
const $=s=>document.querySelector(s), el=(tag,cls,text)=>{const n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=text;return n;};
const clone=v=>JSON.parse(JSON.stringify(v));
let data,presets,values,baseline,lang='vi',mode='stage',route='overview',query='',presetName='';
const bi=(vi,en)=>lang==='vi'?vi:en;
const norm=s=>String(s||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/[đĐ]/g,'d').toLowerCase();
const routeInfo=id=>data.routes.find(x=>x.id===id)||{vi:'Bản đồ cũ → mới',en:'Old → new map'};
const name=r=>lang==='vi'?r.labelVi:r.label;
const sourceText=r=>`${r.oldPage||'No standard row extracted'}${r.oldCard?' / '+r.oldCard:''} · ${r.property}`;
const descriptions={
 overview:['Chọn một look, rồi tinh chỉnh theo từng giai đoạn của nốt. Không phải đi qua nhiều trang để tìm cùng một hiệu ứng.','Choose a look, then refine each stage of a note. No more hunting across separate effect pages.'],
 notes:['Màu, hình dáng và chuyển động của nốt. Những điều bạn muốn đổi thường xuyên được đặt ở trên.','Note color, shape and motion. Everyday adjustments come first.'],
 press:['Khoảnh khắc chạm phím: impact, tia hạt và hình sóng. Các thông số vật lý nằm trong Nâng cao.','The moment a key is struck: impact, sparks and waves. Detailed physics stay under Advanced.'],
 hold:['Hiệu ứng trong lúc giữ phím: Wisps, Flames và nhịp thở. Giữ đúng look đã duyệt.','Sustained-key effects: Wisps, Flames and breathing. Preserve the approved look.'],
 release:['Cách hiệu ứng kết thúc sau khi thả phím. Không trộn lẫn với phần nhấn hoặc phần giữ.','How effects finish after a key is released, separate from press and hold.'],
 lighting:['Ánh sáng phím, hậu kỳ và bộ điều biến. Phân biệt điều khiển Stage và AR bằng nhãn rõ ràng.','Key lighting, post-processing and modulation, with explicit Stage and AR labels.'],
 stage:['Bàn phím, nền và không khí của sân khấu. Không đưa calibration vào nhóm này.','Keyboard, background and atmosphere. Camera calibration stays separate.'],
 camera:['Nguồn camera, căn chỉnh, đường đi 3D, timing và theo dõi bàn tay — giữ nguyên đầy đủ các lối vào.','Camera sources, calibration, 3D paths, timing and hand tracking — all existing entry points retained in the map.'],
 recording:['Ghi hình và xuất video. Các nút nghiệp vụ chỉ là tham chiếu bố cục, không chạy trong prototype.','Capture and video. Business actions are layout references, not functional recording controls.'],
 audio:['Piano, hiệu ứng âm thanh và MIDI. Prototype không tải piano hay mở thiết bị.','Piano, audio effects and MIDI. This prototype does not download instruments or open devices.'],
 practice:['Tùy chọn luyện tập, biểu đồ, ghost và lịch sử. Không có dữ liệu luyện tập thật trong bản xem.','Practice options, charts, ghosts and history. No real session data is loaded here.'],
 app:['Giao diện, ngôn ngữ, hiệu năng, trợ năng và hồ sơ. Bố cục này không thay đổi cấu hình ứng dụng.','Theme, language, performance, accessibility and profiles. This layout never changes app configuration.'],
 inventory:['Đối chiếu 13 trang hiện tại với đề xuất mới. Các liên kết trang cũ được giữ ở bản đồ này để rà soát migration sau.','Cross-reference all 13 current pages. Old-page links remain in this map for the later migration review.']
};
const desc=id=>(descriptions[id]||descriptions.overview)[lang==='vi'?0:1];
const titleText=id=>routeInfo(id)[lang==='vi'?'vi':'en'];
const matches=r=>!query||norm([r.property,r.label,r.labelVi,r.tooltip,r.tooltipVi,r.oldPage,r.oldCard,r.handler,r.name,r.kind, ...(data.routes.filter(x=>x.id===r.route).flatMap(x=>[x.vi,x.en])), 'settings cai dat', ...(r.property?.startsWith('Wisp')?['khoi wisps giu phim']:[]), ...(r.property?.startsWith('Flame')?['lua flames giu phim']:[])].join(' ')).includes(norm(query));
const dirtyCount=()=>Object.keys(values).filter(k=>JSON.stringify(values[k])!==JSON.stringify(baseline[k])).length;
function setDirty(){ $('#dirty').textContent=dirtyCount()?bi(`${dirtyCount()} giá trị đã đổi trong bản nháp`,`${dirtyCount()} draft values changed`):bi('Bản nháp chưa chỉnh sửa','Unmodified draft'); }
function toast(text){const n=$('#toast');n.textContent=text;n.style.display='block';clearTimeout(toast.timer);toast.timer=setTimeout(()=>n.style.display='none',3000);}
function header(){
 document.documentElement.lang=lang;
 const texts={ 'notice-title':bi('BẢN BỐ CỤC TƯƠNG TÁC','INTERACTIVE LAYOUT PROPOSAL'),'notice-detail':bi('Không kết nối app · Không phát MIDI/âm thanh · Không ghi cài đặt thật','No app connection · No MIDI/audio · No real settings saved'),'brand-sub':bi('Không gian cài đặt','Settings workspace'),'inventory-button':bi('Bản đồ cũ → mới','Old → new map'),'inventory-link':bi('Inventory từ source ↗','Source inventory ↗'),'shared-note':bi('Một bộ giá trị dùng chung. Chuyển mode không ghi đè look hay calibration.','One shared set of values. Switching context never replaces a look or calibration.'),'footer-note':bi('Chỉ trong phiên xem này · Không tự lưu vào app','This viewing session only · Never autosaved to the app'),'reset':bi('Khôi phục nhóm…','Reset section…'),'export':bi('Xuất bản nháp','Export draft')};
 for(const [id,text] of Object.entries(texts))document.getElementById(id).textContent=text;
 $('#search').placeholder=bi('Tìm một cài đặt…','Find a setting…');$('#search').setAttribute('aria-label',bi('Tìm cài đặt bằng VI hoặc EN','Search settings in VI or EN'));$('#language').textContent=lang==='vi'?'VI / EN':'EN / VI';
 document.querySelectorAll('[data-mode]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.mode===mode)));
 $('#context').textContent=mode==='ar'?'AR MODE':'LIVE STAGE';
 const nav=$('#nav');nav.replaceChildren();
 data.routes.forEach((r,i)=>{
  if([0,6,8].includes(i)){const p=el('div','nav-label'+(i?' nav-break':''),i===0?bi('THIẾT KẾ HIỆU ỨNG','EFFECT DESIGN'):i===6?bi('KHÔNG GIAN','ENVIRONMENT'):bi('PHIÊN & ỨNG DỤNG','SESSION & APP'));nav.append(p);}
  const a=el('a','nav-item');a.href='#'+r.id;a.dataset.route=r.id;if(route===r.id&&!query)a.setAttribute('aria-current','page');a.append(el('span','nav-number',String(i).padStart(2,'0')),el('span','',r[lang==='vi'?'vi':'en']));nav.append(a);
 });setDirty();
}
function scopeBadge(r){const t=r.scope==='ar'?'AR':r.scope==='stage'?'Stage':r.scope==='unclassified'?bi('Chưa phân loại','Unclassified'):bi('Dùng chung','Shared');return el('span','badge '+r.scope,t);}
function inactive(r){return r.scope==='ar'&&mode!=='ar'||r.scope==='stage'&&mode!=='stage';}
const units={ParticleLife:'s',ParticleSize:'px',NoteMinLength:'px',NoteGap:'px',NoteFallSpeed:'px/s',WispSpeed:'px/s',ParticleAmount:'',ParticleVelocity:'',Gravity:'',VectorField:'',WispAmount:'',HandSplitPitch:'MIDI',PaletteLowPitch:'MIDI',PaletteHighPitch:'MIDI',RecordingFrameRate:'fps',ArLookahead:'s',ArDelay:'ms',ArNoteDirectionTilt:'°',ArNoteDirectionAzimuth:'°',ShaderCameraTilt:'°'};
function displayValue(r,value){const u=Object.hasOwn(units,r.property)?units[r.property]:'%';return String(value)+(u?' '+u:'');}
function field(r){
 const off=inactive(r),stored=r.kind==='Stored';const row=el('div','control'+(off?' disabled':''));row.dataset.property=r.property;row.dataset.scope=r.scope;
 const left=el('div'),label=el('label','control-label',name(r));label.htmlFor='field-'+r.property;label.append(scopeBadge(r));left.append(label);
 const tip=lang==='vi'?r.tooltipVi:r.tooltip;if(tip)left.append(el('p','control-desc',tip));
 if(off)left.append(el('p','control-desc',bi(`Không dùng trong ngữ cảnh ${mode==='ar'?'AR':'Live Stage'} này; giá trị được giữ nguyên.`,`Not applicable in this ${mode==='ar'?'AR':'Live Stage'} context; its value is preserved.`)));
 if(r.visibleWhen)left.append(el('p','provenance',bi('Điều kiện hiển thị gốc (chưa nối lại): ','Original visibility dependency (not wired here): ')+r.visibleWhen));
 left.append(el('div','provenance',sourceText(r)));row.append(left);
 const widget=el('div','setting-widget');let input;const value=values[r.property];
 if(stored){widget.append(el('code','',String(value)));left.append(el('p','control-desc',bi('Chỉ được lưu trong model; chưa tìm thấy row bằng bộ trích xuất. Không tạo thêm điều khiển app.','Stored in the model; no row found by this extractor. No new app control is implied.')));}
 else if(typeof value==='boolean'){input=el('input');input.type='checkbox';input.checked=value;}
 else if(r.kind==='Choice'&&r.options?.length){input=el('select');const options=[...r.options];if(!options.some(o=>o.value===value))options.unshift({value,label:String(value),labelEn:String(value)});for(const o of options){const n=el('option','',lang==='vi'?o.label:o.labelEn);n.value=o.value;input.append(n);}input.value=value;}
 else if(r.kind==='SliderRow'&&r.min!==null&&r.max!==null){input=el('input');input.type='range';input.min=r.min;input.max=r.max;input.step=Number.isInteger(value)&&r.max-r.min>10?1:Math.min(.01,(r.max-r.min)/100);input.value=value;const out=el('output','value',displayValue(r,value));out.htmlFor='field-'+r.property;widget.append(input,out);input.addEventListener('input',()=>{out.value=displayValue(r,Number(input.value));});}
 else if(r.kind==='ColorRow'&&/^#[\da-f]{6,8}$/i.test(value)){input=el('input');input.type='color';input.value=value.length===9?'#'+value.slice(3):value;widget.append(el('code','',value));}
 else {widget.append(el('code','',String(value)));left.append(el('p','control-desc',bi('Tham chiếu bố cục · giá trị này cần điều khiển chuyên biệt trong WPF.','Layout reference · this value needs a dedicated WPF control.')));}
 if(input){input.id='field-'+r.property;input.disabled=off;input.dataset.property=r.property;if(!input.parentNode)widget.append(input);input.addEventListener('input',()=>{let v=input.type==='checkbox'?input.checked:input.type==='range'?Number(input.value):input.type==='color'?(value.length===9?value.slice(0,3)+input.value.slice(1):input.value):input.value;values[r.property]=v;if(input.type==='color')widget.querySelector('code').textContent=v;setDirty();});}
 row.append(widget);return row;
}
function section(title,subtitle,rows){
 const s=el('section','section'),head=el('div','section-head'),txt=el('div');txt.append(el('h2','',title));if(subtitle)txt.append(el('p','',subtitle));head.append(txt,el('span','minor',`${rows.length} ${bi('mục','items')}`));s.append(head);
 const basic=rows.filter(r=>!r.advanced),advanced=rows.filter(r=>r.advanced);for(const r of basic)s.append(field(r));
 if(advanced.length){const d=el('details'),sum=el('summary','',bi(`Nâng cao · ${advanced.length} thông số`,`Advanced · ${advanced.length} parameters`));d.open=Boolean(query);d.append(sum);for(const r of advanced)d.append(field(r));s.append(d);}
 return s;
}
function referencePanel(actions,xaml,references=[]){
 const panel=el('section','section'),heading=el('div','section-head');heading.append(el('h2','',bi('Lối vào & thao tác được giữ lại','Retained entry points & actions')));panel.append(heading);
 const body=el('div','section-body');body.append(el('p','control-desc',bi('Tham chiếu vị trí trong bố cục — không kết nối nghiệp vụ. Import, thiết bị, calibration, lịch sử và hồ sơ sẽ dùng handler thật khi chuyển sang WPF.','Placement references only — no business handlers connected. Import, devices, calibration, history and profiles will use their original handlers in WPF.')));
 const list=el('div','action-list');for(const a of actions){const b=el('button','action-ref',name(a));b.disabled=true;b.title=bi('Chỉ tham chiếu · chưa nối handler thật','Reference only · not connected to real handler');list.append(b);}body.append(list);panel.append(body);
 for(const r of references){const row=el('div','reference');row.append(el('span','',name(r)),el('code','',r.oldPage+' / '+r.oldCard+' · '+r.host));panel.append(row);}
 for(const r of xaml){const row=el('div','reference');row.append(el('span','',name(r)),el('code','',r.oldPage+' · '+(r.name||Object.values(r.events).join(', ')||r.kind)+(r.options?.length?' · '+r.options.join(' / '):'')+(r.enabled===false?' · disabled in app':'')));panel.append(row);}
 return panel;
}
function jump(id,number){const a=el('a','jump');a.href='#'+id;a.append(el('span','num',`${number} / ${bi('GIAI ĐOẠN','STAGE')}`),el('h3','',titleText(id)),el('p','',desc(id)),el('span','arrow','↗'));return a;}
function overview(container){
 const hero=el('div','hero'),copy=el('div');copy.append(el('small','',bi('LOOK HIỆN TẠI · BẢN NHÁP','CURRENT LOOK · DRAFT')),el('h2','',presetName||bi('Giá trị mặc định từ model','Model defaults')),el('p','',bi('Cùng một ngôn ngữ hình ảnh cho Live Stage và AR. Các điều khiển riêng từng ngữ cảnh vẫn ở đúng nơi của chúng.','One visual language for Live Stage and AR, with context-specific controls kept distinct.')));const side=el('div','hero-side');side.append(el('strong','','05'),el('span','',bi('giai đoạn thiết kế','design stages')));hero.append(copy,side);container.append(hero);
 const bar=el('div','preset-bar'),label=el('label','',bi('Thử preset cục bộ','Try a local preset'));label.htmlFor='preset';const select=el('select');select.id='preset';const initial=el('option','',bi('Chọn preset để thử…','Choose a preset to try…'));initial.value='';select.append(initial);for(const key of Object.keys(presets)){const option=el('option','',key);option.value=key;select.append(option);}select.value=presetName;select.addEventListener('change',()=>{if(!select.value)return;applyPreset(select.value);render();toast(bi('Đã áp dụng vào bản nháp; không ghi app, không thay cấu hình máy.','Applied to the draft only; app and machine settings are untouched.'));});bar.append(label,select);container.append(bar,el('p','count',bi('6 preset cộng đồng để thử bố cục, không thay thế toàn bộ danh sách preset tích hợp trong app. Chọn preset tạo mốc khôi phục mới trong phiên xem.','Six community presets for layout exploration, not the full built-in app catalogue. Selecting one creates a new draft reset baseline.')));
 const grid=el('div','grid');['notes','press','hold','release','lighting'].forEach((id,i)=>grid.append(jump(id,String(i+1).padStart(2,'0'))));container.append(grid);
 const hints=el('div','hint',bi('Điều khiển phổ biến trước, vật lý ở Nâng cao. Audio/MIDI, luyện tập/lịch sử, ghi hình, theme và camera vẫn có khu vực riêng.','Common controls first, physics in Advanced. Audio/MIDI, practice/history, recording, theme and camera remain accessible.'));container.append(hints);
}
function inventoryView(container){
 const occurrences=data.occurrences.length,exposed=data.controls.filter(r=>r.kind!=='Stored').length;
 container.append(el('div','hint gold',bi(`${Object.keys(data.oldRoutes).length} trang cũ · ${exposed} thuộc tính có row được trích xuất · ${occurrences} lần xuất hiện · ${data.xamlControls.length} tham chiếu XAML. Đây là đối chiếu source, chưa phải chứng nhận migration hoàn tất.`,`${Object.keys(data.oldRoutes).length} old pages · ${exposed} properties with extracted rows · ${occurrences} row occurrences · ${data.xamlControls.length} XAML references. This is a source cross-reference, not certification of a completed migration.`)));
 const s=el('section','section');
 for(const [old,targets] of Object.entries(data.oldRoutes)){const row=el('div','map-row');const oldLink=el('a','old',old);oldLink.href='#old/'+old;const dest=el('div');for(const target of targets){const a=el('a','',titleText(target)+' ↗');a.href='#'+target;dest.append(a);}row.append(oldLink,dest,el('small','',bi('giữ route','retain route')));s.append(row);}container.append(s);
 const stored=data.controls.filter(r=>r.kind==='Stored');container.append(section(bi('Ngoài bộ dựng row chuẩn — rà soát riêng','Outside standard row extraction — separate review'),bi('Không tự biến thuộc tính lưu trữ thành control mới. Trích xuất có giới hạn; runtime rows và nghiệp vụ chưa được mô phỏng.','Stored properties do not automatically become new controls. Extraction has limits; runtime rows and business operations are not simulated.'),stored));
}
function render(){
 header();$('#title').textContent=query?bi(`Tìm “${query}”`,`Search “${query}”`):titleText(route);$('#subtitle').textContent=query?bi('Tìm bằng tiếng Việt hoặc tiếng Anh, kể cả tên thuộc tính và trang cũ. Kết quả giữ cùng một giá trị bản nháp.','Search Vietnamese or English, including property names and old pages. Results share the same draft values.'):desc(route);$('#crumb').textContent=query?bi('TÌM TRONG TOÀN BỘ CÀI ĐẶT','SEARCH ALL SETTINGS'):route==='inventory'?bi('ĐỐI CHIẾU MIGRATION','MIGRATION CROSS-REFERENCE'):bi('CÀI ĐẶT / BẢN ĐỀ XUẤT','SETTINGS / PROPOSAL');
 const container=$('#content');container.replaceChildren();
 if(route==='inventory'&&!query){inventoryView(container);return;}
 if(route==='overview'&&!query)overview(container);
 if(route==='hold'&&!query){const hint=el('div','hint',bi('Spark và emitter physics dùng chung cho nhấn/giữ ở engine hiện tại; prototype không tạo hai bộ tham số độc lập. ','Spark and emitter physics are shared by press/hold in the current engine; this proposal does not invent independent parameter sets. '));const a=el('a','',bi('Đi tới Nhấn phím →','Open Press →'));a.href='#press';hint.append(a);container.append(hint);}
 if(route==='camera'&&!query)container.append(el('div','hint gold',bi('Camera và calibration là cấu hình máy. Chuyển Stage/AR chỉ đổi ngữ cảnh xem, không thay nguồn, căn chỉnh hay timing. Các nút thiết bị chỉ là tham chiếu.','Camera and calibration are machine-local. Switching Stage/AR only changes viewing context, never sources, alignment or timing. Device actions are references only.')));
 const rows=data.controls.filter(r=>(query?matches(r):r.route===route));
 const groups=new Map();for(const r of rows){const key=query?r.route:r.oldCard||bi('Điều khiển','Controls');if(!groups.has(key))groups.set(key,[]);groups.get(key).push(r);}
 const references=data.references.filter(r=>(query?matches(r):r.route===route));
 const actions=data.actions.filter(r=>(query?matches(r):r.route===route));const xaml=data.xamlControls.filter(r=>(query?matches(r):r.route===route));
 if(query)container.append(el('div','count',bi(`${rows.length} thuộc tính · ${actions.length+xaml.length+references.length} tham chiếu thao tác/host`,`${rows.length} properties · ${actions.length+xaml.length+references.length} action/host references`)));
 for(const [key,items] of groups){const title=query?titleText(key):groupTitle(key);container.append(section(title,query?bi('Kết quả từ các trang hiện tại','Results from current pages'):bi('Phổ biến ở trên · chi tiết trong Nâng cao','Everyday controls first · details in Advanced'),items));}
 if(actions.length||xaml.length||references.length)container.append(referencePanel(actions,xaml,references));
 if(!rows.length&&!actions.length&&!xaml.length&&!references.length&&route!=='overview')container.append(el('div','empty',bi('Không tìm thấy. Thử “wisps”, “giữ phím”, “camera” hoặc “MIDI”.','No matches. Try “wisps”, “hold”, “camera” or “MIDI”.')));
}
function groupTitle(s){const titles={'COLOR':'Màu sắc','SHAPE & STYLE':'Hình dáng & phong cách','GLOW & EDGES':'Viền & độ sáng','MOTION':'Chuyển động','FALLING FX':'Hiệu ứng nốt rơi','IMPACT':'Va chạm','HOLD':'Giữ phím','HOLD FX':'Hiệu ứng giữ phím','RELEASE FX':'Hiệu ứng thả phím','IMPACT · WAVE & FLASH':'Sóng, flash & va chạm','SPARKS · EMITTER':'Spark · bộ phát hạt','SPARKS · PHYSICS':'Spark · vật lý','PARTICLE APPEARANCE':'Diện mạo hạt','KEYBOARD SIZE':'Kích thước bàn phím','KEYBOARD':'Bàn phím','CAMERAS ON THIS MACHINE':'Camera trên máy','AR OVERLAY':'Lớp AR','KEYBOARD ALIGNMENT':'Căn chỉnh bàn phím','AR PLAYBACK':'Phát lại AR','3D NOTE PATH':'Đường đi nốt 3D','GRAPHICS ENGINE':'Bộ máy đồ họa','SETTINGS PROFILE':'Hồ sơ cài đặt','AMBIENT LAYERS':'Các lớp môi trường','RELEASE':'Thả phím','APPEARANCE':'Diện mạo','SPARKS — EMITTER & PHYSICS':'Spark · phát hạt & vật lý','WISPS':'Wisps · dải khói','FLAMES':'Flames · ngọn lửa','KEY LIGHTING':'Ánh sáng phím','RAY-TRACED SHADING':'Chiếu sáng & vật liệu','SMART MODULATORS':'Điều biến thông minh','POST FX':'Hậu kỳ','LAYERS':'Lớp hình ảnh','INTERFACE THEME':'Theme giao diện','USER THEMES':'Theme cá nhân','QUICK LOOKS':'Look nhanh','SIZE':'Kích thước','ATMOSPHERE':'Không khí','AMBIENT':'Môi trường','HIT LINE':'Vạch chạm','BACKGROUND':'Nền','FRAMING':'Khung hình','DEVICES':'Thiết bị','WEBCAM OVERLAY':'Lớp camera','ALIGNMENT':'Căn chỉnh','PLAYBACK':'Phát lại','3D PATH':'Đường đi 3D','LIVE KEY RESPONSE':'Phản hồi phím trực tiếp','STREAM & TIMING':'Luồng & thời gian','HAND TRACKING':'Theo dõi bàn tay','LANGUAGE':'Ngôn ngữ','ENGINE':'Bộ máy hiển thị','ACCESSIBILITY':'Trợ năng','UPDATES':'Cập nhật','PROFILE':'Hồ sơ'};return lang==='vi'?titles[s]||s:s;}
function applyPreset(key){
 const machine=new Set(data.machineLocalProperties);const next=clone(values);for(const [k,v] of Object.entries(data.defaults)){if(!machine.has(k))next[k]=clone(presets[key][k]??v);}values=next;baseline=clone(next);presetName=key;
}
function navigate(){
 let hash=decodeURIComponent(location.hash.slice(1));if(hash.startsWith('old/')){const old=hash.slice(4);route=data.oldRoutes[old]?.[0]||'inventory';toast(bi(`Lối vào cũ: ${old}. Xem bản đồ để tới các nhóm liên quan.`,`Old entry point: ${old}. See the map for its related sections.`));}
 else route=data.routes.some(r=>r.id===hash)||hash==='inventory'?hash:'overview';
 query='';$('#search').value='';render();
}
function resetRows(){return data.controls.filter(r=>r.kind!=='Stored'&&!inactive(r)&&(query?matches(r):r.route===route));}
async function main(){
 [data,presets]=await Promise.all(['inventory.json','presets.json'].map(async p=>{const res=await fetch(p);if(!res.ok)throw Error(p+': '+res.status);return res.json();}));
 values=clone(data.defaults);baseline=clone(values);
 $('#search').addEventListener('input',e=>{query=e.target.value.trim();render();});$('#language').addEventListener('click',()=>{lang=lang==='vi'?'en':'vi';render();});
 document.querySelectorAll('[data-mode]').forEach(b=>b.addEventListener('click',()=>{mode=b.dataset.mode;render();}));
 $('#inventory-button').addEventListener('click',()=>location.hash='inventory');window.addEventListener('hashchange',navigate);
 $('#reset').addEventListener('click',()=>{const rows=resetRows();if(!rows.length){toast(bi('Nhóm này không có giá trị bản nháp để khôi phục.','This section has no draft values to reset.'));return;}$('#dialog-title').textContent=bi('Khôi phục nhóm bản nháp?','Reset this draft section?');$('#dialog-copy').textContent=bi(`Khôi phục ${rows.length} thuộc tính trong ${query?'kết quả tìm kiếm':titleText(route)} về mốc ${presetName||'mặc định model'}. Không ảnh hưởng nhóm khác hay app thật.`,`Restore ${rows.length} properties in ${query?'search results':titleText(route)} to ${presetName||'model defaults'}. Other sections and the real app stay unchanged.`);$('#cancel-reset').textContent=bi('Hủy','Cancel');$('#confirm-reset').textContent=bi('Khôi phục bản nháp','Reset draft');$('#reset-dialog').showModal();});
 $('#cancel-reset').addEventListener('click',()=>$('#reset-dialog').close());$('#confirm-reset').addEventListener('click',()=>{for(const r of resetRows())values[r.property]=clone(baseline[r.property]);$('#reset-dialog').close();render();toast(bi('Đã khôi phục nhóm trong bản nháp.','Draft section reset.'));});
 $('#export').addEventListener('click',()=>{const payload={kind:'settings-layout-prototype',schema:1,notAnAppPreset:true,context:mode,preset:presetName||null,sourceHashes:data.sourceHashes,values};const a=el('a');a.href=URL.createObjectURL(new Blob([JSON.stringify(payload,null,2)],{type:'application/json'}));a.download='keyflow-settings-layout-draft.json';a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);toast(bi('Xuất bản nháp bố cục; file này không phải preset để import vào app.','Exported a layout draft, not a preset for importing into the app.'));});
 Object.defineProperty(window,'settingsPrototype',{value:{snapshot:()=>clone({values,baseline,lang,mode,route,presetName}),inventory:()=>clone(data)},writable:false});navigate();
}
main().catch(error=>{$('#content').textContent=bi('Không tải được prototype: ','Could not load prototype: ')+error.message;console.error(error);});
