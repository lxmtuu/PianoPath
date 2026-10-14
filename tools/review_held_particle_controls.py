#!/usr/bin/env python3
"""Validate real WARP capture hashes and assemble a standalone, native-crop quartet viewer.
Pillow is needed only to verify/reconstruct PNG pixels, never to synthesize review evidence.
"""
import argparse
import base64
import hashlib
import html
import itertools
import json
from pathlib import Path

from make_held_particle_clips import read_png

FAMILIES = ('wisps', 'flames')
PALETTES = ('prism', 'amber', 'indigo')
PATTERNS = {'white': [60], 'black': [61], 'chord': [60, 61, 64, 67]}
VIEWS = ('live-stage', 'ar-reference', 'ar-oblique', 'ar-near-overhead')
VARIANTS = (('neither', False, False), ('light-only', False, True),
            ('particles-only', True, False), ('combined', True, True))
COMPARISONS = (('particles-unlit', 'particles-only', 'neither'),
               ('particles-lit', 'combined', 'light-only'),
               ('light-without-particles', 'light-only', 'neither'),
               ('light-with-particles', 'combined', 'particles-only'))


def validate_plan(manifest, expected_revision=None):
    if (manifest['schema'], manifest['captureCount'], manifest['sceneCount'], manifest['width'], manifest['height'],
        manifest['seed'], manifest['fps'], manifest['frames'], manifest['time'], manifest['pedal'], manifest['heldArt']) != (
            1, 288, 72, 1280, 720, 20261014, 60, 36, .6, False, True):
        raise ValueError('Unexpected control capture plan')
    if expected_revision and manifest['sourceRevision'] != expected_revision:
        raise ValueError('Capture belongs to a different source revision')
    expected = {'-'.join(p) for p in itertools.product(FAMILIES, PALETTES, PATTERNS, VIEWS)}
    scenes = manifest['scenes']
    if len(scenes) != 72 or {s['Id'] for s in scenes} != expected:
        raise ValueError('Missing/duplicate control scenes')
    if set(manifest['sourceHashes']) != {
        'Gpu/StageShaders.hlsl','Gpu/GpuStageRenderer.cs','Gpu/GpuStageSimulation.cs','Gpu/GpuStageSimulation.Effects.cs',
        'Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs','Gpu/GpuLook.cs','Gpu/GpuStageFeed.cs','Gpu/HeldParticleAppearance.cs',
        'Camera/ArCameraPose.cs','Camera/ArParticleBillboard.cs','Stage/PianoStage.cs','Stage/PrismFlowPalette.cs',
        'Diagnostics/HeldParticleControlPlan.cs','Diagnostics/HeldParticleControlReview.cs',
        'Diagnostics/AuxParticleFixtures.cs','Diagnostics/KeyParticleFixtures.cs','docs/samples/ar-keyboard-topdown.png'}:
        raise ValueError('Incomplete source provenance')
    off_hashes = {}
    for scene in scenes:
        if scene['Id'] != '-'.join((scene['Family'], scene['palette'], scene['pattern'], scene['view'])):
            raise ValueError('Scene label does not match its factors')
        if scene['pitches'] != PATTERNS[scene['pattern']]:
            raise ValueError('Incorrect physical-held chord/key input')
        shots = scene['captures']
        if [(s['Variant'], s['Particles'], s['KeyLight']) for s in shots] != list(VARIANTS):
            raise ValueError('Incomplete or reordered lighting × particle quartet')
        if [(c['id'], c['after'], c['before']) for c in scene['comparisons']] != list(COMPARISONS):
            raise ValueError('Unpaired comparison')
        crop = scene['crop']
        if (crop['Width'], crop['Height']) != (384, 360) or not (0 <= crop['X'] <= 896 and 0 <= crop['Y'] <= 360):
            raise ValueError('Invalid native crop')
        if scene['sheetFile'] != f"sheets/{scene['Id']}.png":
            raise ValueError('Unexpected sheet path')
        if (scene['cameraSha256'] is None) != (scene['view'] == 'live-stage'):
            raise ValueError('Missing or misplaced camera provenance')
        for shot in shots:
            if shot['File'] != f"frames/{scene['Id']}/{shot['Variant']}.png":
                raise ValueError('Unexpected full-frame path')
            if not shot['Particles']:
                key = (scene['palette'], scene['pattern'], scene['view'], shot['Variant'])
                if key in off_hashes and off_hashes[key] != shot['PixelSha256']:
                    raise ValueError('Particle-off controls differ between families')
                off_hashes[key] = shot['PixelSha256']
    return scenes


