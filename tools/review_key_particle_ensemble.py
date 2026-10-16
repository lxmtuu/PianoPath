#!/usr/bin/env python3
"""Validate the first native mixed-scene slice; publish a bounded, exact pixel subset."""
import argparse,gzip,hashlib,itertools,json,re,shutil
from pathlib import Path
RECIPES=[dict(zip(('Id','Burst','Held','Morph','Release'),x)) for x in [
 ('embers-wisps','Embers','Wisps','Shatter','Float Up'),('splash-flames','Splash','Flames','Melt','Dissolve'),
 ('fireworks-wisps','Fireworks','Wisps','Bounce','Smoke'),('confetti-flames','Confetti','Flames','Shatter','Float Up'),('dust-wisps','Dust','Wisps','Melt','Dissolve')]]
NOTES=[dict(Pitch=60,On=0,Off=12,Strength=.85)]+[dict(Pitch=p,On=18,Off=36,Strength=.9) for p in (48,55,60,61,64,67,72,73)]
CHECKPOINTS=dict(zip(('single-attack','single-held','chord-attack','chord-held','release-pedal','pedal-up','rest'),(2,10,20,33,38,56,149)))
VARIANTS=['off','classic','mixed','front-control'];VIEWS=['live-stage','ar-reference'];PALETTES=['prism','indigo'];LIGHTS=[0,100]
POLICY='classic=held v2/release legacy; mixed=approved held v3/release material at existing depth; front-control=only release Stage depth .01; burst/morph stay shipping classic in every variant'
SOURCES={'Gpu/StageShaders.hlsl','Gpu/GpuLook.cs','Gpu/GpuStageRenderer.cs','Gpu/GpuStageSimulation.cs','Gpu/GpuStageSimulation.Effects.cs',
 'Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs','Gpu/GpuRenderLoop.cs','Gpu/GpuStageFeed.cs','Gpu/HeldParticleAppearance.cs','Gpu/ReleaseParticleAppearance.cs',
 'Stage/PianoVisualSettings.cs','Stage/PianoStage.cs','Stage/PianoStage.Gpu.cs','Camera/ArParticleBillboard.cs','Camera/Ar3DNoteProjection.cs','Camera/ArCameraPose.cs',
 'Diagnostics/KeyParticleFixtures.cs','Diagnostics/HeldParticleControlPlan.cs','Diagnostics/KeyParticleEnsemblePlan.cs','Diagnostics/KeyParticleEnsembleReview.cs','docs/samples/ar-keyboard-topdown.png'}
BUDGET=750000

