#!/usr/bin/env python3
"""Verify native budget inventory/payloads and publish bounded exact crops and temporal slices."""
import argparse,gzip,hashlib,itertools,json,re,shutil
from collections import deque
from pathlib import Path
from review_key_particle_ensemble import RECIPES,NOTES,CHECKPOINTS,digest,read,sha
from review_release_particle_art import pack_frames
VIEWS=['live-stage','ar-reference','ar-oblique','ar-near-overhead'];MODES=['fast','legacy','fair'];BUDGET=2400000
POLICY='round-robin origins 0/1/2 then pitches, borrow unused shares; retain legacy draw order; exact under-cap identity'
BASELINE='79930e52cb3773f02d9e8e59be2c111236917227'
SOURCES={'Gpu/ArParticleBudget.cs','Gpu/GpuLook.cs','Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs','Gpu/GpuStageRenderer.cs',
 'Gpu/GpuStageSimulation.cs','Gpu/GpuStageSimulation.Effects.cs','Gpu/GpuStageFeed.cs','Gpu/GpuRenderLoop.cs','Gpu/StageShaders.hlsl',
 'Gpu/HeldParticleAppearance.cs','Gpu/ReleaseParticleAppearance.cs','Stage/CommunityPresets.cs','Stage/PianoVisualSettings.cs','Stage/PianoStage.cs',
 'Stage/PianoStage.Gpu.cs','Camera/ArParticleBillboard.cs','Camera/ArCameraPose.cs','Camera/Ar3DNoteProjection.cs',
 'Diagnostics/KeyParticleFixtures.cs','Diagnostics/HeldParticleControlPlan.cs','Diagnostics/KeyParticleEnsemblePlan.cs',
 'Diagnostics/KeyParticleEnsembleReview.cs','Diagnostics/ArParticleBudgetReview.cs','docs/samples/ar-keyboard-topdown.png'}

def select(keys,cap=192):
    if type(cap) is not int or cap<0 or any(type(k) is not int or not 0<=k<384 for k in keys):raise ValueError('Invalid budget candidates')
    if len(keys)<=cap:return list(range(len(keys)))
    roles=[]
    for role in range(3):
        groups={}
        for i,k in enumerate(keys):
            if k//128==role:groups.setdefault(k%128,deque()).append(i)
        roles.append(deque(groups[p] for p in sorted(groups)))
    out=[]
    while len(out)<cap and any(roles):
        for groups in roles:
            if not groups or len(out)==cap:continue
            group=groups.popleft();out.append(group.popleft())
            if group:groups.append(group)
    return sorted(out)

def key(c):return tuple(c[f] for f in ('Recipe','Palette','View','Phase','Frame','Mode'))
def identifier(k):return f'{k[0]}-{k[1]}-{k[2]}-{k[3]}-f{k[4]}-{k[5]}'
def plan():
    return set(itertools.product([r['Id'] for r in RECIPES],['prism','indigo'],VIEWS,CHECKPOINTS.items(),MODES))
