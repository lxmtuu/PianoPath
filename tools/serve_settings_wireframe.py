#!/usr/bin/env python3
"""Build a source-derived Settings inventory and a disconnected clickable P6 proposal.
No WPF routes, settings files, rendering, devices or input streams are modified.
"""
import argparse,hashlib,json,re,shutil,subprocess,tempfile,xml.etree.ElementTree as ET
from functools import partial
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
import make_presets
ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'tools/settings_wireframe'
APPROVED_SOURCE='3914aac'

def source_bytes(repo,path):
    return subprocess.check_output(['git','show',APPROVED_SOURCE+':'+path],cwd=repo)

def source_text(repo,path):
    return source_bytes(repo,path).decode('utf-8')
ROUTES=[('overview','Tổng quan','Overview'),('notes','Nốt & màu','Notes & color'),('press','Nhấn phím','Press'),('hold','Giữ phím','Hold'),('release','Thả phím','Release'),('lighting','Ánh sáng','Lighting'),('stage','Sân khấu & nền','Stage & background'),('camera','Camera & AR','Camera & AR'),('recording','Ghi hình','Recording'),('audio','Âm thanh & MIDI','Sound & MIDI'),('practice','Luyện tập & lịch sử','Practice & history'),('app','Ứng dụng','Application')]
OLD={'Style':['overview','notes','press','hold','lighting','stage'],'Theme':['app','overview'],'Notes':['notes','press','hold','release','lighting'],'Particles':['press','hold','overview'],'Keyboard':['stage','lighting'],'Background':['stage','lighting'],'Camera':['camera','lighting'],'Audio':['audio'],'MIDI':['audio'],'Practice':['practice'],'History':['practice'],'Recording':['recording'],'General':['app']}
HOSTS={'StyleSettingsHost':'Style','ThemeSettingsHost':'Theme','NoteSettingsHost':'Notes','ParticleSettingsHost':'Particles','KeyboardSettingsHost':'Keyboard','SceneSettingsHost':'Background','CameraSettingsHost':'Camera','RecordingSettingsHost':'Recording','GeneralSettingsHost':'General'}
BASIC=set('ShowNotes ColorMode Palette NoteStyle NoteWidth NoteFallSpeed NoteDirection NoteSatin ShowEmbers ImpactBurst ParticleAmount ParticleSize ParticleGlow ShowImpactRings ImpactWave ShowImpactFlash ImpactMorph UpgradedParticleEffects ShowWisps WispAmount WispHeight WispGlow ShowFlame FlameIntensity FlameHeight FlameColorMode ReleaseEffect ReleaseIntensity HoldBar HoldBreath ShowKeys KeyboardSize ShowBackground BackgroundMode BackgroundColor BackgroundDim BloomIntensity ShaderKeyLight ArKeyLight ArEnabled ArMode ArLivePressTrails ArNotesInPerspective ArViewProfileId ArOpacity ArDelay CameraMirror ShowCameraOverlay CameraSourceLink RecordingFormat RecordingResolution RecordingFrameRate RecordAudio Language ShellTheme ReducedMotion ReduceFlashes HighContrast GpuFrameRate GpuVSync'.split())
# Conservative UI-context proposal, not a renderer capability certification.
STAGE_ONLY=set('ShowKeys KeyboardStyle KeyboardScale KeyOverhang KeyLabels ShowKeyShadow ShowKeyFelt KeyFeltColor KeyPressDepth ShadingQuality ShaderCameraTilt ShaderKeyLight ShaderShadows ShaderAmbientOcclusion ShaderGloss ShowBackground BackgroundMode BackgroundColor BackgroundDim BackgroundImagePath BackgroundGradient BackgroundGuide CameraZoom CameraParallax'.split())

def strings(path):
    return {json.loads(k):json.loads(v) for k,v in re.findall(r'^\s*\[("(?:[^"\\]|\\.)*")\] = ("(?:[^"\\]|\\.)*"),',source_text(ROOT,str(path.relative_to(ROOT))),re.M)}

def balanced(text,start,opening='(',closing=')'):
    depth=0;quoted=False;escape=False
    for i in range(start,len(text)):
        c=text[i]
        if quoted:
            if escape:escape=False
            elif c=='\\':escape=True
            elif c=='"':quoted=False
        elif c=='"':quoted=True
        elif c==opening:depth+=1
        elif c==closing:
            depth-=1
            if depth==0:return text[start+1:i],i+1
    raise ValueError('Unclosed source expression')