def read(p):return json.loads(gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_text(encoding='utf-8-sig'))
def digest(im):return hashlib.sha256(im.convert('RGBA').tobytes('raw','BGRA')).hexdigest().upper()
def key(c):return tuple(c[k] for k in ('Recipe','Palette','Light','View','Phase','Variant'))
def identifier(k):return f'{k[0]}-{k[1]}-l{k[2]}-{k[3]}-{k[4]}-{k[5]}'
def sha(x):return isinstance(x,str) and re.fullmatch('[A-F0-9]{64}',x)
def validate(m,revision=None):
    if (m['schema'],m['family'],m['reviewOnly'],m['width'],m['height'],m['fps'],m['seed'],m['legacyParticleAxesForReview'])!=(1,'key-particle-ensemble',True,640,360,30,20261016,False):raise ValueError('Wrong ensemble contract')
    if revision and m['sourceRevision']!=revision:raise ValueError('Wrong native revision')
    if m['recipes']!=RECIPES or m['notes']!=NOTES or m['checkpoints']!=[dict(Id=k,Frame=v) for k,v in CHECKPOINTS.items()]:raise ValueError('Wrong mixed-scene input')
    if (m['variants'],m['lights'],m['palettes'],m['pedalOnFrame'],m['pedalOffFrame'],m['materialPolicy'])!=(VARIANTS,LIGHTS,PALETTES,24,54,POLICY):raise ValueError('Wrong lighting/depth/pedal plan')
    if set(m['sourceHashes'])!=SOURCES or not all(sha(h) for h in m['sourceHashes'].values()):raise ValueError('Incomplete source guards')
    expected=set(itertools.product((r['Id'] for r in RECIPES),PALETTES,LIGHTS,VIEWS,CHECKPOINTS,VARIANTS))
    if len(m['captures'])!=1120 or {key(c) for c in m['captures']}!=expected:raise ValueError('Incomplete ensemble matrix')
    rows={key(c):c for c in m['captures']}
    for k,c in rows.items():
        if c['Id']!=identifier(k) or c['File']!=c['Id']+'.png' or c['Frame']!=CHECKPOINTS[c['Phase']]:raise ValueError('Bad capture provenance')
        for f in ('PixelSha256','RawHash','GeometryHash','PacketHash','KeyHash'):
            if not sha(c[f]):raise ValueError('Bad native digest')
        for f,maximum in [('RawParticles',8192),('Packets',100000),('StageNotes',100000),('ArLiveBars',100000),('Signal',640*360*765),('VisiblePixels',640*360),('ClippedDeltaPixels',640*360)]:
            if type(c[f]) is not int or not 0<=c[f]<=maximum:raise ValueError('Bad native metric')
        if c['Phase'] in ('single-held','chord-held') and not c['ArLiveBars' if c['View']=='ar-reference' else 'StageNotes']:raise ValueError('Held bars absent')
        if c['View']=='ar-reference' and c['StageNotes']:raise ValueError('Stage notes leaked to AR')
        if c['ClippedDeltaPixels']>c['VisiblePixels']:raise ValueError('Bad clipped-delta metric')
        for field in ('Population','ArSelected'):
            groups=c[field]
            if any(set(g)!={'Pitch','Origin','Route','Count'} or any(type(v) is not int for v in g.values()) or not(0<=g['Pitch']<128 and 0<=g['Origin']<=2 and 0<=g['Route']<=5 and g['Count']>0) for g in groups):raise ValueError('Bad source population')
            ids=[(g['Pitch'],g['Origin'],g['Route']) for g in groups]
            if ids!=sorted(set(ids)):raise ValueError('Duplicate/out-of-order population')
            total=sum(g['Count'] for g in groups)
            if (field=='Population' and total!=c['RawParticles']) or (field=='ArSelected' and (total>c['RawParticles'] or (c['View']=='live-stage' and total))):raise ValueError('Bad population accounting')
        if c['Variant']=='off' and any(c[f] for f in ('RawParticles','Signal','VisiblePixels','ClippedDeltaPixels')):raise ValueError('Effect-off emitted particles/delta')
        if c['Phase']=='rest' and (c['RawParticles'] or c['Signal']):raise ValueError('Rest has particle state/delta')
        if c['Variant']=='mixed' and c['Phase'] not in ('rest','pedal-up') and (not c['RawParticles'] or not c['Signal']):raise ValueError('Active scene is empty')
        if c['Variant']=='mixed' and c['Phase']=='release-pedal' and not any(p['Origin']==1 for p in c['Population']):raise ValueError('No real physical release')
        if c['Variant'] in ('mixed','front-control'):
            a=rows[k[:-1]+(('classic' if c['Variant']=='mixed' else 'mixed'),)]
            if any(a[f]!=c[f] for f in ('RawHash','GeometryHash','KeyHash','RawParticles','Packets','Population','ArSelected')):raise ValueError('Material/depth changed state/geometry/selection')
            if c['Variant']=='front-control' and c['View']=='ar-reference' and a['PixelSha256']!=c['PixelSha256']:raise ValueError('Stage depth changed AR')
        stage=rows[(k[0],k[1],k[2],'live-stage',k[4],k[5])];dark=rows[(k[0],k[1],0,k[3],k[4],k[5])]
        if c['RawHash']!=stage['RawHash'] or any(c[f]!=dark[f] for f in ('RawHash','GeometryHash','PacketHash','KeyHash','Population','ArSelected')):raise ValueError('Light/projection changed simulation or packets')
        if c['View']=='ar-reference' and c['PixelSha256']!=dark['PixelSha256']:raise ValueError('Virtual light changed AR')
    return rows

def sheets(rows):
    # FIRST slice: three recipes/all release types, prism/100%, two checkpoints, reference views only.
    # Other native originals stay in CI artifact, not silently claimed to be displayed here.
    for recipe,view in itertools.product(RECIPES[:3],VIEWS):
        box=[210,180,430,340] if view=='live-stage' else [210,45,430,205]
        panes=[]
        for y,phase in enumerate(('chord-held','release-pedal')):
            for x,variant in enumerate(('mixed','front-control')):
                c=rows[recipe['Id'],'prism',100,view,phase,variant]
                panes.append(dict(id=c['Id'],box=box,x=x*220,y=y*160,width=220,height=160))
        yield dict(file=recipe['Id']+'-'+view+'.png',width=440,height=320,panes=panes)