def image_pixels(path, size, expected_hash):
    from PIL import Image
    data = path.read_bytes()
    read_png(data)  # CRC and format guard, in addition to the decoder.
    with Image.open(path) as opened:
        image = opened.convert('RGBA')
    if image.size != size or hashlib.sha256(image.tobytes('raw', 'BGRA')).hexdigest().upper() != expected_hash:
        raise ValueError(f'Pixel dimensions/hash differ: {path}')
    return image


def verify(directory, sheets_only=False, source_root=None, expected_revision=None):
    manifest = json.loads((directory/'manifest.json').read_text(encoding='utf-8-sig'))
    scenes = validate_plan(manifest, expected_revision)
    if source_root:
        for relative, expected in manifest['sourceHashes'].items():
            source = Path(relative)
            if source.is_absolute() or '..' in source.parts:
                raise ValueError('Unsafe source path')
            data = (source_root/source).read_bytes()
            if source.suffix != '.png':
                data = data.decode('utf-8-sig').replace('\r\n','\n').encode('utf-8')
            if hashlib.sha256(data).hexdigest().upper() != expected:
                raise ValueError(f'Source hash differs: {relative}')
    for scene in scenes:
        sheet = image_pixels(directory/scene['sheetFile'], (768,720), scene['sheetPixelSha256'])
        if not sheets_only:
            crop = scene['crop']; x, y = crop['X'], crop['Y']
            for index, shot in enumerate(scene['captures']):
                frame = image_pixels(directory/shot['File'], (1280,720), shot['PixelSha256'])
                native = frame.crop((x,y,x+384,y+360))
                pane = sheet.crop(((index%2)*384,(index//2)*360,(index%2+1)*384,(index//2+1)*360))
                if native.tobytes() != pane.tobytes():
                    raise ValueError(f'Quartet does not contain the native crop: {scene["Id"]}')
    return manifest


def build(directory, output, **options):
    manifest = verify(directory, **options)
    scenes = manifest['scenes']
    data = {scene['Id']: {
        'image': 'data:image/png;base64,'+base64.b64encode((directory/scene['sheetFile']).read_bytes()).decode('ascii'),
        'captures': scene['captures'], 'comparisons': scene['comparisons'], 'crop': scene['crop']
    } for scene in scenes}
    page = TEMPLATE.replace('__SOURCE__', html.escape(manifest['sourceRevision'])).replace(
        '__DATA__', json.dumps(data, separators=(',',':')).replace('<', '\\u003c'))
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(page,encoding='utf-8')
    return manifest


TEMPLATE = '''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Held particles — lighting controls</title><style>
:root{color-scheme:dark;font:16px system-ui;background:#10141b;color:#e1e9f3}body{max-width:1040px;margin:24px auto;padding:0 16px}h1{font-size:26px}p{line-height:1.6;color:#bbc7d7}select{font:inherit;padding:8px;margin:4px;background:#1b2637;color:inherit;border:1px solid #607189;border-radius:6px}label{display:inline-block}img{display:block;max-width:100%;height:auto;border:1px solid #607189}table{font-size:14px;border-collapse:collapse;margin:16px 0}th,td{text-align:left;border-bottom:1px solid #354055;padding:7px}.error{color:#ff9c9c}.legend{max-width:768px;display:grid;grid-template-columns:1fr 1fr;gap:8px;background:#1b2637;padding:12px}small{color:#aebdd0}#details{overflow-x:auto}
</style><h1>Wisps / Flames — đối chứng ánh sáng phím × hạt</h1>
<p>Shader v2 giữ nguyên. 72 cảnh, mỗi cảnh 4 ảnh cùng seed / input / thời điểm giữ 0,6 giây. Synthetic WARP; không phải camera thật, đánh giá chuyển động hay nghiệm thu art. Source: <code>__SOURCE__</code>.</p>
<label>Hiệu ứng <select id="family"><option>wisps</option><option>flames</option></select></label>
<label>Màu <select id="palette"><option>prism</option><option>amber</option><option>indigo</option></select></label>
<label>Phím <select id="pattern"><option>white</option><option>black</option><option>chord</option></select></label>
<label>Góc <select id="view"><option>live-stage</option><option>ar-reference</option><option>ar-oblique</option><option>ar-near-overhead</option></select></label>
<p id="status" aria-live="polite"></p><div class="legend"><span>↖ Tắt cả hai (neither)</span><span>↗ Chỉ ánh sáng phím (light-only)</span><span>↙ Chỉ hạt (particles-only)</span><span>↘ Cả hai (combined)</span></div>
<img id="sheet" alt="Bốn crop nguyên pixel của cùng một cảnh" width="768" height="720" hidden>
<div id="details"></div><p>Stage tắt tint/emission/spill của phím ở bước đóng gói render; giữ ánh sáng nền của phím, độ nhấn và simulation. AR dùng KeyLight=0, tắt cả tint lẫn projected spill. Màu amber/indigo là màu Note cố định, không chuẩn hóa độ sáng.</p>
<p>Hai góc AR mới dùng phép chiếu của mặt phẳng vật lý và warp cùng ảnh synthetic vào đúng calibration. Không dựng lại ánh sáng 3D của ảnh nền. Crop 384×360, file quartet không resize; trình duyệt có thể thu vừa màn hình. Full scene và manifest nằm trong artifact CI.</p>
<p>Số pixel chênh &gt;4 chỉ là diện tích sai khác RGB 8-bit, không phải độ sáng/chất lượng; không được cộng các delta để suy ra đóng góp tuyến tính. Hợp âm: C4, C♯4, E4, G4, giữ đồng thời, velocity 0,9; pedal tắt.</p>
<script>
const DATA=__DATA__, ids=['family','palette','pattern','view'];let generation=0;
const sheet=document.getElementById('sheet'),status=document.getElementById('status'),details=document.getElementById('details');
async function show(){const gen=++generation,key=ids.map(id=>document.getElementById(id).value).join('-'),d=DATA[key];sheet.hidden=true;details.replaceChildren();status.className='';status.textContent='Đang giải mã '+key+'…';try{if(!d)throw Error('Thiếu cảnh');const image=new Image();image.src=d.image;await image.decode();if(gen!==generation)return;sheet.src=image.src;await sheet.decode();if(gen!==generation)return;sheet.hidden=false;status.textContent=key+' · crop ('+d.crop.X+', '+d.crop.Y+') · 0,600 s';const table=document.createElement('table');table.innerHTML='<tr><th>Ảnh</th><th>Stage keys / sprites</th><th>AR keys / spill / particles</th></tr>';for(const c of d.captures){const row=table.insertRow();for(const v of [c.Variant,c.StageKeys+' / '+c.StageSprites,c.ArKeys+' / '+c.ArLights+' / '+c.ArParticles])row.insertCell().textContent=v;}details.append(table);const metrics=document.createElement('table');metrics.innerHTML='<tr><th>So sánh</th><th>Pixel Δ&gt;4 full frame</th><th>Pixel Δ&gt;4 crop</th><th>Mean |ΔRGB| crop /255</th></tr>';for(const c of d.comparisons){const row=metrics.insertRow();for(const v of [c.id,c.fullFrame.OverFourPixels,c.crop.OverFourPixels,c.crop.MeanAbsoluteRgbDelta.toFixed(4)])row.insertCell().textContent=v;}details.append(metrics);}catch(e){if(gen!==generation)return;sheet.hidden=true;status.className='error';status.textContent='Không mở được ảnh: '+e.message;}}
ids.forEach(id=>document.getElementById(id).addEventListener('change',show));show();
</script></html>'''


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path)
    parser.add_argument('--output',type=Path)
    parser.add_argument('--sheets-only',action='store_true',help='For published crop sheets; does not verify full-frame hashes/crop reconstruction')
    parser.add_argument('--source-root',type=Path)
    parser.add_argument('--expected-revision')
    args=parser.parse_args()
    manifest=build(args.directory,args.output or args.directory/'review.html',sheets_only=args.sheets_only,
                   source_root=args.source_root,expected_revision=args.expected_revision)
    print(f"PASS {manifest['captureCount']} capture records, {manifest['sceneCount']} sheet hashes; " +
          ('full-frame pixel hashes/native crops NOT checked (sheets-only)' if args.sheets_only else '288 full-frame pixel hashes + all native crops checked'))


if __name__=='__main__':
    main()
