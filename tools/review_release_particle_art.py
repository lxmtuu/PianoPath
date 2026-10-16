#!/usr/bin/env python3
"""Native Release material pairs. Full validation in CI; publish bounded lossless static crops/lossless BGRA bundles."""
import argparse
import gzip
import hashlib
import itertools
import io
import struct
import json
from pathlib import Path
from review_held_particle_controls import image_pixels

VIEWS=('live-stage','ar-reference','ar-oblique','ar-near-overhead')
PHASES=('burst','held','released','expired')
SOURCES={'Gpu/StageShaders.hlsl','Gpu/SparkParticleAppearance.cs','Gpu/ReleaseParticleAppearance.cs','Gpu/GpuLook.cs','Gpu/GpuStageSimulation.cs',
 'Gpu/GpuStageSimulation.Effects.cs','Gpu/GpuStageRenderer.cs','Gpu/GpuArLayer.cs','Gpu/GpuArLayer.Effects.cs',
 'Gpu/GpuStageFeed.cs','Gpu/GpuRenderLoop.cs','Camera/ArParticleBillboard.cs','Camera/Ar3DNoteProjection.cs',
 'Camera/ArCameraPose.cs','Stage/PianoStage.cs','Diagnostics/KeyParticleFixtures.cs','Diagnostics/HeldParticleControlPlan.cs',
 'Diagnostics/ReleaseParticleArtReview.cs','Diagnostics/AuxParticleFixtures.cs','Diagnostics/AuxParticleTimeline.cs','docs/samples/ar-keyboard-topdown.png'}


def digest(im):return hashlib.sha256(im.convert('RGBA').tobytes('raw','BGRA')).hexdigest().upper()
def assemble_delta(frames):
    """60 exact 1/30s frames, including identical frames; SOURCE delta rectangles, no dropped frames."""
    from PIL import ImageChops
    from make_held_particle_clips import SIGNATURE,chunk,read_png
    if len(frames)!=60 or any(im.size!=frames[0].size for im in frames):raise ValueError('Wrong delta APNG plan')
    opaque=all(im.convert('RGBA').getchannel('A').getextrema()==(255,255) for im in frames)
    result=None;sequence=0;previous=None
    for index,frame in enumerate(frames):
        frame=frame.convert('RGBA');w,h=frame.size;box=(0,0,w,h)
        if previous is not None:
            r,g,b,a=ImageChops.difference(frame,previous).split()
            box=ImageChops.lighter(ImageChops.lighter(r,g),ImageChops.lighter(b,a)).getbbox() or (0,0,1,1)
        buffer=io.BytesIO();tile=frame.crop(box);tile=tile.convert('RGB') if opaque else tile;tile.save(buffer,format='PNG',compress_level=9)
        header,compressed=read_png(buffer.getvalue())
        if result is None:
            result=bytearray(SIGNATURE+chunk(b'IHDR',header)+chunk(b'acTL',struct.pack('>II',60,0)))
        x,y,x1,y1=box
        result+=chunk(b'fcTL',struct.pack('>IIIIIHHBB',sequence,x1-x,y1-y,x,y,1,30,0,0));sequence+=1
        if index==0:result+=chunk(b'IDAT',compressed)
        else:result+=chunk(b'fdAT',struct.pack('>I',sequence)+compressed);sequence+=1
        previous=frame
    return bytes(result+chunk(b'IEND',b''))


def pack_frames(frames):
    """Lossless full-frame XOR + gzip. Frame count/rate live in the guarded manifest, never dropped."""
    raw=bytearray();previous=None
    for frame in frames:
        pixels=frame.convert('RGBA').tobytes('raw','BGRA')
        raw.extend(pixels if previous is None else bytes(a^b for a,b in zip(pixels,previous)))
        previous=pixels
    return gzip.compress(raw,compresslevel=9,mtime=0)


def paired_frames(directory,track):
    from PIL import Image
    if track.get('pairedEncoding')!='xor-bgra-gzip-v1' or track['pairedFile']!=track['Id']+'.bgra.gz':raise ValueError('Wrong Release frame encoding')
    w=track['crop']['Width']*2;h=track['crop']['Height'];size=w*h*4;expected=size*60
    # Bound decompression before allocating untrusted frame data.
    with gzip.open(directory/track['pairedFile'],'rb') as stream:raw=stream.read(expected+1)
    if len(raw)!=expected:raise ValueError('Wrong Release frame bundle length')
    previous=None
    for i in range(60):
        delta=raw[i*size:(i+1)*size]
        pixels=delta if previous is None else bytes(a^b for a,b in zip(delta,previous))
        yield Image.frombytes('RGBA',(w,h),pixels,'raw','BGRA')
        previous=pixels