def args(text):
    out=[];start=0;depth=0;quoted=False;escape=False
    for i,c in enumerate(text):
        if quoted:
            if escape:escape=False
            elif c=='\\':escape=True
            elif c=='"':quoted=False
        elif c=='"':quoted=True
        elif c in '([{':depth+=1
        elif c in ')]}':depth-=1
        elif c==',' and depth==0:out.append(text[start:i].strip());start=i+1
    return out+[text[start:].strip()]

def literal(value):
    try:return json.loads(value)
    except (ValueError,TypeError):return None

def destination(prop,page,card):
    if page in ('Audio','MIDI'):return 'audio'
    if page in ('Practice','History'):return 'practice'
    if page in ('General','Theme'):return 'app'
    if page=='Recording':return 'recording'
    if prop=='ArKeyLight' or prop=='ShowLightBeams':return 'lighting'
    if prop=='ShowNotes':return 'notes'
    if prop=='ShowBackground':return 'stage'
    if card=='IMPACT · WAVE & FLASH':return 'press'
    if card=='SMART MODULATORS':return 'lighting'
    if prop.startswith(('Ar','Camera','HandTracking','ShowCamera','ShowHand')):return 'camera'
    if prop=='UpgradedParticleEffects':return 'overview'
    if prop.startswith('Release'):return 'release'
    if prop.startswith(('Hold','Wisp','Flame')) or prop in ('ShowWisps','ShowFlame'):return 'hold'
    if prop.startswith(('Impact','Particle','Emitter','Gravity','Drag','VectorField','FieldScale','Evolution','Physics','Spiral','ZoneSplit')) or prop in ('ShowEmbers','ShowImpactRings','ShowImpactFlash'):return 'press'
    if card in ('POST FX','KEY LIGHTING','RAY-TRACED SHADING','HIT LINE','SMART MODULATORS') or prop.startswith(('Bloom','Shader','Halo')):return 'lighting'
    if page in ('Keyboard','Background'):return 'stage'
    if page=='Camera':return 'camera'
    if page=='Style':return 'overview'
    return 'notes'

