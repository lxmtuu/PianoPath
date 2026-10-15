// Real Chromium QA for native four-track burst/held review pages. Not a DOM mock.
// Dependencies: puppeteer-core and either CHROME_BIN or @sparticuz/chromium.
// Shatter single-impact label/metadata checks: add REVIEW_SINGLE_IMPACT=1.
// Usage: NODE_PATH=/path/to/deps/node_modules node tools/check_burst_particle_review_browser.cjs URL [screenshot.png]
const puppeteer=require('puppeteer-core');
const assert=require('node:assert/strict');
(async()=>{
 let executablePath=process.env.CHROME_BIN,args=['--no-sandbox'];
 if(!executablePath){const mod=require('@sparticuz/chromium'),chromium=mod.default||mod;executablePath=await chromium.executablePath();args=chromium.args;}
 const singleImpact=process.env.REVIEW_SINGLE_IMPACT==='1';
 const browser=await puppeteer.launch({args,executablePath,headless:true});
 try {
  const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(String(e)));
  const base=process.argv[2]||'http://127.0.0.1:8771';
  await page.setViewport({width:1100,height:1000});await page.goto(base,{waitUntil:'networkidle0'});
  assert.equal(await page.$$eval('img',xs=>xs.every(x=>x.complete&&x.naturalWidth>0)),true);
  const halves=await page.evaluate(async()=>{
   const m=await(await fetch('manifest.json')).json();let checked=0;
   for(const t of m.motions)for(let f=0;f<60;f++){
    const im=new Image();im.src=`motion/${t.Id}/${f}.png`;await im.decode();
    const w=t.crop.Width,h=t.crop.Height,canvas=document.createElement('canvas');canvas.width=w*2;canvas.height=h;
    const ctx=canvas.getContext('2d',{willReadFrequently:true});ctx.drawImage(im,0,0);
    for(let i=0;i<2;i++){
     const pixels=ctx.getImageData(i*w,0,w,h).data;
     for(let j=0;j<pixels.length;j+=4){const r=pixels[j];pixels[j]=pixels[j+2];pixels[j+2]=r;}
     const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',pixels))).map(x=>x.toString(16).padStart(2,'0')).join('').toUpperCase();
     if(hash!==t.tracks.find(x=>x.variant===(i?'art':'classic')).hashes[f])throw Error(`Browser pixels changed ${t.Id}/${f}/${i}`);
     checked++;
    }
   }
   return checked;
  });assert.equal(halves,480);
  await page.goto(base+'/motion-review.html',{waitUntil:'networkidle0'});
  const ready=async(mode,n)=>{
   await page.waitForFunction((mode,n)=>!document.getElementById('pair').hidden&&document.getElementById('status').textContent.includes(`${mode} · Frame ${n}/59`),{},mode,n);
   if(singleImpact){
    const status=await page.$eval('#status',x=>x.textContent);
    assert(status.includes(mode.endsWith('burst')?'Nhấn mạnh .9':'Nhấn nhẹ .45'));
    assert(status.includes('không held/release'));
    assert(status.includes(n<12?'pedal off':'pedal on'));
    assert(!/Giữ phím|Đã thả phím/.test(status));
   }
  };
  if(singleImpact){
   assert.equal(await page.$eval('#release',x=>x.textContent),'Pedal on: frame 12');
   assert.deepEqual(await page.$$eval('#mode option',xs=>xs.map(x=>x.textContent)),['Stage · nhấn nhẹ','AR · nhấn nhẹ','Stage · nhấn mạnh','AR · nhấn mạnh']);
   const m=await page.evaluate(async()=>await(await fetch('manifest.json')).json());
   assert.equal(m.family,'shatter');assert.deepEqual(m.rasterEmptyPairs.slice().sort(),['indigo-ar-near-overhead-p61-held','prism-ar-near-overhead-p61-held']);assert.deepEqual(m.staticSampleFrames,[2,10,26,89]);assert.deepEqual(m.motionPressVelocities,[.9,.45]);
   assert(m.motions.every(x=>x.emission.includes('single ')&&x.emission.includes('no held emitter; pedal f12')));
  }
  for(const mode of ['live-stage-held','ar-reference-held','live-stage-burst','ar-reference-burst']){
   await page.select('#mode',mode);
   if(singleImpact){
    for(const n of [0,11]){
     await page.$eval('#frame',(x,n)=>{x.value=n;x.dispatchEvent(new Event('input',{bubbles:true}));},n);await ready(mode,n);
    }
   }
   await page.click('#release');await ready(mode,12);
   assert((await page.$eval('#pair',im=>im.src)).endsWith(`/motion/${mode}/12.png`));
   await page.click('#next');await ready(mode,13);await page.click('#previous');await ready(mode,12);
   await page.$eval('#frame',x=>{x.value=59;x.dispatchEvent(new Event('input',{bubbles:true}));});await ready(mode,59);
  }
  await page.click('#release');await ready('ar-reference-burst',12);await page.click('#play');
  await page.waitForFunction(()=>Number(document.getElementById('frame').value)>13);await page.click('#play');
  assert.equal(await page.$eval('#play',x=>x.textContent),'Phát');
  await ready('ar-reference-burst',await page.$eval('#frame',x=>Number(x.value)));
  assert.deepEqual(errors,[]);
  if(process.argv[3])await page.screenshot({path:process.argv[3],fullPage:true});
  await page.setViewport({width:390,height:844});await page.goto(base,{waitUntil:'networkidle0'});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  console.log('PASS real headless Chromium: 480 decoded native BGRA halves, four mode pedal/step/seek controls, play/pause, no page errors, mobile overflow check. Not realtime FPS/camera/GPU acceptance.');
  if(singleImpact)console.log('PASS Shatter single-impact semantics: four soft/firm labels, pedal button and frame 0/11/12/13/59 statuses; no held/release claims.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
