#!/usr/bin/env python3
"""Native Shatter geometry trial. Same V1 material both sides; strict state/material/non-body guards."""
import argparse,gzip,hashlib,itertools,json,math
from pathlib import Path
from review_shatter_particle_art import SOURCES as V1_SOURCES,digest,pack_frames,paired_frames
from review_held_particle_controls import image_pixels

SOURCES=V1_SOURCES|{'Diagnostics/ShatterBillboardReview.cs'}
VIEWS=('live-stage','ar-reference','ar-oblique','ar-near-overhead')
SAMPLES=(2,10,26,89)
VARIANTS=('off','before','after')
TRACKS={f'{v}-{p}' for v in (VIEWS[1],VIEWS[3]) for p in ('burst','held')}
GUARDS=('RawHash','SelectedHash','ColourHash','UnchangedHash','Count')

def read(path):return json.loads(gzip.decompress(path.read_bytes()) if path.suffix=='.gz' else path.read_text(encoding='utf-8-sig'))
def hash_(x):
    if not isinstance(x,str) or len(x)!=64 or any(c not in '0123456789ABCDEF' for c in x):raise ValueError('Bad native digest')
def record(c):
    for k in ('PixelSha256','RawHash','SelectedHash','ColourHash','GeometryHash','UnchangedHash'):hash_(c[k])
    if type(c['Count']) is not int or not 0<=c['Count']<=192:raise ValueError('Bad physical body count')
    if type(c['MaxCornerError']) not in (float,int) or not math.isfinite(c['MaxCornerError']) or not 0<=c['MaxCornerError']<=.0003:raise ValueError('Uploaded corners/weights drifted')
    for k in ('Signal','VisiblePixels'):
        if type(c[k]) is not int or c[k]<0:raise ValueError('Bad signal metric')

def pair(a,b,ar,active):
    if any(a[k]!=b[k] for k in GUARDS):raise ValueError('State/ordered identity/material/non-body packet changed')
    if not ar and a['PixelSha256']!=b['PixelSha256']:raise ValueError('Stage pixels changed')
    if (ar and active)!=(a['GeometryHash']!=b['GeometryHash']):raise ValueError('Unexpected actual geometry delta')

