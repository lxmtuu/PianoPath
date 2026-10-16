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
# A settings default may name a constant of another file: which view of the camera the AR overlay uses is
# ArCalibrationProfiles.LegacyId, declared beside the catalogue it comes from.
PROFILE_CATALOGUE = ROOT / "Camera" / "ArCalibrationProfile.cs"
AR_CATALOGUE = ROOT / "Camera" / "ArElementCatalog.cs"
SHELL_THEMES = ROOT / "Theme" / "ShellTheme.cs"
TARGET = ROOT / "presets"

# ---------------------------------------------------------------------------------------------------
# The shelf. Each entry is a full look: colours, note style, the impact graph, the keyboard and the
# atmosphere. Keep the name free of characters a file name cannot hold, and describe what the eye sees.
# ---------------------------------------------------------------------------------------------------
PRESETS: list[dict] = [
    {
        "name": "Prism Flow Preview",
        "description": "Experimental cyan-to-coral satin bars with quiet lighting. Palette/material preview; flow particles are not implemented yet.",
        "settings": {
            "Palette": "Prism Flow", "PaletteLowPitch": 36, "PaletteHighPitch": 84,
            "NoteStyle": "Solid", "NoteSatin": True, "NoteTint": 92, "NoteTexture": 32,
            "NoteEdge": 42, "NoteEdgeWidth": 16, "NoteRefraction": 28, "NoteRoundness": 26,
            "NoteGlow": 30, "NoteHeadGlow": 12, "NoteMinLength": 10, "NoteWidth": 84,
            "BackgroundColor": "#030509", "BackgroundDim": 0, "BackgroundMotion": "None",
            "HaloColor": "#00CFE8", "HaloIntensity": 16, "HaloPulse": False, "HorizonGlow": 4,
            "BloomIntensity": 22, "BloomSize": 32, "Vignette": 12,
            "KeyLighting": 20, "KeyGlowRadius": 22, "KeyLabels": "C", "ShowKeyFelt": False,
            "ShaderKeyLight": 70, "ShaderGloss": 28, "ShaderEmissive": 38, "ShaderExposure": 100,
            "ShaderRimLight": 22, "ShaderCameraTilt": 38, "ShaderShadows": 58, "ShaderAmbientOcclusion": 44,
            "VelocityColor": False, "OctaveColor": False, "ZoneSplit": False, "HoldColorCycle": False,
            "TempoSync": False, "AudioReactive": False, "PedalGlow": False,
            "NoteLandingGlow": False, "NoteShimmer": False, "HoldBar": False,
            "ShowEmbers": False, "ShowWisps": False, "ShowFlame": False,
            "AmbientEnergy": "None", "AmbientNature": "None", "AmbientLight": "None", "AmbientCosmic": "None",
            "ShowStars": False, "ShowPetals": False, "ShootingStars": False,
        },
    },
    {
        "name": "Ember Rain",
        "description": "A restrained dual-element look: molten notes, sparse rain and small water-ripple impacts.",
        "settings": {
            "NoteStyle": "Fire", "ColorMode": "Gradient", "Palette": "Custom",
            "NoteColorStart": "#E8724D", "NoteColorEnd": "#F1BE75", "HaloColor": "#EAA16B", "HaloIntensity": 45,
            "BackgroundMotion": "Ember Haze", "BackgroundMotionAmount": 10, "BackgroundMotionSpeed": 15, "BackgroundMotionColor": "#AA563F",
            "HaloPulse": False, "NoteTexture": 28, "NoteTint": 72, "NoteGlow": 48, "NoteEdge": 72, "NoteEdgeWidth": 28,
            "NoteRoundness": 42, "NoteHeadGlow": 22, "NoteFallSpeed": 540,
            "FallingTrail": "Glow", "FallingTrailIntensity": 20, "FallingTrailLength": 24,
            "ImpactBurst": "Splash", "ImpactWave": "Ripple", "ImpactWaveIntensity": 34,
            "ImpactMorph": "Melt", "ImpactMorphIntensity": 18,
            "ShowImpactFlash": False, "ShowImpactRings": True, "RingSize": 34,
            "HoldBar": True, "HoldBarIntensity": 20, "ReleaseEffect": "Smoke", "ReleaseIntensity": 20,
            "AmbientNature": "Rain", "AmbientNatureAmount": 10, "AmbientNatureSpeed": 18,
            "AmbientEnergy": "None", "AmbientLight": "None", "AmbientCosmic": "None",
            "ShowFlame": True, "FlameIntensity": 18, "FlameHeight": 28, "FlameColorMode": "Warm",
            "ShowEmbers": True, "ShowWisps": False, "ShowLightBeams": False, "ShowPetals": False,
            "ParticleAmount": 6, "ParticleVelocity": 110, "ParticleSpread": 32, "Gravity": 80, "Drag": 42,
            "ParticleLife": 0.55, "ParticleSize": 1.0, "ParticleGlow": 35,
            "KeyboardStyle": "Studio", "PressedKeyColorMode": "Note", "KeyGlowRadius": 30, "KeyLighting": 25,
            "ShowKeyFelt": True, "KeyFeltColor": "#553A31",
            "BloomIntensity": 36, "BloomSize": 45, "Vignette": 25, "HorizonGlow": 12, "Saturation": 100,
            "ShadingQuality": "Balanced", "ShaderCameraTilt": 38, "ShaderKeyLight": 75, "ShaderGloss": 58,
            "ShaderShadows": 62, "ShaderAmbientOcclusion": 48, "ShaderEmissive": 58,
            "ShellTheme": "velvet-gold", "ChromeMotion": "Calm", "BackdropDensity": 18,
        },
    },
    {
        "name": "Lo-Fi Study",
        "description": "Muted, opaque study bars, almost no glow and only a barely moving aurora for long practice sessions.",
        "settings": {
            "NoteStyle": "Solid", "ColorMode": "Gradient", "Palette": "Custom",
            "NoteColorStart": "#91AFB9", "NoteColorEnd": "#C6A6BF", "HaloColor": "#8FA5AE", "HaloIntensity": 24,
            "BackgroundMotion": "Aurora", "BackgroundMotionAmount": 6, "BackgroundMotionSpeed": 10, "BackgroundMotionColor": "#657E89",
            "HaloPulse": False, "NoteLandingGlow": False,
            "NoteTexture": 4, "NoteTint": 100, "NoteGlow": 8, "NoteEdge": 34, "NoteEdgeWidth": 18,
            "NoteRoundness": 34, "NoteHeadGlow": 0, "NoteFallSpeed": 520, "Notes3D": False,
            "FallingTrail": "None", "FallingTrailIntensity": 0,
            "ImpactBurst": "Dust", "ImpactWave": "None", "ImpactWaveIntensity": 0,
            "ImpactMorph": "None", "ShowImpactFlash": False, "ShowImpactRings": False,
            "ShowEmbers": False, "ShowFlame": False, "ShowWisps": False, "ShowLightBeams": False,
            "AmbientEnergy": "None", "AmbientNature": "None", "AmbientLight": "None", "AmbientCosmic": "None",
            "ShowPetals": False, "ShowStars": False, "ShootingStars": False,
            "ParticleAmount": 0, "ParticleVelocity": 80, "ParticleGlow": 0,
            "ReleaseEffect": "Fade", "ReleaseIntensity": 26,
            "KeyboardStyle": "Classic", "PressedKeyColorMode": "Note", "KeyGlowRadius": 8, "KeyLighting": 14, "ShowKeyFelt": False,
            "BloomIntensity": 8, "BloomSize": 28, "Vignette": 18, "HorizonGlow": 0, "Saturation": 88,
            "ShadingQuality": "Fast", "ShaderCameraTilt": 42, "ShaderGloss": 34, "ShaderShadows": 38,
            "ShaderEmissive": 28, "ShellTheme": "concert-noir", "ChromeMotion": "Calm", "BackdropDensity": 8,
        },
    },
    {
        "name": "Sunset Drive",
        "description": "A polished synthwave look: coral-and-amber neon, short speed lines, a quiet retro grid and compact hit accents.",
        "settings": {
            "NoteStyle": "Neon", "ColorMode": "Gradient", "Palette": "Custom",
            "NoteColorStart": "#F06C92", "NoteColorEnd": "#F7B56F", "HaloColor": "#E98991", "HaloIntensity": 48,
            "HaloPulse": True, "HaloPulseStyle": "Sweep", "HaloPulseIntensity": 16, "HaloPulseSpeed": 20, "NoteShimmer": True, "NoteShimmerAmount": 16,
            "NoteTexture": 10, "NoteTint": 48, "NoteGlow": 52, "NoteEdge": 84, "NoteEdgeWidth": 34,
            "NoteRoundness": 38, "NoteHeadGlow": 22, "NoteFallSpeed": 570,
            "FallingTrail": "Speed Lines", "FallingTrailIntensity": 28, "FallingTrailLength": 34,
            "ImpactBurst": "Fireworks", "ImpactWave": "Shockwave", "ImpactWaveIntensity": 30,
            "ImpactMorph": "Shatter", "ImpactMorphIntensity": 18,
            "ShowImpactFlash": False, "ShowImpactRings": True, "RingSize": 34,
            "HoldBar": True, "HoldBarIntensity": 20, "HoldVibration": False,
            "ReleaseEffect": "Echo Rings", "ReleaseIntensity": 18,
            "AmbientEnergy": "Laser Beams", "AmbientEnergyAmount": 8, "AmbientEnergySpeed": 18,
            "ShowFlame": False, "ShowWisps": False, "ShowStars": False, "ShowLightBeams": False,
            "ShowEmbers": True, "ParticleAmount": 7, "ParticleVelocity": 135, "ParticleSpread": 42,
            "Gravity": 260, "Drag": 32, "ParticleLife": 0.65, "ParticleSize": 1.2, "ParticleGlow": 40,
            "BackgroundGradient": True, "BackgroundMode": "Solid", "BackgroundColor": "#0D1020", "BackgroundDim": 32,
            "BackgroundMotion": "Retro Grid", "BackgroundMotionAmount": 22, "BackgroundMotionSpeed": 18, "BackgroundMotionColor": "#7E526F",
            "ShootingStars": False, "ShowPetals": False,
            "KeyboardStyle": "Glass", "PressedKeyColorMode": "Note", "KeyGlowRadius": 30, "KeyLighting": 24,
            "BloomIntensity": 42, "BloomSize": 50, "Vignette": 28, "HorizonGlow": 18, "Saturation": 106, "Contrast": 102,
            "ShadingQuality": "Balanced", "ShaderCameraTilt": 46, "ShaderGloss": 62, "ShaderShadows": 58,
            "ShaderEmissive": 62, "ShaderRimLight": 48,
            "ShellTheme": "velvet-gold", "ChromeMotion": "Calm", "BackdropDensity": 16,
        },
    },
]

