#!/usr/bin/env python3
"""P6 migration source guards, not a WPF/runtime or render test."""
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from serve_settings_wireframe import ROOT, APPROVED_SOURCE, balanced, source_text

def method(text, name):
    m = re.search(r'\b(?:void|Panel\??|SettingRow)\s+' + name + r'\([^;]*?\)\s*\{', text, re.S)
    assert m, name
    return balanced(text, text.index('{', m.start()), '{', '}')[0]

def main():
    old = source_text(ROOT, 'Ui/MainWindow.Settings.cs')
    new = (ROOT / 'Ui/MainWindow.Settings.cs').read_text()
    # Every actual page builder still declares the same controls/actions/dependencies.
    builders = re.findall(r'private void (Build\w+Page)\(\)', old)
    assert len(builders) == 9
    for name in builders:
        assert method(old, name) == method(new, name), f'Page builder changed: {name}'
    for name in ['VisualToggle_Changed', 'VisualSlider_ValueChanged', 'VisualChoice_Changed', 'CalibrateAr', 'ResetArCalibration_Click', 'ApplyPreset', 'CreateTheme_Click', 'ImportProfile_Click', 'ExportProfile_Click']:
        assert method(old, name) == method(new, name), f'Business/settings handler changed: {name}'
    before = ET.fromstring(source_text(ROOT, 'Ui/MainWindow.xaml'))
    after = ET.parse(ROOT / 'Ui/MainWindow.xaml').getroot()
    xn = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
    names = {e.get(xn):e for e in after.iter() if e.get(xn)}
    events = ('Click','Checked','Unchecked','SelectionChanged','ValueChanged','TextChanged','MouseDoubleClick','KeyDown','LostFocus')
    old_names = {e.get(xn) for e in before.iter() if e.get(xn)}
    assert old_names <= names.keys(), old_names - names.keys()
    for element in before.iter():
        if element.get(xn) not in names:continue
        other = names[element.get(xn)]
        for event in events:
            assert element.get(event) == other.get(event), (element.get(xn), event)
    # Unnamed business actions retain their event/caption/tooltip tuples too.
    def action_set(root):
        return {(e.tag, tuple((k,e.get(k)) for k in events),e.get('Content'),e.get('ToolTip')) for e in root.iter() if any(e.get(k) for k in events) and not e.get(xn)}
    assert action_set(before) <= action_set(after)
    tabs = names['SettingsTabs']
    headers = [t.get('Header') for t in tabs]
    assert headers == ['Overview','Notes & color','Press','Hold','Release','Lighting','Stage & background','Camera & AR','Recording','Sound & MIDI','Practice & history','Application']
    assert all(next(iter(t)).tag.endswith('ScrollViewer') for t in tabs)
    catalogue = (ROOT / 'Ui/SettingsPages.cs').read_text()
    assert 'Array.IndexOf(Order, Canonical(name))' in catalogue
    for alias in ['Style => Overview','Theme or General => Application','Particles => Press','Keyboard or Background => StageDesign','Audio or Midi => Sound','Practice or History => Session','"Notes" => Notes','"Camera" or "Camera & FX" => Camera']:
        assert alias in catalogue, alias
    layout = (ROOT / 'Ui/MainWindow.SettingsLayout.cs').read_text()
    for call in ['ApplyVisualSettings(', 'MarkModified(', 'CopyFrom(', 'SyncCameraOverlay(']:
        assert call not in re.sub(r'//[^\n]*', '', method(layout, 'SetSettingsContext')), call
    assert 'source.CopyFrom(BasePresetSettings(), keepBackgroundImage: true)' in method(new, 'ResetPage_Click')
    assert 'SettingsPhraseMatches(phrase, token)' in new
    assert 'nameof(RecordingSettingsHost) => SettingsPages.Recording' in new
    assert 'StringsVietnamese.Table.TryGetValue' in layout
    assert 'NormalizeSettingsSearch' in layout
    assert 'ArrangeApprovedSettingsLayout();' in new
    assert 'foreach (var duplicates' in layout and '_settingRows.Remove(alias)' in layout
    assert '_settingsExpansionBeforeSearch' in layout
    assert 'advanced.IsExpanded = true' in new
    assert 'RegisterBusinessCard(AudioBusinessCard' in layout
    assert 'RegisterBusinessCard(MidiBusinessCard' in layout
    assert 'RegisterBusinessCard(PracticeBusinessCard' in layout
    assert 'RegisterBusinessCard(HistoryBusinessCard' in layout
    changed = subprocess.check_output(['git','diff',APPROVED_SOURCE,'--name-only'],cwd=ROOT,text=True).splitlines()
    protected = ('Gpu/','Stage/','Camera/','Audio/','Video/')
    assert not [p for p in changed if p.startswith(protected)], 'Renderer/model/media scope changed'
    print('PASS P6 migration source guards: 9 builders + selected business handlers unchanged; all original XAML names/events and unnamed actions retained; 12 real pages, legacy routes, shared-element reparent/dedup, bilingual search, Advanced/deep links, CopyFrom reset boundary; no renderer/model/media edits. Not WPF/native/runtime acceptance.')

if __name__ == '__main__':
    main()