def inventory(repo):
    source=source_text(repo,'Ui/MainWindow.Settings.cs');vi=strings(repo/'Localization/Strings.Vietnamese.cs')
    # Generator reads declarations, not guesses at defaults from a screenshot.
    names=['SETTINGS','PROFILE_CATALOGUE','AR_CATALOGUE','SHELL_THEMES']
    previous={name:getattr(make_presets,name) for name in names}
    with tempfile.TemporaryDirectory(prefix='settings-approved-') as temp:
        try:
            for name,path in previous.items():
                target=Path(temp)/path.relative_to(ROOT);target.parent.mkdir(parents=True,exist_ok=True)
                target.write_bytes(source_bytes(repo,str(path.relative_to(ROOT))));setattr(make_presets,name,target)
            defaults=dict(make_presets.read_defaults())
        finally:
            for name,path in previous.items():setattr(make_presets,name,path)
    rows=[];actions=[];references=[];cards={}
    for method in re.finditer(r'private void Build(\w+)Page\(\)\s*\{',source):
        body,end=balanced(source,source.index('{',method.start()),'{','}');offset=source.index('{',method.start())+1;panel={}
        for call in re.finditer(r'\b(Card|Toggle|LayerToggle|SliderRow|Choice|ColorRow|ButtonRow|Register|BuildCameraList|TrackPaletteRow)\(',body):
            raw,after=balanced(body,body.index('(',call.start()));parts=args(raw);kind=call[1];line=source.count('\n',0,offset+call.start())+1
            if kind=='Card':
                assign=re.search(r'var (\w+)\s*=\s*$',body[max(0,call.start()-100):call.start()])
                if assign and parts[0] in HOSTS:
                    panel[assign[1]]=(HOSTS[parts[0]],literal(parts[1]) or parts[1]);cards[f'{HOSTS[parts[0]]}/{literal(parts[1])}']=line
                continue
            if not parts or parts[0] not in panel:continue
            page,card=panel[parts[0]]
            if kind=='ButtonRow':
                for part in parts[1:]:
                    item=args(part[1:-1]) if part.startswith('(') and part.endswith(')') else []
                    if len(item)<2:continue
                    label=literal(item[0])
                    if label is None:label=' / '.join(literal(v) for v in re.findall(r'"(?:[^"\\]|\\.)*"',item[0])) or item[0]
                    actions.append(dict(label=label,labelVi=vi.get(label,label),handler=item[1],oldPage=page,oldCard=card,source='Ui/MainWindow.Settings.cs',line=line,route=destination('',page,card)))
                continue
            if kind in ('Register','BuildCameraList','TrackPaletteRow'):
                label=(literal(parts[-1]) or parts[1]) if kind=='Register' else {'BuildCameraList':'Camera device list','TrackPaletteRow':'Per-track palette'}[kind]
                references.append(dict(label=label,labelVi=vi.get(label,label),property=next(iter(re.findall(r'nameof\(PianoVisualSettings\.(\w+)\)',raw)),None),host=parts[1] if kind=='Register' else kind,oldPage=page,oldCard=card,source='Ui/MainWindow.Settings.cs',line=line,route=destination('',page,card)))
                continue
            if len(parts)<4:continue
            prop=re.fullmatch(r'nameof\(PianoVisualSettings\.(\w+)\)',parts[2])
            if not prop or prop[1] not in defaults:continue
            prop=prop[1];label=literal(parts[1]);tip=literal(parts[5] if kind=='SliderRow' else parts[3])
            tail=body[after:body.find(';',after)+1];condition=re.search(r'\.VisibleWhen\s*=\s*\(\)\s*=>\s*(.*);',tail,re.S)
            row=dict(property=prop,kind=kind,label=label or prop,labelVi=vi.get(label,label) or prop,tooltip=tip or '',tooltipVi=vi.get(tip,tip) or '',oldPage=page,oldCard=card,source='Ui/MainWindow.Settings.cs',line=line,route=destination(prop,page,card),advanced=prop not in BASIC,scope='ar' if prop.startswith('Ar') else 'stage' if prop in STAGE_ONLY else 'shared',visibleWhen=condition[1].strip() if condition else None)
            if kind=='SliderRow':
                def number(value):
                    try:return float(value)
                    except ValueError:
                        owner,name=value.split('.',1)
                        candidates=list(repo.glob('**/'+owner+'.cs'))
                        if not candidates:return None
                        m=re.search(r'\b'+re.escape(name)+r'\s*=\s*(-?[0-9.]+)',source_text(repo,str(candidates[0].relative_to(repo))))
                        return float(m[1]) if m else None
                row.update(min=number(parts[3]),max=number(parts[4]),rangeSource=parts[3:5])
            if kind=='Choice':row['options']=[dict(value=literal(a),label=vi.get(literal(b),literal(b)),labelEn=literal(b)) for a,b in re.findall(r'\(("(?:[^"\\]|\\.)*"),\s*("(?:[^"\\]|\\.)*")\)',raw)]
            rows.append(row)
    # Retain all static actions/controls, including business controls not backed by visual settings.
    xn='{http://schemas.microsoft.com/winfx/2006/xaml}Name'
    xaml=ET.fromstring(source_text(repo,'Ui/MainWindow.xaml'));tabs=next(e for e in xaml.iter() if e.get(xn)=='SettingsTabs');oldpages=[];xcontrols=[]
    for tab in tabs:
        page=tab.get('Header');page='Camera' if page=='Camera & FX' else page
        if not page:continue
        oldpages.append(page)
        for e in tab.iter():
            tag=e.tag.rsplit('}',1)[-1];events={k:v for k,v in e.attrib.items() if k in ('Click','Checked','Unchecked','SelectionChanged','ValueChanged','TextChanged','MouseDoubleClick')}
            if tag not in ('Button','ToggleButton','CheckBox','ComboBox','Slider','TextBox','ListBox','Canvas') and not (e.get(xn) or '').endswith('Host'):continue
            if e.get(xn) in HOSTS:continue
            label=e.get('Content') or e.get('AutomationProperties.Name') or e.get('ToolTip') or e.get(xn) or next(iter(events.values()),tag)
            xcontrols.append(dict(name=e.get(xn),kind=tag,label=label,labelVi=vi.get(label,label),events=events,enabled=e.get('IsEnabled')!='False',options=[c.get('Content') for c in e if c.tag.rsplit('}',1)[-1]=='ComboBoxItem'],minimum=e.get('Minimum'),maximum=e.get('Maximum'),initialValue=e.get('Value'),oldPage=page,route=OLD[page][0],source='Ui/MainWindow.xaml'))
    if oldpages!=list(OLD):raise ValueError('Review mapping must account for the actual 13-page order')
    for e in xaml.iter():
        if e.get('Click') in ('ResetPage_Click','SaveVisualSettings_Click'):
            label=e.get('Content') or e.get('ToolTip')
            actions.append(dict(label=label,labelVi=vi.get(label,label),handler=e.get('Click'),oldPage='Settings shell',oldCard='Footer',source='Ui/MainWindow.xaml',route='overview'))
    byprop={}
    for row in rows:
        # Prefer the detailed owner over Style's duplicate quick switches.
        if row['property'] not in byprop or byprop[row['property']]['oldPage']=='Style':byprop[row['property']]=row
    for prop,value in defaults.items():
        if prop not in byprop:byprop[prop]=dict(property=prop,kind='Stored',label=prop,labelVi=prop,tooltip='',tooltipVi='',oldPage=None,oldCard=None,source='Stage/PianoVisualSettings.cs',line=None,route='inventory',advanced=True,scope='unclassified',visibleWhen=None)
    copybody,_=balanced(source_text(repo,'Stage/PianoVisualSettings.cs'),source_text(repo,'Stage/PianoVisualSettings.cs').index('{',source_text(repo,'Stage/PianoVisualSettings.cs').index('internal void CopyFrom')),'{','}')
    machine=sorted(set(re.findall(r'nameof\((\w+)\)',copybody))|{'BackgroundImagePath'})
    return dict(schema=1,prototypeOnly=True,sourceHashes={p:hashlib.sha256(source_bytes(repo,p)).hexdigest() for p in ('Ui/MainWindow.Settings.cs','Ui/SettingsPages.cs','Ui/MainWindow.xaml','Stage/PianoVisualSettings.cs','Localization/Strings.Vietnamese.cs','Camera/ArOptions.cs')},routes=[dict(id=a,vi=b,en=c) for a,b,c in ROUTES],oldRoutes=OLD,defaults=defaults,machineLocalProperties=machine,controls=list(byprop.values()),occurrences=rows,actions=actions,references=references,xamlControls=xcontrols,cards=cards,extractionLimits='Generated builder calls with literal labels + nameof settings, static XAML controls/hosts. Custom Register hosts, TrackPaletteRow and BuildCameraList are retained as references; their children, runtime device rows and action handlers are not proven exhaustively migrated. Stored-only properties are not newly exposed as working controls.')