# Accepted ensemble recipes, now reachable without diagnostic flags. Applying a shelf look
# preserves machine-local AR calibration, camera, MIDI/audio routing and GPU preferences.
for _name, _burst, _held, _morph, _release in (
    ("Prism Flow - Wisps", "Embers", "Wisps", "Shatter", "Float Up"),
    ("Prism Flow - Flames", "Splash", "Flames", "Melt", "Dissolve"),
):
    PRESETS.append({
        "name": _name,
        "description": "Approved held/release materials for GPU Live Stage and AR, with balanced AR cap 192. Other burst/morph artwork stays classic; software rendering stays classic.",
        "settings": {
            **PRESETS[0]["settings"],
            "UpgradedParticleEffects": True,
            "ShowNotes": True, "ShowHalo": True, "ShowEmbers": True,
            "ShowWisps": _held == "Wisps", "ShowFlame": _held == "Flames",
            "WispAmount": 32, "WispHeight": 35, "WispSpeed": 100,
            "FlameIntensity": 70, "FlameHeight": 45, "FlameColorMode": "Note",
            "ImpactBurst": _burst, "ImpactMorph": _morph, "ImpactMorphIntensity": 70,
            "ReleaseEffect": _release, "ReleaseIntensity": 70,
            "ParticleAmount": 18, "ParticleResponse": 55, "ParticleVelocity": 170,
            "ParticleSpeed": 100, "ParticleSpread": 38, "ParticleLife": .7, "ParticleSize": 1.8,
            "ParticleLifeRandomness": 0, "ParticleSizeRandomness": 0, "ParticleRandomness": 0,
            "ParticleGlow": 65, "Gravity": 180, "Drag": 32, "Spiral": 0, "EmitterSize": 8,
            "ShowImpactFlash": False, "ShowImpactRings": False, "ImpactWave": "None",
            "ShaderKeyLight": 100, "ShaderRimLight": 62, "ShaderEmissive": 149, "ShaderGloss": 92,
            "ShaderCameraTilt": 38, "ShaderShadows": 46, "ShaderAmbientOcclusion": 52,
            "BloomIntensity": 35, "BackgroundMode": "Solid", "BackgroundColor": "#10141B",
            "KeyLabels": "None", "HorizonGlow": 0, "PedalGlow": True,
            "HoldBar": False, "HoldElectricArc": False, "PhysicsTimeFactor": 100,
        },
    })