def read(p):
    if p.suffix=='.gz':return json.loads(gzip.decompress(p.read_bytes()))
    return json.loads(p.read_text(encoding='utf-8-sig'))
def check_counts(record):
    if any(type(record[k]) is not int or record[k]<0 for k in ('RawParticles','PackedParticles')) or record['PackedParticles']>record['RawParticles']:
        raise ValueError('Bad release counts')


def check_digest(value):
    if not isinstance(value,str) or len(value)!=64 or any(c not in '0123456789ABCDEF' for c in value):raise ValueError('Bad release digest')


def validate(m,revision=None):
    if revision is not None and m['sourceRevision']!=revision:raise ValueError('Wrong release source revision')
    if m['schema']!=4:raise ValueError('Unknown release schema')
    if m['family'] not in ('release-float-up','release-dissolve','release-smoke'):raise ValueError('Unknown release family')
    if (m['family'],m['reviewOnly'],m['legacyParticleAxesForReview'],m['width'],m['height'],m['seed'],m['captureCount'],m['staticFps'],m['staticReleaseFrame'])!=(m['family'],True,False,640,360,20261014,192,60,21):raise ValueError('Wrong release recipe')
    if m.get('staticSampleFrames')!=[23,31,47,110] or m.get('releaseOffsets')!=[2,10,26,89] or m.get('motionPressVelocities')!=[.9,.9]:raise ValueError('Wrong Release input timeline')
    if set(m['sourceHashes'])!=SOURCES:raise ValueError('Missing release source guards')
    expected=set(itertools.product(('prism','indigo'),VIEWS,(60,61),PHASES,('off','classic','art')))
    keys=[(c['Palette'],c['View'],c['Pitch'],c['Phase'],c['Variant']) for c in m['captures']]
    if len(keys)!=192 or set(keys)!=expected:raise ValueError('Incomplete release matrix')
    groups={}
    for c in m['captures']:
        id_=f"{c['Palette']}-{c['View']}-p{c['Pitch']}-{c['Phase']}"
        if c['Id']!=id_ or c['File']!=id_+'-'+c['Variant']+'.png':raise ValueError('Bad static release provenance')
        for k in ('PixelSha256','RawHash','PacketHash'):check_digest(c[k])
        check_counts(c)
        if c['Variant']=='off' or c['Phase']=='expired':
            if c['RawParticles'] or c['PackedParticles']:raise ValueError('Off/expired release particles')
        elif not c['PackedParticles']:raise ValueError('Missing active releases')
        groups.setdefault(id_,{})[c['Variant']]=c
    if m['schema']==4:
        for c in m['captures']:
            check_digest(c['ColourHash'])
            if c['Variant']=='art':
                stage=groups[f"{c['Palette']}-live-stage-p{c['Pitch']}-{c['Phase']}"]['art']
                if any(c[k]!=stage[k] for k in ('RawHash','PackedParticles','ColourHash')):raise ValueError('Shared v2 colour/alpha differs across outputs')
    for group in groups.values():
        a,b=group['classic'],group['art']
        if any(a[k]!=b[k] for k in ('RawHash','PacketHash','RawParticles','PackedParticles')):raise ValueError('Release state/actual packet changed')
        if (a['Phase']=='expired') != (a['PixelSha256']==b['PixelSha256']):raise ValueError('Wrong Release material delta')
        if a['Phase']!='expired' and b['PixelSha256']==group['off']['PixelSha256']:raise ValueError('Active Release candidate contributes no native off-delta')
        if a['Phase']=='expired' and a['PixelSha256']!=group['off']['PixelSha256']:raise ValueError('Expired output differs from off')
    if m.get('stageDepthTrial')!='candidate Stage depth .01; material-only control depth .6; AR unchanged':raise ValueError('Missing explicit Release depth experiment')
    controls=m.get('stageDepthControls',[])
    expected={c['Id'] for c in m['captures'] if c['View']=='live-stage' and c['Variant']=='art'}
    if len(controls)!=16 or {c['Id'] for c in controls}!=expected:raise ValueError('Incomplete material-only Stage controls')
    for c in controls:
        if c['Variant']!='material-only' or c['View']!='live-stage' or c['File']!=c['Id']+'-material-only.png':raise ValueError('Wrong Stage depth control')
        a=groups[c['Id']]['art'];check_counts(c)
        for key in ('RawHash','PacketHash','ColourHash','PixelSha256'):check_digest(c[key])
        if any(c[key]!=a[key] for key in ('RawHash','PacketHash','ColourHash','RawParticles','PackedParticles')):raise ValueError('Front-layer changed more than depth/tag')
        if c['Phase']=='expired' and c['PixelSha256']!=a['PixelSha256']:raise ValueError('Depth changed expired output')
    pedal=m.get('pedalControls',[])
    if len(pedal)!=64 or {c['Id'] for c in pedal}!=set(groups):raise ValueError('Incomplete independent pedal controls')
    for c in pedal:
        a=groups[c['Id']]['art'];check_counts(c)
        if c['Variant']!='pedal-off' or c['File']!=c['Id']+'-pedal-off.png':raise ValueError('Wrong pedal control')
        for key in ('PixelSha256','RawHash','PacketHash','ColourHash'):check_digest(c[key])
        if any(c[k]!=a[k] for k in ('Palette','View','Pitch','Phase','RawHash','PacketHash','ColourHash','RawParticles','PackedParticles')):raise ValueError('Pedal changed release birth/material/packing')
    pre=m.get('preReleaseControls',[])
    expected={c['Id'] for c in m['captures'] if c['Variant']=='art' and c['Phase']=='burst'}
    if len(pre)!=16 or {c['Candidate']['Id'] for c in pre}!=expected:raise ValueError('Incomplete pre-release controls')
    for pair in pre:
        a,b=pair['Candidate'],pair['Off']
        for c,variant in ((a,'before-release'),(b,'before-off')):
            check_counts(c)
            if c['Variant']!=variant or c['File']!=c['Id']+'-'+variant+'.png' or c['Id']!=a['Id']:raise ValueError('Bad pre-release provenance')
            for k in ('PixelSha256','RawHash','PacketHash','ColourHash'):check_digest(c[k])
            target=groups[c['Id']]['art']
            if any(c[k]!=target[k] for k in ('Palette','View','Pitch','Phase')):raise ValueError('Wrong pre-release scene')
            if c['RawParticles'] or c['PackedParticles']:raise ValueError('Release emitted before physical Note Off')
        if a['PixelSha256']!=b['PixelSha256']:raise ValueError('Release changed pre-release scene')
    if len(m['motions'])!=4 or {x['Id'] for x in m['motions']}!={v+'-'+p for v in VIEWS[:2] for p in ('burst','held')}:raise ValueError('Missing release motion')
    for track in m['motions']:
        if track['Id']!=track['view']+'-'+track['phase'] or (track['fps'],track['count'],track['releaseFrame'])!=(30,60,12):raise ValueError('Bad release timeline')
        expected_emission='physical Note Off f12; pedal '+('on' if track['phase']=='held' else 'off')+'; no impact/held emitter'
        if track.get('emission')!=expected_emission:raise ValueError('Wrong physical release/pedal recipe')
        c=track['crop']
        if not (0<=c['X']<640 and 0<=c['Y']<360 and 0<c['Width']<=640-c['X'] and 0<c['Height']<=360-c['Y']):raise ValueError('Bad native crop')
        versions={t['variant']:t for t in track['tracks']}
        if len(track['tracks'])!=3 or set(versions)!={'off','classic','art'}:raise ValueError('Bad release variants')
        for variant,t in versions.items():
            if len(t['hashes'])!=60 or len(t['frames'])!=60:raise ValueError('Missing release frames')
            for h,f in zip(t['hashes'],t['frames']):
                check_digest(h);check_counts(f)
                if m['schema']==4:check_digest(f['ColourHash'])
                for k in ('RawHash','PacketHash','fullPixelSha256'):check_digest(f[k])
                if variant=='off' and (f['RawParticles'] or f['PackedParticles']):raise ValueError('Motion off emitted releases')
        for a,b in zip(versions['classic']['frames'],versions['art']['frames']):
            if any(a[k]!=b[k] for k in ('RawHash','PacketHash','RawParticles','PackedParticles')):raise ValueError('Motion state/packet changed')
        if sum(a!=b for a,b in zip(versions['classic']['hashes'],versions['art']['hashes']))<6:raise ValueError('Invisible release motion candidate')
        if sum(a!=b for a,b in zip(versions['off']['hashes'],versions['art']['hashes']))<6:raise ValueError('Blank release motion candidate')
    # Actual lifecycle and CC64 independence throughout the captured sequence, not only four stills.
    by_id={t['Id']:t for t in m['motions']}
    for view in VIEWS[:2]:
        on,off=(by_id[view+'-'+phase] for phase in ('held','burst'))
        a={t['variant']:t for t in on['tracks']};b={t['variant']:t for t in off['tracks']}
        for variant in ('classic','art'):
            for x,y in zip(a[variant]['frames'],b[variant]['frames']):
                if any(x[k]!=y[k] for k in ('RawHash','PacketHash','ColourHash','RawParticles','PackedParticles')):raise ValueError('Motion pedal changed release state/material/packing')
        for track in (on,off):
            variants={t['variant']:t for t in track['tracks']}
            for variant in ('classic','art'):
                frames=variants[variant]['frames']
                if any(f['RawParticles'] or f['PackedParticles'] for f in frames[:12]):raise ValueError('Motion released before Note Off')
                if any(x!=y for x,y in zip(variants[variant]['hashes'][:12],variants['off']['hashes'][:12])):raise ValueError('Pre-release motion changed pixels')
                if not frames[12]['RawParticles'] or not frames[12]['PackedParticles']:raise ValueError('No physical release birth at f12')
                if any(y['RawParticles']>x['RawParticles'] for x,y in zip(frames[12:],frames[13:])):raise ValueError('Release became a held/pedal emitter')
                if frames[59]['RawParticles'] or frames[59]['PackedParticles']:raise ValueError('Release failed to expire independently of sustain')
    return groups