def validate(m,revision=None):
    if (m['schema'],m['family'],m['reviewOnly'],m['width'],m['height'],m['seed'],m['staticFps'],m['staticCount'],m['lineage'])!=(1,'shatter-billboard',True,640,360,20261014,60,624,64):raise ValueError('Wrong billboard recipe')
    if revision is not None and m['sourceRevision']!=revision:raise ValueError('Wrong source revision')
    if m['samples']!=list(SAMPLES) or set(m['sourceHashes'])!=SOURCES:raise ValueError('Missing source/timeline guards')
    if [(d['TiltDegrees'],d['AzimuthDegrees']) for d in m['directions']]!=[(0,0),(45,-45),(90,90),(135,60)]:raise ValueError('Wrong note directions')
    for h in m['sourceHashes'].values():hash_(h)
    expected={(p,v,d,k,f,a) for p in ('prism','indigo') for v in VIEWS for d in range(1 if v=='live-stage' else 4) for k in (60,61) for f in SAMPLES for a in VARIANTS}
    rows=m['captures'];keys=[(c['Palette'],c['View'],c['Direction'],c['Pitch'],c['Sample'],c['Variant']) for c in rows]
    if len(rows)!=624 or set(keys)!=expected:raise ValueError('Incomplete/duplicate static matrix')
    groups={}
    for c in rows:
        record(c);id_=f"{c['Palette']}-{c['View']}-d{c['Direction']}-p{c['Pitch']}-f{c['Sample']}"
        if c['Id']!=id_ or c['File']!=id_+'-'+c['Variant']+'.png':raise ValueError('Static provenance changed')
        if c['Variant']=='off' or c['Sample']==89:
            if c['Count']:raise ValueError('Off/expired emitted particles')
        elif not c['Count']:raise ValueError('Missing active bodies')
        groups.setdefault(id_,{})[c['Variant']]=c
    for g in groups.values():
        a,b,off=g['before'],g['after'],g['off'];active=b['Sample']!=89
        pair(a,b,b['View']!='live-stage',active)
        if active and (b['PixelSha256']==off['PixelSha256'] or b['Signal']==0):raise ValueError('Aligned active static is raster-empty')
        if not active and (a['PixelSha256']!=off['PixelSha256'] or b['PixelSha256']!=off['PixelSha256']):raise ValueError('Expired scene changed')
        stage=groups[f"{b['Palette']}-live-stage-d0-p{b['Pitch']}-f{b['Sample']}"]['after']
        if any(b[k]!=stage[k] for k in ('RawHash','ColourHash','Count')):raise ValueError('Stage/AR intrinsic body RGBA or raw state differs')
    if len(m['motions'])!=4 or {t['Id'] for t in m['motions']}!=TRACKS:raise ValueError('Incomplete motion tracks')
    for t in m['motions']:
        if t['Id']!=t['view']+'-'+t['phase'] or (t['fps'],t['count'],t['pedalFrame'],t['velocity'])!=(30,60,12,.45 if t['phase']=='held' else .9):raise ValueError('Wrong single-impact motion timeline')
        if (t['pitch'],t['direction'])!=((60,0) if t['view']=='ar-reference' else (61,2)):raise ValueError('Motion regression fixture changed')
        box=t['crop']
        if any(type(box[k]) is not int for k in ('X','Y','Width','Height')) or not (0<=box['X']<640 and 0<=box['Y']<360 and 0<box['Width']<=640-box['X'] and 0<box['Height']<=360-box['Y']):raise ValueError('Invalid crop')
        versions={v['variant']:v for v in t['tracks']}
        if len(t['tracks'])!=3 or set(versions)!=set(VARIANTS):raise ValueError('Missing motion variant')
        for v in versions.values():
            if len(v['hashes'])!=60 or len(v['frames'])!=60:raise ValueError('Dropped motion frame')
            for h,c in zip(v['hashes'],v['frames']):
                hash_(h);record(c)
                if c['Variant']!=v['variant'] or c['Id']!=t['Id']:raise ValueError('Motion provenance drift')
                if v['variant']=='off' and c['Count']:raise ValueError('Off motion emitted')
        for a,b in zip(versions['before']['frames'],versions['after']['frames']):pair(a,b,True,b['Count']>0)
        if sum(a!=b for a,b in zip(versions['before']['hashes'],versions['after']['hashes']))<6:raise ValueError('No visible motion geometry trial')
        if sum(a!=b for a,b in zip(versions['off']['hashes'],versions['after']['hashes']))<6:raise ValueError('Aligned motion blank')
    return groups

