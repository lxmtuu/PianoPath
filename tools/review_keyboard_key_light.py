#!/usr/bin/env python3
"""Validate actual Windows key-light captures and publish exact, bounded native crops."""
import argparse,gzip,hashlib,itertools,json,re
from pathlib import Path
LEVELS=(0,1,10,25,50,70,92,100,200)
CURVE='t*(4.05-5.6*t) below .25; .35+1.25*t at/above .25; main lamp only'
SOURCES={'Gpu/StageShaders.hlsl','Gpu/GpuLook.cs','Gpu/GpuStageRenderer.cs','Gpu/GpuStageSimulation.cs',
 'Gpu/GpuStageSimulation.Effects.cs','Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs','Gpu/GpuRenderLoop.cs','Gpu/GpuStageFeed.cs',
 'Stage/PianoVisualSettings.cs','Stage/PianoStage.cs','Stage/PianoStage.Gpu.cs','Stage/Shading/PianoShaderScene.cs',
 'Stage/Shading/PianoKeyboardRenderer.cs','Ui/MainWindow.Settings.cs','Diagnostics/KeyboardKeyLightReview.cs',
 'Diagnostics/KeyboardKeyLightPlan.cs','Diagnostics/KeyParticleFixtures.cs','Diagnostics/HeldParticleControlPlan.cs','docs/samples/ar-keyboard-topdown.png'}

def digest(im):return hashlib.sha256(im.convert('RGBA').tobytes('raw','BGRA')).hexdigest().upper()
def read(p):return json.loads((gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_bytes()).decode('utf-8-sig'))
def key(c):return (c['Engine'],c['Scene'],c['State'],c['Percent'],c['Variant'])
def plan():
    return {(e,s,t,p,v) for e in ('gpu','software') for s in (('isolated','mixed','ar') if e=='gpu' else ('isolated','mixed'))
        for t in ('idle','held') for p in LEVELS for v in (('fixed','legacy','forced-off') if p==0 else ('fixed','legacy'))
        if e=='gpu' or v!='legacy'}
def validate(m,revision=None):
    if (m['schema'],m['family'],m['levels'],m['gpuWidth'],m['gpuHeight'],m['softwareWidth'],m['softwareHeight'],m['curve'])!=(1,'keyboard-key-light',list(LEVELS),640,360,640,128,CURVE):raise ValueError('Wrong key-light recipe')
    if revision and m['sourceRevision']!=revision:raise ValueError('Wrong native source revision')
    if set(m['sourceHashes'])!=SOURCES:raise ValueError('Incomplete source guards')
    if len(m['captures'])!=154 or {key(c) for c in m['captures']}!=plan():raise ValueError('Incomplete key-light matrix')
    records={key(c):c for c in m['captures']}
    for c in m['captures']:
        e,s,t,p,v=key(c);id_=f'{e}-{s}-{t}-p{p}-{v}'
        if c['Id']!=id_ or c['File']!=id_+'.png' or (c['Width'],c['Height'])!=(640,360 if e=='gpu' else 128):raise ValueError('Bad capture provenance')
        for field in ('PixelSha256','RawHash','PacketHash'):
            if not isinstance(c[field],str) or not re.fullmatch('[A-F0-9]{64}',c[field]):raise ValueError('Bad native digest')
        if type(c['KeyboardMean']) not in (float,int) or not 0<=c['KeyboardMean']<=255:raise ValueError('Bad mean RGB')
    for e,s,t in itertools.product(('gpu','software'),('isolated','mixed','ar'),('idle','held')):
        if e=='software' and s=='ar':continue
        at=lambda p,v:records[e,s,t,p,v]
        if at(0,'fixed')['PixelSha256']!=at(0,'forced-off')['PixelSha256']:raise ValueError('Zero is not forced-off')
        if s=='ar':
            rows=[c for c in m['captures'] if c['Engine']==e and c['Scene']==s and c['State']==t]
            if any(c[f]!=rows[0][f] for c in rows for f in ('PixelSha256','RawHash','PacketHash')):raise ValueError('AR/camera changed')
            continue
        means=[at(p,'fixed')['KeyboardMean'] for p in LEVELS]
        if any(a>=b for a,b in zip(means,means[1:])):raise ValueError('Nonmonotone native ramp')
        if e=='gpu':
            if at(0,'fixed')['KeyboardMean']>=at(0,'legacy')['KeyboardMean']:raise ValueError('Zero did not darken keyboard')
            for p in LEVELS:
                a,b=at(p,'fixed'),at(p,'legacy')
                if any(a[f]!=b[f] for f in ('RawHash','PacketHash')):raise ValueError('Non-light packet/state drift')
                if p>=25 and a['PixelSha256']!=b['PixelSha256']:raise ValueError('Usual preset illumination changed')
    return records

