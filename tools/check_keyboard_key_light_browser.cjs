// Real Chromium QA for exact native key-light crops and all eight view controls.
// NODE_PATH=/path/to/deps/node_modules node tools/check_keyboard_key_light_browser.cjs URL [screenshot.png]
const assert=require('node:assert/strict');
const puppeteer=require('puppeteer-core');
(async()=>{
 let executablePath=process.env.CHROME_BIN,args=['--no-sandbox'];
 if(!executablePath){const mod=require('@sparticuz/chromium'),c=mod.default||mod;executablePath=await c.executablePath();args=c.args;}
 const browser=await puppeteer.launch({executablePath,args,headless:true});
 try{
  const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(String(e)));
  const base=(process.argv[2]||'http://127.0.0.1:8782').replace(/\/$/,'');
  await page.setViewport({width:1200,height:1050});await page.goto(base,{waitUntil:'networkidle0'});
  const checked=await page.evaluate(async()=>{
   const m=await(await fetch('manifest.json')).json();let panes=0,derived=0;
   async function image(src){const im=new Image();im.src=src;await im.decode();return im;}
   async function hash(im,x,y,w,h){const c=document.createElement('canvas');c.width=w;c.height=h;const ctx=c.getContext('2d',{willReadFrequently:true});ctx.drawImage(im,-x,-y);const p=ctx.getImageData(0,0,w,h).data;for(let i=0;i<p.length;i+=4){const r=p[i];p[i]=p[i+2];p[i+2]=r;}return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',p))).map(x=>x.toString(16).padStart(2,'0')).join('').toUpperCase();}
   for(const s of m.sheets){
    const im=await image(s.file);
    if(await hash(im,0,0,s.width,s.height)!==s.hash)throw Error('Browser sheet changed '+s.file);
    for(const p of s.panes){if(await hash(im,p.x,p.y,p.width,p.height)!==p.hash)throw Error('Browser pane changed '+p.id);panes++;}
    if(s.file.includes('-ar-'))continue;
    const pair=await image('pairs/'+s.file),ramp=await image('ramps/'+s.file);
    for(let i=0;i<2;i++){const p=s.panes[i];if(await hash(pair,i*p.width,0,p.width,p.height)!==p.hash)throw Error('Pair crop changed');derived++;}
    for(let i=0;i<5;i++){const p=s.panes[i+2];if(await hash(ramp,i*p.width,0,p.width,p.height)!==p.hash)throw Error('Ramp crop changed');derived++;}
   }
   return {panes,derived};
  });assert.deepEqual(checked,{panes:60,derived:56});
  for(const e of ['gpu','software'])for(const s of ['isolated','mixed'])for(const t of ['idle','held']){
   await page.select('#engine',e);await page.select('#scene',s);await page.select('#state',t);
   await page.waitForFunction(id=>document.getElementById('metrics').dataset.id===id,{},e+'-'+s+'-'+t);
   assert(await page.$$eval('#pair,#ramp,#ar',xs=>xs.every(im=>!im.hidden&&im.complete&&im.naturalWidth>0)));
   assert((await page.$eval('#pair',im=>im.src)).endsWith(`/pairs/${e}-${s}-${t}.png`));
   assert.equal(await page.$eval('#left',x=>x.textContent),e==='gpu'?'Trước sửa · 0%':'Ép đèn tắt · reference');
   assert((await page.$eval('#ar',im=>im.src)).endsWith(`/gpu-ar-${t}.png`));
  }
  await page.select('#engine','gpu');await page.select('#scene','mixed');await page.select('#state','idle');
  await page.waitForFunction(()=>document.getElementById('metrics').dataset.id==='gpu-mixed-idle');
  if(process.argv[3])await page.screenshot({path:process.argv[3],fullPage:true});
  await page.setViewport({width:390,height:844});await page.goto(base,{waitUntil:'networkidle0'});
  await page.waitForFunction(()=>document.getElementById('metrics').dataset.id==='gpu-mixed-idle');
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.deepEqual(errors,[]);
  console.log('PASS real Chromium: 60 native panes + 56 derived exact crop halves, all 8 renderer/scene/key-state combinations, correct reference labels, decode-safe controls, mobile/no page errors. Not realtime/GPU/camera acceptance.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
