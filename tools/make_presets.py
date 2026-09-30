#!/usr/bin/env python3
"""Writes the community preset shelf: ``presets/*.json``.

A shelf preset is a normal preset file — the same envelope the app writes, the same settings keys —
so a contributor can copy one, change the values, drop it in ``presets/`` and open a pull request.
The files are generated from the defaults that ``Stage/PianoVisualSettings.cs`` declares, which keeps
them complete: every property is present, and ``tools/check_sources.py`` fails when a committed file
no longer matches what this script writes (a new setting was added, or somebody edited a file by hand).

Run from the repository root:

    python3 tools/make_presets.py

Every value below is a documented control, and the settings are written in declaration order, exactly
like ``PianoVisualSettings.ToJson()`` writes them, so the files read the same as the app's own output.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SETTINGS = ROOT / "Stage" / "PianoVisualSettings.cs"
SHELL_THEMES = ROOT / "Theme" / "ShellTheme.cs"
TARGET = ROOT / "presets"

# ---------------------------------------------------------------------------------------------------
# The shelf. Each entry is a full look: colours, note style, the impact graph, the keyboard and the
# atmosphere. Keep the name free of characters a file name cannot hold, and describe what the eye sees.
# ---------------------------------------------------------------------------------------------------
PRESETS: list[dict] = [
    {
        "name": "Ember Rain",
        "description": "Warm sparks falling through rain over a dark stage: ember notes, melting impacts and water ripples on every hit.",
        "settings": {
            "NoteStyle": "Fire", "ColorMode": "Gradient", "Palette": "Fire",
            "NoteColorStart": "#FF6A00", "NoteColorEnd": "#FFD166", "HaloColor": "#FF4A1C", "HaloIntensity": 120,
            "NoteTexture": 70, "NoteTint": 85, "NoteGlow": 120, "NoteEdge": 100, "NoteEdgeWidth": 40,
            "NoteRoundness": 40, "NoteHeadGlow": 60, "NoteFallSpeed": 520,
            "FallingTrail": "Sparkles", "FallingTrailIntensity": 70, "FallingTrailLength": 60,
            "ImpactBurst": "Splash", "ImpactWave": "Ripple", "ImpactWaveIntensity": 110,
            "ImpactMorph": "Melt", "ImpactMorphIntensity": 65,
            "ShowImpactFlash": True, "ImpactFlashStyle": "Flash", "ImpactFlashIntensity": 70,
            "HoldBar": True, "HoldBarIntensity": 65, "ReleaseEffect": "Smoke", "ReleaseIntensity": 65,
            "AmbientNature": "Rain", "AmbientNatureAmount": 60, "AmbientNatureSpeed": 50,
            "AmbientLight": "Color Splash", "AmbientLightAmount": 30, "AmbientLightSpeed": 45, "AmbientLightColor": "#FF8A3D",
            "ShowFlame": True, "FlameIntensity": 70, "FlameHeight": 55, "FlameColorMode": "Warm",
            "ShowImpactRings": True, "RingSize": 55, "ShowWisps": True, "WispAmount": 40,
            "WispHeight": 60, "WispGlow": 85,
            "ParticleAmount": 30, "ParticleVelocity": 240, "ParticleSpread": 80, "Gravity": 380,
            "ParticleLife": 0.9, "ParticleSize": 2.2, "ParticleGlow": 90,
            "KeyboardStyle": "Studio", "PressedKeyColorMode": "Note", "KeyGlowRadius": 80, "KeyLighting": 50,
            "ShowKeyFelt": True, "KeyFeltColor": "#FF2E3A",
            "BloomIntensity": 100, "BloomSize": 85, "Vignette": 40, "HorizonGlow": 70, "Saturation": 112,
            "ShadingQuality": "Cinematic", "ShaderCameraTilt": 40, "ShaderKeyLight": 75, "ShaderGloss": 60,
            "ShaderShadows": 85, "ShaderEmissive": 115, "ShaderRimLight": 80,
            "VelocityColor": True, "VelocityColorAmount": 55,
            "ShellTheme": "velvet-gold",
        },
    },
    {
        "name": "Lo-Fi Study",
        "description": "Quiet pastel bars, dust instead of sparks and almost no glow: the cheapest look, made for long study recordings.",
        "settings": {
            "NoteStyle": "Solid", "ColorMode": "Gradient", "Palette": "Ocean",
            "NoteColorStart": "#8FB8C9", "NoteColorEnd": "#C9A6D8", "HaloColor": "#9AB6C4", "HaloIntensity": 40,
            "NoteTexture": 20, "NoteTint": 60, "NoteGlow": 45, "NoteEdge": 30, "NoteEdgeWidth": 20,
            "NoteRoundness": 70, "NoteHeadGlow": 20, "NoteFallSpeed": 480, "Notes3D": False,
            "FallingTrail": "None",
            "ImpactBurst": "Dust", "ImpactWave": "Ring", "ImpactWaveIntensity": 50,
            "ImpactMorph": "None", "ShowImpactFlash": False, "ReleaseEffect": "Fade", "ReleaseIntensity": 40,
            "ShowFlame": False, "ShowImpactRings": True, "RingSize": 30, "ShowWisps": False,
            "ShowEmbers": True, "ParticleAmount": 12, "ParticleVelocity": 90, "ParticleSpread": 45,
            "Gravity": 40, "Drag": 45, "ParticleLife": 1.1, "ParticleSize": 1.4, "ParticleGlow": 40,
            "AmbientEnergy": "None", "AmbientNature": "None", "AmbientLight": "None", "AmbientCosmic": "None",
            "ShowPetals": False, "ShowStars": False,
            "KeyboardStyle": "Classic", "PressedKeyColorMode": "Note", "KeyGlowRadius": 25, "ShowKeyFelt": False,
            "BloomIntensity": 30, "BloomSize": 40, "Vignette": 25, "HorizonGlow": 15, "Saturation": 90,
            "ShadingQuality": "Fast", "ShaderCameraTilt": 60, "ShaderGloss": 40, "ShaderShadows": 45,
            "ShaderEmissive": 55,
            "ChromeMotion": "Calm", "BackdropDensity": 40,
            "ShellTheme": "concert-noir",
        },
    },
    {
        "name": "Sunset Drive",
        "description": "Synthwave neon in magenta and amber: speed-line trails, shattering hits, echo rings and laser beams over a purple horizon.",
        "settings": {
            "NoteStyle": "Neon", "ColorMode": "Gradient", "Palette": "Custom",
            "NoteColorStart": "#FF3D7F", "NoteColorEnd": "#FFB86B", "HaloColor": "#FF7A59", "HaloIntensity": 110,
            "NoteTexture": 35, "NoteTint": 40, "NoteGlow": 115, "NoteEdge": 120, "NoteEdgeWidth": 55,
            "NoteRoundness": 55, "NoteHeadGlow": 50, "NoteFallSpeed": 620,
            "FallingTrail": "Speed Lines", "FallingTrailIntensity": 80, "FallingTrailLength": 65,
            "FallingGhost": True, "FallingGhostAmount": 35,
            "ImpactBurst": "Fireworks", "ImpactWave": "Shockwave", "ImpactWaveIntensity": 130,
            "ImpactMorph": "Shatter", "ImpactMorphIntensity": 70,
            "ShowImpactFlash": True, "ImpactFlashStyle": "Flash", "ImpactFlashIntensity": 85,
            "HoldBar": True, "HoldBarIntensity": 70, "HoldVibration": True, "HoldVibrationAmount": 30,
            "ReleaseEffect": "Echo Rings", "ReleaseIntensity": 65,
            "AmbientEnergy": "Laser Beams", "AmbientEnergyAmount": 40, "AmbientEnergySpeed": 60,
            "AmbientLight": "Gradient Wave", "AmbientLightAmount": 40, "AmbientLightSpeed": 50, "AmbientLightColor": "#FF3D7F",
            "ShowFlame": False, "ShowImpactRings": True, "RingSize": 65, "ShowWisps": False,
            "ShowLightBeams": True, "BeamIntensity": 60,
            "ParticleAmount": 34, "ParticleVelocity": 250, "ParticleSpread": 72, "Gravity": 300,
            "ParticleLife": 0.8, "ParticleSize": 1.9, "ParticleGlow": 110,
            "BackgroundGradient": True, "BackgroundMode": "Solid", "BackgroundColor": "#1B0B2E",
            "ShowStars": True, "StarDensity": 70, "HorizonGlow": 65,
            "KeyboardStyle": "Glass", "PressedKeyColorMode": "Note", "KeyGlowRadius": 75,
            "BloomIntensity": 95, "BloomSize": 80, "Vignette": 42, "Saturation": 118, "Contrast": 108,
            "ShadingQuality": "Cinematic", "ShaderCameraTilt": 52, "ShaderGloss": 85, "ShaderShadows": 60,
            "ShaderEmissive": 90, "ShaderRimLight": 70,
            "VelocityColor": True, "VelocityColorAmount": 65,
            "ShellTheme": "velvet-gold",
        },
    },
]


def read_consts() -> dict[str, str]:
    """``internal const string VelvetGoldId = "velvet-gold";`` → ``{"ShellThemes.VelvetGoldId": "velvet-gold"}``.

    A const may name another const (``DefaultId = ConcertGrandId``), so resolve those too.
    """
    text = SHELL_THEMES.read_text(encoding="utf-8")
    raw = {name: value.strip() for name, value in re.findall(r"internal const string (\w+) = ([^;]+);", text)}
    consts: dict[str, str] = {}

    def resolve(name: str, seen: frozenset[str]) -> str | None:
        if name in seen or name not in raw:
            return None
        value = raw[name]
        literal = re.fullmatch(r'"([^"]*)"', value)
        if literal:
            return literal.group(1)
        return resolve(value, seen | {name})

    for name in raw:
        value = resolve(name, frozenset())
        if value is not None:
            consts[f"ShellThemes.{name}"] = value
    return consts


def read_defaults() -> list[tuple[str, object]]:
    """The properties of the settings class in declaration order, with the default each one declares."""
    text = SETTINGS.read_text(encoding="utf-8")
    consts = read_consts()
    properties: list[tuple[str, object]] = []
    for match in re.finditer(r"^    public ([\w<>\[\]]+) (\w+) \{ get; set; \}(?: = (.+?);)?$", text, re.M):
        kind, name, initializer = match.group(1), match.group(2), match.group(3)
        if initializer is None:
            value: object = False if kind == "bool" else 0 if kind in ("int", "double") else ""
        else:
            initializer = initializer.strip()
            if kind == "bool":
                value = initializer == "true"
            elif kind in ("int", "double"):
                value = float(initializer) if kind == "double" else int(initializer)
            elif kind.startswith("List<"):
                value = json.loads(initializer)
            else:
                literal = re.fullmatch(r'"([^"]*)"', initializer)
                if literal:
                    value = literal.group(1)
                elif initializer in consts:
                    value = consts[initializer]
                else:
                    raise SystemExit(f"{name}: cannot read the default {initializer!r} — teach this script about it")
        properties.append((name, value))
    if len(properties) < 100:
        raise SystemExit(f"only {len(properties)} settings found in {SETTINGS.name} — the class moved?")
    return properties


def build(preset: dict, defaults: list[tuple[str, object]]) -> dict:
    settings = dict(defaults)
    for key, value in preset["settings"].items():
        if key not in settings:
            raise SystemExit(f"{preset['name']}: {key} is not a setting of PianoVisualSettings")
        settings[key] = value
    # A shipped look is a fresh start: it is named after its file, it is not a modified built-in, and it
    # claims the current background appearance so loading it runs no migration.
    settings["PresetName"] = preset["name"]
    settings["PresetModified"] = False
    settings["BackgroundAppearanceVersion"] = 2
    settings["Language"] = ""
    return {"Version": 1, "Thumbnail": "", "Description": preset["description"], "Settings": settings}


def write_all(target: Path, quiet: bool = False) -> list[Path]:
    defaults = read_defaults()
    target.mkdir(parents=True, exist_ok=True)
    written = []
    for preset in PRESETS:
        path = target / f"{preset['name']}.json"
        payload = json.dumps(build(preset, defaults), indent=2, ensure_ascii=False) + "\n"
        path.write_text(payload, encoding="utf-8")
        written.append(path)
        if not quiet:
            print(f"wrote {path.relative_to(ROOT)} ({len(payload)} bytes)")
    return written


if __name__ == "__main__":
    if not SETTINGS.exists():
        sys.exit(f"run this from the repository root: {SETTINGS} is missing")
    write_all(TARGET)
