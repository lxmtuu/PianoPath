#!/usr/bin/env python3
"""Native Embers material pairs. Full validation in CI; publish bounded lossless static crops/APNG pairs."""
import argparse
import hashlib
import itertools
import io
import struct
import json
from pathlib import Path
from review_held_particle_controls import image_pixels

VIEWS=('live-stage','ar-reference','ar-oblique','ar-near-overhead')
PHASES=('burst','held','released','expired')
SOURCES={'Gpu/StageShaders.hlsl','Gpu/SparkParticleAppearance.cs','Gpu/GpuLook.cs','Gpu/GpuStageSimulation.cs',
 'Gpu/GpuStageSimulation.Effects.cs','Gpu/GpuStageRenderer.cs','Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs',
 'Gpu/GpuStageFeed.cs','Gpu/GpuRenderLoop.cs','Camera/ArParticleBillboard.cs','Camera/Ar3DNoteProjection.cs',
 'Camera/ArCameraPose.cs','Stage/PianoStage.cs','Diagnostics/KeyParticleFixtures.cs','Diagnostics/HeldParticleControlPlan.cs',
 'Diagnostics/SparkParticleArtReview.cs','docs/samples/ar-keyboard-topdown.png'}


def digest(im):return hashlib.sha256(im.convert('RGBA').tobytes('raw','BGRA')).hexdigest().upper()
def assemble_delta(frames):
    """60 exact 1/30s frames, including identical frames; SOURCE delta rectangles, no dropped frames."""
    from PIL import ImageChops
    from make_held_particle_clips import SIGNATURE,chunk,read_png
    if len(frames)!=60 or any(im.size!=frames[0].size for im in frames):raise ValueError('Wrong delta APNG plan')
    result=None;sequence=0;previous=None
    for index,frame in enumerate(frames):
        frame=frame.convert('RGBA');w,h=frame.size;box=(0,0,w,h)
        if previous is not None:
            r,g,b,a=ImageChops.difference(frame,previous).split()
            box=ImageChops.lighter(ImageChops.lighter(r,g),ImageChops.lighter(b,a)).getbbox() or (0,0,1,1)
        buffer=io.BytesIO();frame.crop(box).save(buffer,format='PNG',compress_level=9)
        header,compressed=read_png(buffer.getvalue())
        if result is None:
            result=bytearray(SIGNATURE+chunk(b'IHDR',header)+chunk(b'acTL',struct.pack('>II',60,0)))
        x,y,x1,y1=box
        result+=chunk(b'fcTL',struct.pack('>IIIIIHHBB',sequence,x1-x,y1-y,x,y,1,30,0,0));sequence+=1
        if index==0:result+=chunk(b'IDAT',compressed)
        else:result+=chunk(b'fdAT',struct.pack('>I',sequence)+compressed);sequence+=1
        previous=frame
    return bytes(result+chunk(b'IEND',b''))


def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def check_counts(record):
    if any(type(record[k]) is not int or record[k]<0 for k in ('RawParticles','PackedParticles')) or record['PackedParticles']>record['RawParticles']:
        raise ValueError('Bad spark counts')


def check_digest(value):
    if not isinstance(value,str) or len(value)!=64 or any(c not in '0123456789ABCDEF' for c in value):raise ValueError('Bad spark digest')