def verify(directory,source_root=None,revision=None,published_only=False):
    from PIL import Image,ImageChops
    path=directory/'manifest.json'
    if not path.exists():path=directory/'manifest.json.gz'
    m=read(path);groups=validate(m,revision)
    if source_root:
        for name,h in m['sourceHashes'].items():
            raw=(source_root/name).read_bytes()
            if not name.endswith('.png'):raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode()
            if hashlib.sha256(raw).hexdigest().upper()!=h:raise ValueError('Source changed: '+name)
    if not published_only:
        for c in m['captures']:image_pixels(directory/c['File'],(640,360),c['PixelSha256'])
        for t in m['motions']:
            for v in t['tracks']:
                for f,h in enumerate(v['hashes']):image_pixels(directory/'motion'/t['Id']/v['variant']/f'{f:03}.png',(t['crop']['Width'],t['crop']['Height']),h)
        m['sheets']=[]
        for sample,pitch,name in ((2,60,'early.png'),(10,61,'mid.png')):
            panes=[]
            for view,direction in (('live-stage',0),('ar-reference',0),('ar-near-overhead',2)):
                group=groups[f'prism-{view}-d{direction}-p{pitch}-f{sample}'];off=Image.open(directory/group['off']['File']).convert('RGB');bounds=None
                for variant in ('before','after'):
                    im=Image.open(directory/group[variant]['File']).convert('RGB');box=ImageChops.difference(im,off).getbbox()
                    if box:bounds=box if bounds is None else (min(bounds[0],box[0]),min(bounds[1],box[1]),max(bounds[2],box[2]),max(bounds[3],box[3]))
                if not bounds:raise ValueError('No native static crop signal')
                box=(max(0,bounds[0]-8),max(0,bounds[1]-8),min(640,bounds[2]+8),min(360,bounds[3]+8))
                for variant in ('before','after'):
                    c=group[variant];im=Image.open(directory/c['File']).convert('RGBA').crop(box);panes.append((c,im,box))
            w=max(im.width for _,im,_ in panes);h=max(im.height for _,im,_ in panes);sheet=Image.new('RGBA',(w*2,h*3),(16,20,27,255));records=[]
            for i,(c,im,box) in enumerate(panes):
                x=i%2*w;y=i//2*h;sheet.paste(im,(x,y));records.append(dict(id=c['Id'],variant=c['Variant'],box=box,x=x,y=y,width=im.width,height=im.height,hash=digest(im)))
            sheet.save(directory/name,optimize=True,compress_level=9);m['sheets'].append(dict(file=name,width=w*2,height=h*3,hash=digest(sheet),panes=records))
        for t in m['motions']:
            w=t['crop']['Width'];h=t['crop']['Height'];frames=[]
            for f in range(60):
                im=Image.new('RGBA',(w*2,h))
                for i,v in enumerate(('before','after')):
                    with Image.open(directory/'motion'/t['Id']/v/f'{f:03}.png') as half:im.paste(half.convert('RGBA'),(i*w,0))
                frames.append(im)
            t['pairedFile']=t['Id']+'.bgra.gz';t['pairedEncoding']='xor-bgra-gzip-v1';(directory/t['pairedFile']).write_bytes(pack_frames(frames))
    if len(m.get('sheets',[]))!=2:raise ValueError('Missing published sheets')
    for s,(sample,pitch,name) in zip(m['sheets'],((2,60,'early.png'),(10,61,'mid.png'))):
        if s['file']!=name or len(s['panes'])!=6:raise ValueError('Wrong sheet recipe')
        im=image_pixels(directory/name,(s['width'],s['height']),s['hash'])
        for i,p in enumerate(s['panes']):
            view,direction=(('live-stage',0),('ar-reference',0),('ar-near-overhead',2))[i//2];variant=('before','after')[i%2]
            if p['id']!=f'prism-{view}-d{direction}-p{pitch}-f{sample}' or p['variant']!=variant:raise ValueError('Wrong pane provenance')
            box=p['box']
            if len(box)!=4 or any(type(x) is not int for x in box) or not (0<=box[0]<box[2]<=640 and 0<=box[1]<box[3]<=360) or (box[2]-box[0],box[3]-box[1])!=(p['width'],p['height']):raise ValueError('Wrong crop dimensions')
            if digest(im.crop((p['x'],p['y'],p['x']+p['width'],p['y']+p['height'])))!=p['hash']:raise ValueError('Native pane changed')
            if not published_only and digest(Image.open(directory/groups[p['id']][variant]['File']).convert('RGBA').crop(box))!=p['hash']:raise ValueError('Native crop is not exact')
    for t in m['motions']:
        versions={v['variant']:v for v in t['tracks']};w=t['crop']['Width'];h=t['crop']['Height']
        for f,im in enumerate(paired_frames(directory,t)):
            for i,v in enumerate(('before','after')):
                if digest(im.crop((i*w,0,(i+1)*w,h)))!=versions[v]['hashes'][f]:raise ValueError('Decoded native frame changed')
    if not published_only:(directory/'manifest.json.gz').write_bytes(gzip.compress(json.dumps(m,separators=(',',':')).encode(),mtime=0))
    return m

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--source-root',type=Path);p.add_argument('--expected-revision');p.add_argument('--published-only',action='store_true');a=p.parse_args()
    verify(a.directory,a.source_root,a.expected_revision,a.published_only)
    print('PASS Shatter billboard: 624 static / 720 motion records; unchanged state, ordered identities, material and non-body packets; independently checked uploaded corners/weights. 12 static panes, 480 exact paired motion halves. '+('Full originals NOT checked locally.' if a.published_only else 'All original static/motion images verified.'))