# Community looks are also assembled from a quiet, explicit visual baseline rather than inheriting
# whatever a future model-default happens to turn on. Each recipe opts into its own effects below.
COMMUNITY_BASE: dict[str, object] = {
    "UpgradedParticleEffects": False,
    "ShowBackground": True, "ShowNotes": True, "ShowKeys": True, "ShowKeyShadow": True,
    "BackgroundMode": "Solid", "BackgroundColor": "#080B12", "BackgroundGradient": False, "BackgroundGuide": False, "BackgroundDim": 28,
    "BackgroundMotion": "None", "BackgroundMotionAmount": 0, "BackgroundMotionSpeed": 18, "BackgroundMotionColor": "#8A9AAD",
    "ShowEmbers": False, "ShowHalo": True, "ShowFlame": False, "ShowWisps": False, "ShowImpactRings": False, "ShowLightBeams": False,
    "ShowStars": False, "ShootingStars": False, "ShowPetals": False, "ShowNoteLabels": False, "ShowKeyFelt": False,
    "PetalAmount": 0, "StarDensity": 0, "ShootingStarsAmount": 0,
    "NoteStyle": "Solid", "ColorMode": "Gradient", "Palette": "Custom",
    "NoteColorStart": "#63D7DE", "NoteColorEnd": "#F3C56E",
    "LeftHandColor": "#63D7DE", "RightHandColor": "#F3C56E", "HandSplitPitch": 60,
    "NoteWidth": 84, "NoteMinLength": 12, "NoteGap": 2, "NoteFallSpeed": 550,
    "NoteTint": 86, "NoteGlow": 44, "NoteEdge": 68, "NoteEdgeWidth": 30, "NoteRoundness": 38, "NoteHeadGlow": 22, "NoteTexture": 8, "NoteRefraction": 12, "Notes3D": True,
    "HaloColor": "#F3C56E", "HaloIntensity": 38, "HaloPulse": False, "HaloPulseIntensity": 0, "HaloPulseStyle": "Pulse", "HaloPulseSpeed": 18,
    "NoteLandingGlow": False, "NoteLandingGlowAmount": 0,
    "FallingTrail": "None", "FallingTrailIntensity": 0, "FallingTrailLength": 30, "FallingPulse": False, "FallingGhost": False,
    "NoteShimmer": False, "NoteShimmerAmount": 0,
    "ParticleAmount": 0, "ParticleVelocity": 100, "ParticleSpeed": 100, "ParticleResponse": 42, "ParticleSpread": 38, "ParticleLife": 0.6,
    "ParticleLifeRandomness": 25, "ParticleSize": 1.1, "ParticleGlow": 0, "ParticleRandomness": 28, "Gravity": 220, "Drag": 34,
    "WispAmount": 0, "FlameIntensity": 0, "FlameHeight": 30,
    "ImpactBurst": "Dust", "RingSize": 30, "ImpactWave": "None", "ImpactWaveIntensity": 0,
    "ShowImpactFlash": False, "ImpactFlashIntensity": 0, "ImpactFlashStyle": "Flash",
    "ImpactMorph": "None", "ImpactMorphIntensity": 0,
    "HoldBar": False, "HoldBarIntensity": 0, "HoldBreath": False, "HoldVibration": False, "HoldColorCycle": False, "HoldElectricArc": False,
    "ReleaseEffect": "Fade", "ReleaseIntensity": 0,
    "AmbientEnergy": "None", "AmbientEnergyAmount": 0, "AmbientEnergySpeed": 18,
    "AmbientNature": "None", "AmbientNatureAmount": 0, "AmbientNatureSpeed": 18,
    "AmbientLight": "None", "AmbientLightAmount": 0, "AmbientLightSpeed": 18, "AmbientLightColor": "#F3C56E",
    "AmbientCosmic": "None", "AmbientCosmicAmount": 0, "AmbientCosmicSpeed": 18,
    "VelocityColor": False, "OctaveColor": False, "PedalGlow": False, "TempoSync": False, "AudioReactive": False, "ZoneSplit": False,
    "KeyboardStyle": "Studio", "PressedKeyColorMode": "Note", "KeyLighting": 26, "KeyGlowRadius": 28,
    "BloomIntensity": 30, "BloomSize": 44, "Vignette": 22, "HorizonGlow": 12, "BeamIntensity": 0, "Saturation": 100, "Contrast": 100,
    "ShadingQuality": "Balanced", "ChromeMotion": "Calm", "BackdropDensity": 24,
}



