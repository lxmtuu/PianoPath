#!/usr/bin/env python3
"""Source/prototype inventory checks only; never starts the app or a renderer."""
import re
from pathlib import Path
from serve_settings_wireframe import ROOT, audit_tsv, balanced, inventory, source_text

def main():
    data = inventory(ROOT)
    assert data['prototypeOnly'] is True
    assert len(data['oldRoutes']) == 13
    assert len(data['routes']) == 12
    assert len(data['controls']) == len(data['defaults']) == 254
    assert len(data['occurrences']) == 242
    assert len({r['property'] for r in data['occurrences']}) == 236
    assert len(data['actions']) == 29
    assert len(data['references']) == 12
    assert len(data['xamlControls']) == 40
    assert len([r for r in data['controls'] if r['kind'] == 'Stored']) == 18
    routes = {r['id'] for r in data['routes']} | {'inventory'}
    byprop = {r['property']: r for r in data['controls']}
    for r in data['controls']:
        assert r['route'] in routes, r
        if r['kind'] == 'SliderRow':
            assert r['min'] is not None and r['max'] is not None, r
            assert r['min'] < r['max'], r
            assert r['min'] <= data['defaults'][r['property']] <= r['max'], r
        if r['scope'] == 'ar':
            assert r['property'].startswith('Ar')
    for old, targets in data['oldRoutes'].items():
        assert set(targets) <= routes
        proposed = {r['route'] for r in data['occurrences'] + data['actions'] + data['references'] if r['oldPage'] == old}
        assert proposed <= set(targets), (old, proposed - set(targets))
    # Each standard builder property actually written in the nine Build*Page bodies is represented.
    source = source_text(ROOT,'Ui/MainWindow.Settings.cs')
    expected = []
    for m in re.finditer(r'private void Build\w+Page\(\)\s*\{', source):
        body, _ = balanced(source, source.index('{', m.start()), '{', '}')
        for call in re.finditer(r'\b(?:Toggle|LayerToggle|SliderRow|Choice|ColorRow)\(', body):
            raw, _ = balanced(body, body.index('(', call.start()))
            expected += re.findall(r'nameof\(PianoVisualSettings\.(\w+)\)', raw)
    assert sorted(expected) == sorted(r['property'] for r in data['occurrences'])
    for prop in ['ImpactBurst', 'RingSize', 'NoteLandingGlow', 'ParticleAmount']:
        assert byprop[prop]['route'] == 'press', prop
    for prop in ['ShowWisps', 'ShowFlame', 'HoldBar', 'WispHeight', 'FlameHeight']:
        assert byprop[prop]['route'] == 'hold', prop
    assert byprop['ReleaseEffect']['route'] == 'release'
    assert byprop['ArKeyLight']['route'] == 'lighting'
    for handler in ['CalibrateAr_Click', 'ResetArCalibration_Click', 'ImportArCalibrationBackup_Click', 'ExportArCalibrationBackup_Click', 'MeasureArDelay_Click', 'ImportProfile_Click', 'ExportProfile_Click', 'SaveVisualSettings_Click', 'ResetPage_Click']:
        assert any(a['handler'] == handler for a in data['actions']), handler
    assert any(r['host'] == 'BuildCameraList' for r in data['references'])
    assert any(r['host'] == 'TrackPaletteRow' for r in data['references'])
    assert any(r['host'] == '_languageChipHost' for r in data['references'])
    for name in ['PresetList', 'ShareCodeBox', 'InputDeviceCombo', 'ModeCombo', 'HistoryChartHost', 'HistoryGhostHost', 'PracticeHistoryHost']:
        assert any(r['name'] == name for r in data['xamlControls']), name
    assert {'Language', 'CameraSourceLink', 'ArViewProfileId', 'ArDelay', 'GpuFrameRate', 'BackgroundImagePath'} <= set(data['machineLocalProperties'])
    assert (ROOT / 'docs/SETTINGS-LAYOUT-INVENTORY.tsv').read_text() == audit_tsv(data), 'Regenerate the TSV audit'
    print('PASS Settings source/prototype inventory: 13 old / 12 proposed routes, 242 row occurrences / 236 unique standard-row properties, 18 outside extraction, 29 actions, 12 custom hosts, 40 XAML references; all standard builder calls represented; mapping and generated TSV match. No app/native/render checks.')

if __name__ == '__main__':
    main()