def validate(m,revision=None):
    if revision is not None and m['sourceRevision']!=revision:raise ValueError('Wrong spark source revision')
    if (m['schema'],m['family'],m['reviewOnly'],m['legacyParticleAxesForReview'],m['width'],m['height'],m['seed'],m['captureCount'],m['staticFps'],m['staticReleaseFrame'])!=(1,'embers',True,False,640,360,20261014,192,60,21):raise ValueError('Wrong spark recipe')
    if set(m['sourceHashes'])!=SOURCES:raise ValueError('Missing spark source guards')
    expected=set(itertools.product(('prism','indigo'),VIEWS,(60,61),PHASES,('off','classic','art')))
    keys=[(c['Palette'],c['View'],c['Pitch'],c['Phase'],c['Variant']) for c in m['captures']]
    if len(keys)!=192 or set(keys)!=expected:raise ValueError('Incomplete spark matrix')
    groups={}
    for c in m['captures']:
        id_=f"{c['Palette']}-{c['View']}-p{c['Pitch']}-{c['Phase']}"
        if c['Id']!=id_ or c['File']!=id_+'-'+c['Variant']+'.png':raise ValueError('Bad static spark provenance')
        for k in ('PixelSha256','RawHash','PacketHash'):check_digest(c[k])
        check_counts(c)
        if c['Variant']=='off' or c['Phase']=='expired':
            if c['RawParticles'] or c['PackedParticles']:raise ValueError('Off/expired spark particles')
        elif not c['PackedParticles']:raise ValueError('Missing active sparks')
        groups.setdefault(id_,{})[c['Variant']]=c
    for group in groups.values():
        a,b=group['classic'],group['art']
        if any(a[k]!=b[k] for k in ('RawHash','PacketHash','RawParticles','PackedParticles')):raise ValueError('Spark state/actual packet changed')
        if (a['Phase']=='expired') != (a['PixelSha256']==b['PixelSha256']):raise ValueError('Wrong spark material delta')
        if a['Phase']=='expired' and a['PixelSha256']!=group['off']['PixelSha256']:raise ValueError('Expired output differs from off')
    if len(m['motions'])!=4 or {x['Id'] for x in m['motions']}!={v+'-'+p for v in VIEWS[:2] for p in ('burst','held')}:raise ValueError('Missing spark motion')
    for track in m['motions']:
        if track['Id']!=track['view']+'-'+track['phase'] or (track['fps'],track['count'],track['releaseFrame'])!=(30,60,12):raise ValueError('Bad spark timeline')
        c=track['crop']
        if not (0<=c['X']<640 and 0<=c['Y']<360 and 0<c['Width']<=640-c['X'] and 0<c['Height']<=360-c['Y']):raise ValueError('Bad native crop')
        versions={t['variant']:t for t in track['tracks']}
        if len(track['tracks'])!=3 or set(versions)!={'off','classic','art'}:raise ValueError('Bad spark variants')
        for variant,t in versions.items():
            if len(t['hashes'])!=60 or len(t['frames'])!=60:raise ValueError('Missing spark frames')
            for h,f in zip(t['hashes'],t['frames']):
                check_digest(h);check_counts(f)
                for k in ('RawHash','PacketHash','fullPixelSha256'):check_digest(f[k])
                if variant=='off' and (f['RawParticles'] or f['PackedParticles']):raise ValueError('Motion off emitted sparks')
        for a,b in zip(versions['classic']['frames'],versions['art']['frames']):
            if any(a[k]!=b[k] for k in ('RawHash','PacketHash','RawParticles','PackedParticles')):raise ValueError('Motion state/packet changed')
        if sum(a!=b for a,b in zip(versions['classic']['hashes'],versions['art']['hashes']))<6:raise ValueError('Invisible spark motion candidate')
    return groups


