// Real Chromium QA for native four-track burst/held review pages. Not a DOM mock.
// Dependencies: puppeteer-core and either CHROME_BIN or @sparticuz/chromium.
// Usage: NODE_PATH=/path/to/deps/node_modules node tools/check_burst_particle_review_browser.cjs URL [screenshot.png]
const puppeteer=require('puppeteer-core');
const assert=require('node:assert/strict');
(async()=>{
 let executablePath=process.env.CHROME_BIN,args=['--no-sandbox'];
 if(!executablePath){const mod=require('@sparticuz/chromium'),chromium=mod.default||mod;executablePath=await chromium.executablePath();args=chromium.args;}
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
  const ready=async(mode,n)=>page.waitForFunction((mode,n)=>!document.getElementById('pair').hidden&&document.getElementById('status').textContent.includes(`${mode} · Frame ${n}/59`),{},mode,n);
  for(const mode of ['live-stage-held','ar-reference-held','live-stage-burst','ar-reference-burst']){
   await page.select('#mode',mode);await page.click('#release');await ready(mode,12);
   assert((await page.$eval('#pair',im=>im.src)).endsWith(`/motion/${mode}/12.png`));
   await page.click('#next');await ready(mode,13);await page.click('#previous');await ready(mode,12);
   await page.$eval('#frame',x=>{x.value=59;x.dispatchEvent(new Event('input',{bubbles:true}));});await ready(mode,59);
  }
  await page.click('#release');await ready('ar-reference-burst',12);await page.click('#play');
  await page.waitForFunction(()=>Number(document.getElementById('frame').value)>13);await page.click('#play');
  assert.equal(await page.$eval('#play',x=>x.textContent),'Phát');
  assert.deepEqual(errors,[]);
  if(process.argv[3])await page.screenshot({path:process.argv[3],fullPage:true});
  await page.setViewport({width:390,height:844});await page.goto(base,{waitUntil:'networkidle0'});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  console.log('PASS real headless Chromium: 480 decoded native BGRA halves, four mode release/step/seek controls, play/pause, no page errors, mobile overflow check. Not realtime FPS/camera/GPU acceptance.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