def build(repo,output):
    data=inventory(repo);output.mkdir(parents=True,exist_ok=True)
    for p in ASSETS.iterdir():
        if p.is_file():shutil.copyfile(p,output/p.name)
    (output/'inventory.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    presets={p.stem:json.loads(p.read_text())['Settings'] for p in (repo/'presets').glob('*.json')}
    (output/'presets.json').write_text(json.dumps(presets,ensure_ascii=False))
    print(f"Built Settings proposal: {len(data['oldRoutes'])} old routes, {len(data['controls'])} properties, {len(data['occurrences'])} extracted row occurrences, {len(data['xamlControls'])} XAML references. No app/settings/device changes.",flush=True)
    return data

def audit_tsv(data):
    """Complete extracted occurrences, not just the de-duplicated browser controls."""
    columns=['type','identifier','old_page','old_card','new_route','level','scope','source','detail']
    lines=['# Generated by tools/serve_settings_wireframe.py --audit-output; proposal, not migration certification.']
    lines.extend('# SHA256 '+path+' '+digest for path,digest in data['sourceHashes'].items())
    lines.append('\t'.join(columns))
    records=[('row',r) for r in data['occurrences']]+[('model-only-extraction',r) for r in data['controls'] if r['kind']=='Stored']+[(kind,r) for kind,key in [('action','actions'),('custom-host','references'),('xaml-reference','xamlControls')] for r in data[key]]
    for kind,r in records:
        detail=dict(label=r['label'])
        for key in ('visibleWhen','handler','events','options','enabled','rangeSource','minimum','maximum'):
            if key in r:detail[key]=r[key]
        cells=[kind,r.get('property') or r.get('name') or r.get('host') or r.get('handler') or r['label'],r.get('oldPage') or '',r.get('oldCard') or '',r['route'],'advanced' if r.get('advanced') else 'basic' if 'advanced' in r else 'reference',r.get('scope') or 'reference',r['source']+(':'+str(r['line']) if r.get('line') else ''),json.dumps(detail,ensure_ascii=False,separators=(',',':'))]
        lines.append('\t'.join(str(c).replace('\t',' ').replace('\n',' ') for c in cells))
    return '\n'.join(lines)+'\n'

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,default=ROOT);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=8785);p.add_argument('--build-only',action='store_true');p.add_argument('--audit-output',type=Path);a=p.parse_args();data=build(a.repo.resolve(),a.output.resolve())
    if a.audit_output:a.audit_output.write_text(audit_tsv(data))
    if not a.build_only:ThreadingHTTPServer(('0.0.0.0',a.port),partial(SimpleHTTPRequestHandler,directory=str(a.output.resolve()))).serve_forever()
if __name__=='__main__':main()