def verify(directory,source_root=None,revision=None,published_only=False):
    from PIL import Image,ImageChops
    m=read(directory/'manifest.json');groups=validate(m,revision)
    if source_root:
        for name,expected in m['sourceHashes'].items():
            raw=(source_root/name).read_bytes()
            if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(raw).hexdigest().upper()!=expected:raise ValueError('Spark source changed: '+name)
    if not published_only:
        for c in m['captures']:image_pixels(directory/c['File'],(640,360),c['PixelSha256'])
        for t in m['motions']:
            size=(t['crop']['Width'],t['crop']['Height'])
            for v in t['tracks']:
                for f,h in enumerate(v['hashes']):image_pixels(directory/'motion'/t['Id']/v['variant']/f'{f:03}.png',size,h)
        m['sheets']=[]
        # Native crops, not thumbnails. Each row's crop is the union of same-time off deltas.
        for phase in ('burst','held'):
            panes=[]
            for view in VIEWS[:2]:
                group=groups[f'prism-{view}-p60-{phase}']
                off=Image.open(directory/group['off']['File']).convert('RGB')
                bounds=None
                for variant in ('classic','art'):
                    im=Image.open(directory/group[variant]['File']).convert('RGB')
                    box=ImageChops.difference(im,off).getbbox()
                    if box:bounds=box if bounds is None else (min(bounds[0],box[0]),min(bounds[1],box[1]),max(bounds[2],box[2]),max(bounds[3],box[3]))
                if not bounds:raise ValueError('No spark signal for publication')
                box=(max(0,bounds[0]-8),max(0,bounds[1]-8),min(640,bounds[2]+8),min(360,bounds[3]+8))
                for variant in ('classic','art'):
                    c=group[variant];im=Image.open(directory/c['File']).convert('RGBA').crop(box)
                    panes.append((c,im,box))
            w=max(im.width for _,im,_ in panes);h=max(im.height for _,im,_ in panes)
            sheet=Image.new('RGBA',(w*2,h*2),(16,20,27,255));records=[]
            for i,(c,im,box) in enumerate(panes):
                x=i%2*w;y=i//2*h;sheet.paste(im,(x,y))
                records.append(dict(id=c['Id'],variant=c['Variant'],box=box,x=x,y=y,width=im.width,height=im.height,hash=digest(im)))
            name=phase+'.png';sheet.save(directory/name,optimize=True,compress_level=9)
            m['sheets'].append(dict(file=name,width=w*2,height=h*2,hash=digest(sheet),panes=records))
        for t in m['motions']:
            tracks={v['variant']:v for v in t['tracks']};frames=[]
            for f in range(60):
                pair=Image.new('RGBA',(t['crop']['Width']*2,t['crop']['Height']))
                for i,variant in enumerate(('classic','art')):
                    with Image.open(directory/'motion'/t['Id']/variant/f'{f:03}.png') as im:pair.paste(im.convert('RGBA'),(i*t['crop']['Width'],0))
                frames.append(pair)
            name=t['Id']+'.png';t['pairedFile']=name
            # Lossless delta rectangles; no palette quantisation or frame dropping.
            (directory/name).write_bytes(assemble_delta(frames))
        (directory/'manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n')
    if len(m['sheets'])!=2:raise ValueError('Wrong spark published sheets')
    for s,phase in zip(m['sheets'],('burst','held')):
        if s['file']!=phase+'.png' or len(s['panes'])!=4:raise ValueError('Wrong spark sheet plan')
        im=image_pixels(directory/s['file'],(s['width'],s['height']),s['hash'])
        for i,p in enumerate(s['panes']):
            if p['id']!=f'prism-{VIEWS[i//2]}-p60-{phase}' or p['variant']!=('classic','art')[i%2]:raise ValueError('Wrong spark pane provenance')
            if (len(p['box'])!=4 or any(type(v) is not int for v in p['box'])
                or not (0<=p['box'][0]<p['box'][2]<=640 and 0<=p['box'][1]<p['box'][3]<=360)):
                raise ValueError('Invalid native crop coordinates')
            if p['box'][2]-p['box'][0]!=p['width'] or p['box'][3]-p['box'][1]!=p['height']:raise ValueError('Wrong spark crop size')
            if digest(im.crop((p['x'],p['y'],p['x']+p['width'],p['y']+p['height'])))!=p['hash']:raise ValueError('Spark native pane changed')
            if not published_only:
                c=groups[p['id']][p['variant']]
                with Image.open(directory/c['File']) as full:
                    if digest(full.crop(p['box']))!=p['hash']:raise ValueError('Spark crop differs from native frame')
    for t in m['motions']:
        if t['pairedFile']!=t['Id']+'.png':raise ValueError('Wrong motion file')
        versions={v['variant']:v for v in t['tracks']};w=t['crop']['Width'];h=t['crop']['Height']
        with Image.open(directory/t['pairedFile']) as im:
            if im.size!=(w*2,h) or im.n_frames!=60:raise ValueError('Wrong APNG dimensions/count')
            for f in range(60):
                im.seek(f)
                if abs(im.info.get('duration',0)-1000/30)>.002:raise ValueError('Wrong APNG timing')
                for i,v in enumerate(('classic','art')):
                    if digest(im.convert('RGBA').crop((i*w,0,(i+1)*w,h)))!=versions[v]['hashes'][f]:raise ValueError('APNG half differs from native frame')
    return m


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--published-only',action='store_true');a=p.parse_args()
    verify(a.directory,a.source_root,a.expected_revision,a.published_only)
    print('PASS Embers: 192 static records, 64 paired raw/actual-packet guards, 720 motion records; 8 published native crops and 480 synchronized APNG halves. '+('Full originals NOT checked locally.' if a.published_only else 'All original static/motion pixels verified.'))