def read_consts() -> dict[str, str]:
    """Every ``internal const string`` in the theme, settings and profile-catalogue classes, as ``Class.Name`` → value.

    A const may name another const (``DefaultId = ConcertGrandId``), so those are resolved too. This is what
    lets a property default such as ``RecordingFormatIds.Avi`` be read exactly like the compiled value.
    """
    raw: dict[str, str] = {}
    for path in (SHELL_THEMES, SETTINGS, PROFILE_CATALOGUE, AR_CATALOGUE):
        text = path.read_text(encoding="utf-8")
        for owner, body in re.findall(r"internal static class (\w+)\s*\{(.*?)\n\}", text, re.S):
            for name, value in re.findall(r"internal const string (\w+) = ([^;]+);", body):
                raw[f"{owner}.{name}"] = value.strip()
        # A class may be written without "static" (PianoVisualSettings is sealed); every const in the file is
        # collected under the name of the type that declares it, taken from the nearest class header above.
        for match in re.finditer(r"internal (?:static )?(?:sealed )?class (\w+)", text):
            tail = text[match.end():]
            end = tail.find("\ninternal ")
            body = tail[:end if end >= 0 else len(tail)]
            for name, value in re.findall(r"internal const string (\w+) = ([^;]+);", body):
                raw.setdefault(f"{match.group(1)}.{name}", value.strip())
    consts: dict[str, str] = {}

    def resolve(name: str, owner: str, seen: frozenset[str]) -> str | None:
        # A const may name another const of the same class, written either bare or qualified.
        for candidate in (name, f"{owner}.{name}"):
            if candidate in seen or candidate not in raw:
                continue
            value = raw[candidate]
            literal = re.fullmatch(r'"([^"]*)"', value)
            if literal:
                return literal.group(1)
            return resolve(value, owner, seen | {candidate})
        return None

    for key in raw:
        value = resolve(key, key.rsplit(".", 1)[0], frozenset())
        if value is not None:
            consts[key] = value
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
    # Validate the shared baseline against the current model so a renamed/removed property cannot
    # silently revive an ornamental effect in every community preset.
    for key, value in COMMUNITY_BASE.items():
        if key not in settings:
            raise SystemExit(f"community baseline: {key} is not a setting of PianoVisualSettings")
        settings[key] = value
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
        # Bytes, not text mode: write_text turns "\n" into "\r\n" on Windows, and the checker compares the files byte for byte.
        path.write_bytes(payload.encode("utf-8"))
        written.append(path)
        if not quiet:
            print(f"wrote {path.relative_to(ROOT)} ({len(payload)} bytes)")
    return written


if __name__ == "__main__":
    if not SETTINGS.exists():
        sys.exit(f"run this from the repository root: {SETTINGS} is missing")
    write_all(TARGET)