def verify(directory,source_root=None,revision=None,published_only=False):
    from PIL import Image,ImageChops
    manifest=directory/'manifest.json'
    if not manifest.exists():manifest=directory/'manifest.json.gz'
    m=read(manifest);groups=validate(m,revision)
    if not published_only:
        for c in m['stageDepthControls']+m['pedalControls']+[p[k] for p in m['preReleaseControls'] for k in ('Candidate','Off')]:
            if digest(Image.open(directory/c['File']))!=c['PixelSha256']:raise ValueError('Stage material-only native pixels changed')
    if source_root:
        for name,expected in m['sourceHashes'].items():
            raw=(source_root/name).read_bytes()
            if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(raw).hexdigest().upper()!=expected:raise ValueError('Release source changed: '+name)
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
                if not bounds:raise ValueError('No release signal for publication')
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
            name=t['Id']+'.bgra.gz';t['pairedFile']=name;t['pairedEncoding']='xor-bgra-gzip-v1'
            # Lossless delta rectangles; no palette quantisation or frame dropping.
            (directory/name).write_bytes(pack_frames(frames))
        (directory/'manifest.json').write_text(json.dumps(m,ensure_ascii=False,separators=(',',':'))+'\n')
    if len(m['sheets'])!=2:raise ValueError('Wrong release published sheets')
    for s,phase in zip(m['sheets'],('burst','held')):
        if s['file']!=phase+'.png' or len(s['panes'])!=4:raise ValueError('Wrong release sheet plan')
        im=image_pixels(directory/s['file'],(s['width'],s['height']),s['hash'])
        for i,p in enumerate(s['panes']):
            if p['id']!=f'prism-{VIEWS[i//2]}-p60-{phase}' or p['variant']!=('classic','art')[i%2]:raise ValueError('Wrong release pane provenance')
            if (len(p['box'])!=4 or any(type(v) is not int for v in p['box'])
                or not (0<=p['box'][0]<p['box'][2]<=640 and 0<=p['box'][1]<p['box'][3]<=360)):
                raise ValueError('Invalid native crop coordinates')
            if p['box'][2]-p['box'][0]!=p['width'] or p['box'][3]-p['box'][1]!=p['height']:raise ValueError('Wrong release crop size')
            if digest(im.crop((p['x'],p['y'],p['x']+p['width'],p['y']+p['height'])))!=p['hash']:raise ValueError('Release native pane changed')
            if not published_only:
                c=groups[p['id']][p['variant']]
                with Image.open(directory/c['File']) as full:
                    if digest(full.crop(p['box']))!=p['hash']:raise ValueError('Release crop differs from native frame')
    for t in m['motions']:
        versions={v['variant']:v for v in t['tracks']};w=t['crop']['Width'];h=t['crop']['Height']
        for f,im in enumerate(paired_frames(directory,t)):
            for i,v in enumerate(('classic','art')):
                if digest(im.crop((i*w,0,(i+1)*w,h)))!=versions[v]['hashes'][f]:raise ValueError('Frame half differs from native frame')
    if not published_only and m['schema']==4:
        (directory/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(m,separators=(',',':')).encode(),mtime=0))
    return m


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--expected-family');p.add_argument('--published-only',action='store_true');a=p.parse_args()
    m=verify(a.directory,a.source_root,a.expected_revision,a.published_only)
    if a.expected_family and m['family']!=a.expected_family:raise ValueError('Wrong publication family')
    print('PASS Release: 192 main statics + 16 depth + 64 pedal + 32 pre-release images, 64 paired raw/actual-packet guards (48 active, 16 expired), 720 motion records; 8 published native crops and 480 lossless synchronized frame halves. '+('Full originals NOT checked locally.' if a.published_only else 'All original static/motion pixels verified.'))
