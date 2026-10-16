// UI-only QA for the disconnected Settings proposal. Not WPF/render/runtime acceptance.
const assert = require('node:assert/strict');
const puppeteer = require('puppeteer-core');
(async () => {
  const mod = require('@sparticuz/chromium'), chromium = mod.default || mod;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME_BIN || await chromium.executablePath(), args: chromium.args, headless: true });
  try {
    const page = await browser.newPage(), errors = [], remote = [];
    page.on('pageerror', e => errors.push(String(e)));
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    const base = process.argv[2] || 'http://127.0.0.1:8785';
    page.on('request', r => { if (!r.url().startsWith(base) && !r.url().startsWith('blob:')) remote.push(r.url()); });
    const state = () => page.evaluate(() => window.settingsPrototype.snapshot());
    const goto = async id => { await page.evaluate(id => { location.hash = id; }, id); await page.waitForFunction(id => window.settingsPrototype.snapshot().route === id, {}, id); };
    await page.setViewport({ width: 1440, height: 1060 });
    await page.goto(base, { waitUntil: 'networkidle0' });
    await page.waitForFunction(() => window.settingsPrototype);
    const inventory = await page.evaluate(() => window.settingsPrototype.inventory());
    assert.equal(Object.keys(inventory.oldRoutes).length, 13);
    assert.equal(inventory.controls.length, 254);
    assert.equal(await page.$eval('#nav', n => n.querySelectorAll('a').length), 12);
    if (process.argv[3]) await page.screenshot({ path: process.argv[3], fullPage: true });
    // Draft edits, mode switches, navigation, language and search share one model.
    await goto('hold');
    await page.click('#field-ShowWisps');
    const edited = (await state()).values;
    await page.click('[data-mode=ar]');
    assert.deepEqual((await state()).values, edited);
    await goto('release'); await goto('hold');
    assert.deepEqual((await state()).values, edited);
    await page.type('#search', 'giữ phím');
    assert((await page.$$('[data-property=ShowWisps]')).length);
    await page.click('#language');
    assert.deepEqual((await state()).values, edited);
    await page.$eval('#search', n => { n.value = 'WispHeight'; n.dispatchEvent(new Event('input')); });
    assert(await page.$('#field-WispHeight'));
    await page.$eval('#search', n => { n.value = 'no-such-setting-584235'; n.dispatchEvent(new Event('input')); });
    assert(await page.$('.empty'));
    // Scope labels disable context-inapplicable widgets without deleting values.
    await goto('stage');
    assert(await page.$eval('#field-ShowKeys', n => n.disabled));
    await page.click('[data-mode=stage]');
    assert(await page.$eval('#field-ShowKeys', n => !n.disabled));
    assert.deepEqual((await state()).values, edited);
    // AR draft calibration-related values survive applying every community preset.
    await page.click('[data-mode=ar]'); await goto('lighting');
    await page.$eval('#field-ArKeyLight', n => { n.value = 71; n.dispatchEvent(new Event('input')); });
    const machineBefore = (await state()).values;
    await goto('overview');
    const names = await page.$$eval('#preset option', ns => ns.map(n => n.value).filter(Boolean));
    assert.equal(names.length, 6);
    for (const name of names) {
      await page.select('#preset', name);
      const after = (await state()).values;
      for (const prop of inventory.machineLocalProperties) assert.deepEqual(after[prop], machineBefore[prop], `${name}: ${prop} overwritten`);
    }
    await page.select('#preset', 'Prism Flow - Wisps');
    assert.equal((await state()).values.UpgradedParticleEffects, true);
    // Section reset is explicit, cancelable, scoped and uses the selected preset baseline.
    await goto('hold');
    const beforeReset = await state();
    await page.click('#field-ShowWisps');
    await page.click('#reset'); await page.click('#cancel-reset');
    assert.notEqual((await state()).values.ShowWisps, beforeReset.values.ShowWisps);
    await page.click('#reset'); await page.click('#confirm-reset');
    assert.deepEqual((await state()).values, beforeReset.values);
    // A layout draft has its own explicit envelope; it cannot masquerade as an app preset.
    const exported = await page.evaluate(async () => {
      const oldURL = URL.createObjectURL, oldClick = HTMLAnchorElement.prototype.click;
      let blob;
      try {
        URL.createObjectURL = value => { blob = value; return oldURL.call(URL, value); };
        HTMLAnchorElement.prototype.click = function () {};
        document.getElementById('export').click();
        return JSON.parse(await blob.text());
      } finally { URL.createObjectURL = oldURL; HTMLAnchorElement.prototype.click = oldClick; }
    });
    assert.equal(exported.kind, 'settings-layout-prototype');
    assert.equal(exported.notAnAppPreset, true);
    assert.deepEqual(exported.values, (await state()).values);
    // Every new and legacy route resolves; dynamic actions remain reference-only.
    for (const r of inventory.routes) { await goto(r.id); assert.equal(await page.$eval('#title', n => Boolean(n.textContent)), true); }
    for (const [old, targets] of Object.entries(inventory.oldRoutes)) {
      await page.evaluate(old => { location.hash = 'old/' + old; }, old);
      await page.waitForFunction(target => window.settingsPrototype.snapshot().route === target, {}, targets[0]);
    }
    await goto('camera');
    assert(await page.$$eval('.action-ref', ns => ns.length >= 10 && ns.every(n => n.disabled)));
    assert((await page.$eval('#content', n => n.textContent)).includes('CalibrateAr_Click') === false); // Friendly captions, handler names stay in inventory.
    await page.click('[data-mode=stage]');
    assert(await page.$eval('#field-ArEnabled', n => n.disabled));
    assert.deepEqual(remote, []);
    // Responsive audit at small desktop and narrow mobile widths, both languages, all routes.
    for (const width of [800, 390, 320]) {
      await page.setViewport({ width, height: 844 });
      for (let language = 0; language < 2; language++) {
        await page.click('#language');
        for (const r of [...inventory.routes, { id: 'inventory' }]) {
          await goto(r.id);
          assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Horizontal overflow: ${width}, ${r.id}`);
          await page.$$eval('details', ns => ns.forEach(n => n.open = true));
          assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Expanded details overflow: ${width}, ${r.id}`);
          const ids = await page.$$eval('[id]', ns => ns.map(n => n.id));
          assert.equal(ids.length, new Set(ids).size, 'Duplicate control IDs');
        }
      }
    }
    // In-memory state is intentionally disposable, not autosaved.
    await page.reload({ waitUntil: 'networkidle0' });
    assert.deepEqual((await state()).values, inventory.defaults);
    assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0);
    await goto('hold');
    await page.click('[data-mode=ar]');
    if (process.argv[4]) await page.screenshot({ path: process.argv[4], fullPage: true });
    assert.deepEqual(errors, []);
    console.log('PASS Settings prototype UI only: 12 new / 13 legacy routes; VI/EN search; shared draft across mode/language/navigation; six presets preserve machine settings; scoped reset/cancel; disabled business actions; 800/390/320px × two languages × 13 views; no remote requests or storage writes. No WPF/native/render/performance checks.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