def metrics(im,off):
    from PIL import ImageChops,ImageStat
    rgb=im.convert('RGB');diff=ImageChops.difference(rgb,off.convert('RGB'));r,g,b=diff.split();maximum=ImageChops.lighter(ImageChops.lighter(r,g),b)
    visible=maximum.point(lambda x:255 if x>4 else 0)
    r,g,b=rgb.split();bright=ImageChops.lighter(ImageChops.lighter(r,g),b).point(lambda x:255 if x>=250 else 0)
    return int(sum(ImageStat.Stat(diff).sum)),visible.histogram()[255],ImageChops.multiply(visible,bright).histogram()[255]

def verify(directory,source_root=None,revision=None,published_only=False,publish=None):
    from PIL import Image
    p=directory/'manifest.json';p=p if p.exists() else directory/'manifest.json.gz';m=read(p);rows=validate(m,revision)
    if source_root:
        for name,h in m['sourceHashes'].items():
            raw=(source_root/name).read_bytes()
            if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(raw).hexdigest().upper()!=h:raise ValueError('Source drift: '+name)
    if not published_only:
        for k,c in rows.items():
            with Image.open(directory/c['File']) as im,Image.open(directory/rows[k[:-1]+('off',)]['File']) as off:
                if im.size!=(640,360) or digest(im)!=c['PixelSha256']:raise ValueError('Native image changed: '+c['Id'])
                if metrics(im,off)!=(c['Signal'],c['VisiblePixels'],c['ClippedDeltaPixels']):raise ValueError('False image metrics: '+c['Id'])
        m['sheets']=list(sheets(rows));by_id={c['Id']:c for c in rows.values()}
        for s in m['sheets']:
            im=Image.new('RGBA',(s['width'],s['height']))
            for pane in s['panes']:
                with Image.open(directory/by_id[pane['id']]['File']) as native:tile=native.convert('RGBA').crop(pane['box'])
                pane['hash']=digest(tile);im.paste(tile,(pane['x'],pane['y']))
            s['hash']=digest(im);im.save(directory/s['file'],compress_level=9,optimize=True)
        (directory/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(m,separators=(',',':')).encode(),mtime=0))
    expected=list(sheets(rows))
    if len(m.get('sheets',[]))!=len(expected):raise ValueError('Missing ensemble sheets')
    for s,e in zip(m['sheets'],expected):
        if any(s[k]!=e[k] for k in ('file','width','height')) or len(s['panes'])!=len(e['panes']):raise ValueError('Bad sheet provenance')
        with Image.open(directory/s['file']) as im:
            if im.size!=(s['width'],s['height']) or digest(im)!=s['hash']:raise ValueError('Native sheet changed')
            for p,q in zip(s['panes'],e['panes']):
                if any(p[k]!=q[k] for k in q) or digest(im.crop((p['x'],p['y'],p['x']+p['width'],p['y']+p['height'])))!=p['hash']:raise ValueError('Native pane provenance/pixels changed')
    size=(directory/'manifest.json.gz').stat().st_size+sum((directory/s['file']).stat().st_size for s in m['sheets'])
    if size>BUDGET:raise ValueError(f'Bounded ensemble publication {size} exceeds {BUDGET} bytes')
    if publish:
        evidence,previews=publish;evidence.mkdir(parents=True,exist_ok=True);previews.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(directory/'manifest.json.gz',evidence/'manifest.json.gz')
        for s in m['sheets']:shutil.copyfile(directory/s['file'],previews/s['file'])
    return m

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--published-only',action='store_true');p.add_argument('--publish-evidence',type=Path);p.add_argument('--publish-previews',type=Path);a=p.parse_args()
    if bool(a.publish_evidence)!=bool(a.publish_previews):p.error('Both publication paths are required')
    try:
        m=verify(a.directory,a.source_root,a.expected_revision,a.published_only,(a.publish_evidence,a.publish_previews) if a.publish_evidence else None)
        print(f'PASS {len(m["captures"])} native mixed checkpoints; six lossless subset sheets. Not full regression, continuous motion, physical hardware or art acceptance.')
    except Exception as ex:
        print('::error title=Ensemble native validation::'+str(ex).replace('\n',' ')[:1000],flush=True);raise
if __name__=='__main__':main()