def validate(m,revision=None):
    if (m['schema'],m['family'],m['reviewOnly'],m['defaultEnabled'],m['cap'],m['width'],m['height'],m['fps'],m['seed'])!=(1,'ar-particle-budget',True,False,192,640,360,30,20261016):raise ValueError('Wrong budget recipe')
    if revision and m['sourceRevision']!=revision:raise ValueError('Wrong native source')
    if m['baselineRevision']!=BASELINE or m['policy']!=POLICY or m['recipes']!=RECIPES or m['palettes']!=['prism','indigo'] or m['notes']!=NOTES or m['checkpoints']!=[dict(Id=p,Frame=f) for p,f in CHECKPOINTS.items()] or (m['pedalOnFrame'],m['pedalOffFrame'])!=(24,54):raise ValueError('Wrong budget timeline/policy')
    if set(m['sourceHashes'])!=SOURCES or not all(sha(h) for h in m['sourceHashes'].values()):raise ValueError('Missing source guards')
    expected={(r,p,v,phase,f,mode) for r,p,v,(phase,f),mode in plan()}
    expected|={(r['Id'],'prism','ar-reference','motion',f,mode) for r,f,mode in itertools.product(RECIPES[:2],range(29,45),('legacy','fair'))}
    if len(m['captures'])!=904 or {key(c) for c in m['captures']}!=expected:raise ValueError('Incomplete budget matrix')
    rows={key(c):c for c in m['captures']}
    for k,c in rows.items():
        if c['Id']!=identifier(k) or c['File']!=c['Id']+'.png':raise ValueError('Bad capture provenance')
        for f in ('PixelSha256','RawHash','PacketHash','KeyHash','NonParticleHash','EligibleHash','SelectedHash'):
            if not sha(c[f]):raise ValueError('Bad native digest')
        if any(type(c[f]) is not int or not 0<=c[f]<=100000 for f in ('RawParticles','Packets')) or type(c['DrawMilliseconds']) not in (int,float) or not 0<=c['DrawMilliseconds']<1e9:raise ValueError('Bad native count/time')
        keys=c['EligibleKeys'];s=c['Selected'];traced=c['View']!='live-stage' and c['Mode']!='fast'
        if traced:
            if len(keys)>c['RawParticles'] or len(keys)>8192 or any(type(x) is not int or x%128 not in range(21,109) or x//128 not in (0,1,2) for x in keys):raise ValueError('Bad eligibility')
            wanted=select(keys) if c['Mode']=='fair' else list(range(min(192,len(keys))))
            if s!=wanted:raise ValueError('Cap/order/role-pitch allocation violated')
            if c['InventoryFile']!=c['Id']+'.json.gz' or not sha(c['InventoryHash']):raise ValueError('Missing native inventory')
        elif keys or s or c['InventoryFile'] is not None or c['InventoryHash'] is not None:raise ValueError('Unexpected inventory')
        if c['Mode']=='fair':
            a=rows[k[:-1]+('legacy',)]
            if any(c[f]!=a[f] for f in ('RawHash','RawParticles','KeyHash','Packets','NonParticleHash','EligibleHash','EligibleKeys')):raise ValueError('Trial changed more than selection')
            if c['View']=='live-stage' or len(keys)<=192:
                if any(c[f]!=a[f] for f in ('PixelSha256','PacketHash')):raise ValueError('Stage/under-cap output changed')
        if c['Mode']=='fast':
            a=rows[k[:-1]+('legacy',)]
            if any(c[f]!=a[f] for f in ('PixelSha256','PacketHash','RawHash','KeyHash','RawParticles','Packets')):raise ValueError('Instrumented legacy differs from real default path')
    return rows

def box(view):return [210,45,430,205] if view=='ar-reference' else [210,65,430,225]
def sheets(rows):
    for recipe,view in itertools.product(RECIPES[:2],VIEWS[1:]):
        panes=[]
        for y,phase in enumerate(('chord-held','release-pedal')):
            for x,mode in enumerate(('legacy','fair')):
                c=rows[recipe['Id'],'prism',view,phase,CHECKPOINTS[phase],mode]
                panes.append(dict(id=c['Id'],box=box(view),x=x*220,y=y*160,width=220,height=160))
        yield dict(file=recipe['Id']+'-'+view+'.png',width=440,height=320,panes=panes)
def motions(rows):
    for recipe in RECIPES[:2]:
        yield dict(id=recipe['Id'],file=recipe['Id']+'.bgra.gz',width=440,height=160,frames=16,fps=30,firstFrame=29,box=box('ar-reference'),
            ids=[[rows[recipe['Id'],'prism','ar-reference','motion',f,mode]['Id'] for mode in ('legacy','fair')] for f in range(29,45)])
def motion_frames(path,t):
    from PIL import Image
    size=440*160*4;expected=size*16
    with gzip.open(path/t['file'],'rb') as stream:raw=stream.read(expected+1)
    if len(raw)!=expected:raise ValueError('Wrong temporal bundle size')
    previous=None
    for i in range(16):
        delta=raw[i*size:(i+1)*size];pixels=delta if previous is None else bytes(a^b for a,b in zip(delta,previous))
        yield Image.frombytes('RGBA',(440,160),pixels,'raw','BGRA');previous=pixels

def verify(path,source_root=None,revision=None,published_only=False,publish=None):
    from PIL import Image
    manifest=path/'manifest.json';m=read(manifest if manifest.exists() else path/'manifest.json.gz');rows=validate(m,revision)
    if source_root:
        for name,h in m['sourceHashes'].items():
            data=(source_root/name).read_bytes();data=data if name.endswith('.png') else data.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(data).hexdigest().upper()!=h:raise ValueError('Source drift: '+name)
        old=read(source_root/'docs/evidence/key-particle-ensemble-v1/manifest.json.gz')
        if old['sourceRevision']!=BASELINE:raise ValueError('Frozen baseline refreshed')
        oldrows={c['Id']:c for c in old['captures']}
        for c in rows.values():
            if c['Mode']=='fast' and c['View'] in VIEWS[:2]:
                id_=f"{c['Recipe']}-{c['Palette']}-l100-{c['View']}-{c['Phase']}-mixed"
                if c['PixelSha256']!=oldrows[id_]['PixelSha256']:raise ValueError('Default changed frozen native pixels')
    by_id={c['Id']:c for c in rows.values()}
    if not published_only:
        for k,c in rows.items():
            with Image.open(path/c['File']) as im:
                if im.size!=(640,360) or digest(im)!=c['PixelSha256']:raise ValueError('Native PNG changed')
            if c['InventoryFile']:
                file=path/c['InventoryFile']
                if hashlib.sha256(file.read_bytes()).hexdigest().upper()!=c['InventoryHash']:raise ValueError('Native inventory changed')
                inv=read(file);entries=inv['Eligible']
                if [e['Particle']['Pitch']+128*e['Particle']['Origin'] for e in entries]!=c['EligibleKeys'] or inv['Selected']!=c['Selected'] or inv['Fair']!=(c['Mode']=='fair') or inv['Actual']!=[entries[i]['Quad'] for i in c['Selected']]:raise ValueError('Actual payload/eligibility mismatch')
                if c['Mode']=='fair':
                    a=rows[k[:-1]+('legacy',)];legacy=read(path/a['InventoryFile'])
                    if entries!=legacy['Eligible'] or inv['Start']!=legacy['Start']:raise ValueError('Eligible geometry/RGBA changed')
        m['sheets']=list(sheets(rows));m['motions']=list(motions(rows))
        for s in m['sheets']:
            im=Image.new('RGBA',(440,320))
            for pane in s['panes']:
                with Image.open(path/by_id[pane['id']]['File']) as native:tile=native.convert('RGBA').crop(pane['box'])
                pane['hash']=digest(tile);im.paste(tile,(pane['x'],pane['y']))
            s['hash']=digest(im);im.save(path/s['file'],compress_level=9,optimize=True)
        for t in m['motions']:
            frames=[];hashes=[]
            for ids in t['ids']:
                im=Image.new('RGBA',(440,160));pair=[]
                for x,id_ in enumerate(ids):
                    with Image.open(path/by_id[id_]['File']) as native:tile=native.convert('RGBA').crop(t['box'])
                    pair.append(digest(tile));im.paste(tile,(x*220,0))
                frames.append(im);hashes.append(pair)
            t['hashes']=hashes;(path/t['file']).write_bytes(pack_frames(frames))
        (path/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(m,separators=(',',':')).encode(),mtime=0))
    for name,expected in [('sheets',list(sheets(rows))),('motions',list(motions(rows)))]:
        if len(m.get(name,[]))!=len(expected):raise ValueError('Incomplete publication')
        for s,e in zip(m[name],expected):
            if any(s.get(k)!=v for k,v in e.items() if k!='panes'):raise ValueError('Wrong publication provenance')
            if name=='sheets':
                if len(s['panes'])!=4:raise ValueError('Missing native panes')
                with Image.open(path/s['file']) as im:
                    if im.size!=(440,320) or digest(im)!=s['hash']:raise ValueError('Native sheet changed')
                    for p,q in zip(s['panes'],e['panes']):
                        if any(p[k]!=v for k,v in q.items()) or digest(im.crop((p['x'],p['y'],p['x']+220,p['y']+160)))!=p['hash']:raise ValueError('Native pane changed')
            else:
                if len(s['hashes'])!=16 or any(len(h)!=2 for h in s['hashes']):raise ValueError('Missing temporal hashes')
                for i,im in enumerate(motion_frames(path,s)):
                    if [digest(im.crop((x*220,0,(x+1)*220,160))) for x in range(2)]!=s['hashes'][i]:raise ValueError('Temporal frame changed')
    files=['manifest.json.gz']+[s['file'] for s in m['sheets']]+[t['file'] for t in m['motions']];size=sum((path/f).stat().st_size for f in files)
    if size>BUDGET:raise ValueError(f'Native budget publication {size} exceeds {BUDGET} bytes')
    if publish:
        evidence,previews=publish;evidence.mkdir(parents=True,exist_ok=True);previews.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(path/files[0],evidence/files[0])
        for name in files[1:]:shutil.copyfile(path/name,previews/name)
    return m

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--published-only',action='store_true');p.add_argument('--publish-evidence',type=Path);p.add_argument('--publish-previews',type=Path);a=p.parse_args()
    if bool(a.publish_evidence)!=bool(a.publish_previews):p.error('Both publication paths required')
    try:
        verify(a.directory,a.source_root,a.expected_revision,a.published_only,(a.publish_evidence,a.publish_previews) if a.publish_evidence else None)
        print('PASS 904 native budget captures; unchanged default/frozen pixels, eligibility/RGBA/non-particles, fixed cap and stable subset order; six sheets + two exact temporal slices. Not hardware/default promotion.')
    except Exception as e:print('::error title=AR budget native validation::'+str(e).replace('\n',' ')[:1000],flush=True);raise
if __name__=='__main__':main()
