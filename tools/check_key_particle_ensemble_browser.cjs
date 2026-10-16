// Real browser integrity/control QA, not a rendering/FPS/hardware benchmark.
const assert=require('node:assert/strict'),puppeteer=require('puppeteer-core');
(async()=>{let executablePath=process.env.CHROME_BIN,args=['--no-sandbox'];
 if(!executablePath){const mod=require('@sparticuz/chromium'),c=mod.default||mod;executablePath=await c.executablePath();args=c.args;}
 const browser=await puppeteer.launch({executablePath,args,headless:true});
 try{const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(String(e)));
 const base=(process.argv[2]||'http://127.0.0.1:8783').replace(/\/$/,'');await page.setViewport({width:1200,height:1050});await page.goto(base,{waitUntil:'networkidle0'});
 const counts=await page.evaluate(async()=>{const m=await(await fetch('manifest.json')).json();let count=0;
 async function hash(im,x,y,w,h){const c=document.createElement('canvas');c.width=w;c.height=h;const ctx=c.getContext('2d',{willReadFrequently:true});ctx.drawImage(im,-x,-y);const p=ctx.getImageData(0,0,w,h).data;for(let i=0;i<p.length;i+=4){const r=p[i];p[i]=p[i+2];p[i+2]=r;}return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',p))).map(x=>x.toString(16).padStart(2,'0')).join('').toUpperCase();}
 for(const s of m.sheets){const im=new Image();im.src=s.file;await im.decode();if(await hash(im,0,0,s.width,s.height)!==s.hash)throw Error('Native sheet changed');for(const p of s.panes){if(await hash(im,p.x,p.y,p.width,p.height)!==p.hash)throw Error('Native pane changed');count++;}}
 return [m.captures.length,m.sheets.length,count];});assert.deepEqual(counts,[1120,6,24]);
 for(const r of ['embers-wisps','splash-flames','fireworks-wisps'])for(const v of ['live-stage','ar-reference']){await page.select('#recipe',r);await page.select('#view',v);await page.waitForFunction(id=>document.getElementById('metrics').dataset.id===id,{},r+'-'+v);assert(await page.$eval('#sheet',im=>!im.hidden&&im.complete&&im.naturalWidth===440));assert((await page.$eval('#sheet',im=>im.src)).endsWith('/'+r+'-'+v+'.png'));assert((await page.$eval('#metrics',x=>x.textContent)).includes('f38'));}
 // Several overlapping decode requests must not publish stale selection state.
 await page.evaluate(()=>{for(let i=0;i<8;i++){document.getElementById('recipe').value=i%2?'splash-flames':'fireworks-wisps';document.getElementById('recipe').dispatchEvent(new Event('change'));}document.getElementById('view').value='ar-reference';document.getElementById('view').dispatchEvent(new Event('change'));});
 await page.waitForFunction(()=>document.getElementById('metrics').dataset.id==='splash-flames-ar-reference');
 await page.select('#recipe','embers-wisps');await page.select('#view','live-stage');await page.waitForFunction(()=>document.getElementById('metrics').dataset.id==='embers-wisps-live-stage');
 if(process.argv[3])await page.screenshot({path:process.argv[3],fullPage:true});
 await page.setViewport({width:390,height:844});await page.reload({waitUntil:'networkidle0'});await page.waitForFunction(()=>document.getElementById('metrics').dataset.id==='embers-wisps-live-stage');
 assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.deepEqual(errors,[]);
 console.log('PASS real Chromium: six native sheets / 24 exact BGRA panes, six control combinations, rapid decode transitions, 390px/no overflow or page errors. Not app playback/hardware/FPS acceptance.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