def sheets(records):
    """Each pane is an unscaled native crop. Labels live in HTML, not painted onto evidence."""
    for engine,scene,state in itertools.product(('gpu','software'),('isolated','mixed'),('idle','held')):
        box=[210,240,430,360] if engine=='gpu' else [210,0,430,128]
        h=box[3]-box[1];name=f'{engine}-{scene}-{state}.png';panes=[]
        for x,variant in enumerate(('legacy' if engine=='gpu' else 'forced-off','fixed')):
            panes.append(dict(id=records[engine,scene,state,0,variant]['Id'],box=box,x=x*220,y=0,width=220,height=h))
        for x,p in enumerate((0,25,50,100,200)):
            panes.append(dict(id=records[engine,scene,state,p,'fixed']['Id'],box=box,x=x*220,y=h,width=220,height=h))
        yield dict(file=name,width=1100,height=h*2,panes=panes)
    for state in ('idle','held'):
        yield dict(file=f'gpu-ar-{state}.png',width=480,height=140,panes=[dict(id=records['gpu','ar',state,p,'fixed']['Id'],box=[200,120,440,260],x=i*240,y=0,width=240,height=140) for i,p in enumerate((0,200))])

def verify(directory,source_root=None,revision=None,published_only=False,publish=None):
    from PIL import Image,ImageStat
    path=directory/'manifest.json'
    if not path.exists():path=directory/'manifest.json.gz'
    m=read(path);records=validate(m,revision);by_id={c['Id']:c for c in m['captures']}
    if source_root:
        for name,h in m['sourceHashes'].items():
            raw=(source_root/name).read_bytes()
            if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(raw).hexdigest().upper()!=h:raise ValueError('Native source differs: '+name)
    if not published_only:
        for c in m['captures']:
            with Image.open(directory/c['File']) as im:
                if im.size!=(c['Width'],c['Height']) or digest(im)!=c['PixelSha256']:raise ValueError('Original pixels changed')
                top=c['Height']-120 if c['Engine']=='gpu' else 0
                roi=im.convert('RGB').crop((20,top,c['Width']-20,c['Height']))
                measured=sum(ImageStat.Stat(roi).sum)/(roi.width*roi.height*3)
                if abs(measured-c['KeyboardMean'])>1e-9:raise ValueError('Native mean RGB changed')
        # Pixel-level proof that the main lamp cannot darken the non-keyboard region.
        for engine,scene,state,p,variant in plan():
            if engine!='gpu' or scene=='ar' or variant!='fixed':continue
            a=Image.open(directory/records[engine,scene,state,p,'fixed']['File']).convert('RGBA')
            b=Image.open(directory/records[engine,scene,state,p,'legacy']['File']).convert('RGBA')
            if a.crop((0,0,640,230)).tobytes()!=b.crop((0,0,640,230)).tobytes():raise ValueError('Background region changed')
        m['sheets']=list(sheets(records))
        for s in m['sheets']:
            im=Image.new('RGBA',(s['width'],s['height']),(16,20,27,255))
            for p in s['panes']:
                crop=Image.open(directory/by_id[p['id']]['File']).convert('RGBA').crop(p['box']);p['hash']=digest(crop);im.paste(crop,(p['x'],p['y']))
            s['hash']=digest(im);im.save(directory/s['file'],compress_level=9,optimize=True)
        (directory/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(m,separators=(',',':')).encode(),mtime=0))
    expected=list(sheets(records))
    if len(m.get('sheets',[]))!=len(expected):raise ValueError('Incomplete publication')
    for s,e in zip(m['sheets'],expected):
        if any(s[k]!=e[k] for k in ('file','width','height')) or len(s['panes'])!=len(e['panes']):raise ValueError('Bad sheet provenance')
        with Image.open(directory/s['file']) as im:
            if im.size!=(s['width'],s['height']) or digest(im)!=s['hash']:raise ValueError('Sheet changed')
            for p,q in zip(s['panes'],e['panes']):
                if any(p[k]!=q[k] for k in q):raise ValueError('Native crop provenance changed')
                if digest(im.crop((p['x'],p['y'],p['x']+p['width'],p['y']+p['height'])))!=p['hash']:raise ValueError('Native pane changed')
    if publish:
        import shutil
        evidence,previews=publish;evidence.mkdir(parents=True,exist_ok=True);previews.mkdir(parents=True,exist_ok=True)
        names=[s['file'] for s in m['sheets']];size=(directory/'manifest.json.gz').stat().st_size+sum((directory/n).stat().st_size for n in names)
        if size>700000:raise ValueError('Bounded publication exceeds 700000 bytes')
        shutil.copyfile(directory/'manifest.json.gz',evidence/'manifest.json.gz')
        for name in names:shutil.copyfile(directory/name,previews/name)
        print('Native publication bytes:',size)
    return m

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--published-only',action='store_true');p.add_argument('--publish-evidence',type=Path);p.add_argument('--publish-previews',type=Path);a=p.parse_args()
    if bool(a.publish_evidence)!=bool(a.publish_previews):p.error('Both publication directories required')
    verify(a.directory,a.source_root,a.expected_revision,a.published_only,(a.publish_evidence,a.publish_previews) if a.publish_evidence else None)
    print('PASS 154 native key-light captures: independent true-zero references, monotone means, preserved 25+ illumination, unchanged packets/background/AR; exact bounded crop provenance.')
if __name__=='__main__':main()
