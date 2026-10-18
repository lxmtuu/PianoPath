#!/usr/bin/env python3
"""Print the note/effect numbers the action plan quotes, straight from the C# source.

This is an audit aid, not a build step: it parses a handful of named constants so the
document cannot drift away from the code without anyone noticing. Run it after changing
note geometry or effect budgets and update docs/NOTE-EFFECTS-ACTION-PLAN.md.

    python3 tools/note_effects_report.py
    python3 tools/note_effects_report.py --check   # exit 1 when a pinned number moved
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def one(pattern: str, text: str, name: str) -> str:
    match = re.search(pattern, text)
    if not match:
        raise SystemExit(f"cannot find {name} (pattern {pattern!r})")
    return match.group(1)


STAGE = read("Stage/PianoStage.cs")
STAGE_SETTINGS = read("Stage/PianoVisualSettings.cs")
GPU_SIM = read("Gpu/GpuStageSimulation.cs")
GPU_LOOK = read("Gpu/GpuLook.cs")
AR_PROJECTION = read("Camera/ArProjection.cs")
AR_OPTIONS = read("Camera/ArOptions.cs")
CATALOG = read("Stage/Effects/EffectCatalog.cs")
NOTE_GEOMETRY = read("Stage/NoteGeometry.cs")

V = {
    "fall_speed": float(one(r"internal const double FallSpeed = ([0-9.]+);", STAGE, "stage fall speed")),
    "wpf_particles": int(one(r"internal const int MaxParticles = (\d+);", STAGE, "WPF particle cap")),
    "gpu_particles": int(one(r"internal const int MaxParticles = (\d+);", GPU_SIM, "GPU particle cap")),
    "note_width": float(one(r"public double NoteWidth \{ get; set; \} = ([0-9.]+);", STAGE_SETTINGS, "NoteWidth")),
    "note_min": float(one(r"public double NoteMinLength \{ get; set; \} = ([0-9.]+);", STAGE_SETTINGS, "NoteMinLength")),
    "note_gap": float(one(r"public double NoteGap \{ get; set; \} = ([0-9.]+);", STAGE_SETTINGS, "NoteGap")),
    "note_fall": float(one(r"public double NoteFallSpeed \{ get; set; \} = ([0-9.]+);", STAGE_SETTINGS, "NoteFallSpeed")),
    "ar_fall": float(one(r"FallSpeed = ([0-9.]+), double Reach", AR_PROJECTION, "AR fall speed")),
    "ar_reach": float(one(r"FallSpeed = [0-9.]+, double Reach = ([0-9.]+)", AR_PROJECTION, "AR reach")),
    "ar_width": float(one(r"WidthShare = ([0-9.]+)", AR_PROJECTION, "AR width share")),
    "ar_release": float(one(r"internal const double ReleaseLife = ([0-9.]+);", AR_PROJECTION, "AR release life")),
    "ar_live_reach_s": float(one(r"LivePressReachSeconds = ([0-9.]+);", AR_OPTIONS, "AR live reach seconds")),
    "ar_lookahead_s": float(one(r"LookaheadDefault = ([0-9.]+)", AR_OPTIONS, "AR lookahead default")),
    "max_note_seconds": float(one(r"MaxNoteSeconds = ([0-9.]+);", NOTE_GEOMETRY, "max note seconds")),
    "black_fill": float(one(r"BlackKeyFill = ([0-9.]+);", NOTE_GEOMETRY, "black key fill")),
}
entries = [line for line in CATALOG.splitlines() if line.lstrip().startswith('new("')]
V["ids"] = len(entries)
V["planned"] = sum("EffectStatus.Planned" in line for line in entries)
V["available"] = sum("EffectStatus.Available" in line for line in entries)

# Effect lives / caps (first occurrence is WPF, second is the GPU simulation).
wpf_flash = float(one(r"Life = (\.\d+), Strength = strength, Style = _visual\.ImpactFlashStyle", STAGE, "WPF flash life"))
gpu_flash = [float(x) for x in re.findall(r"Life = (\.\d+)f, Size = layout\.Lane|Life = (\.\d+)f, Color = color, Strength = strength, Style", GPU_SIM) for x in [x[0] or x[1]]]
V["wpf_ring_cap"] = int(one(r"_rings\.Count <= (\d+)\) _rings\.Add", STAGE, "WPF ring cap"))
V["wpf_flash_cap"] = int(one(r"_flashes\.Count <= (\d+)\) _flashes\.Add", STAGE, "WPF flash cap"))
V["gpu_ring_cap"] = int(one(r"_rings\.Count < (\d+)\)", GPU_SIM, "GPU ring cap"))
V["gpu_flash_cap"] = int(one(r"_flashes\.Count < (\d+)\)", GPU_SIM, "GPU flash cap"))
V["wpf_flash_life"] = wpf_flash
V["gpu_flash_life_0"] = gpu_flash[0] if gpu_flash else 0.0
V["gpu_flash_life_other"] = gpu_flash[1] if len(gpu_flash) > 1 else 0.0


def layout(width: int, height: int) -> dict[str, float]:
    keyboard = max(80.0, min(300.0, max(110.0, min(228.0, height * .205))))
    hit = height - keyboard
    lane = width / 88
    white = width / 52
    black = white * .52
    speed = V["fall_speed"] * V["note_fall"] / 550
    note_px = min(lane * V["note_width"] / 100, black * V["black_fill"])
    return {
        "height": height,
        "keyboard": keyboard,
        "hit": hit,
        "lane": lane,
        "white": white,
        "black": black,
        "note_px": note_px,
        "note_pct_white": note_px / white * 100,
        "note_pct_black": note_px / black * 100,
        "ar_px": white * V["ar_width"],
        "ar_white_vs_stage": (white * V["ar_width"]) / note_px,
        "speed": speed,
        "lookahead_s": hit / speed,
        "longest_note_s": hit * .9 / speed,
        "px_per_frame": speed / 60,
    }


def main() -> int:
    rows = [layout(1920, 1080), layout(1600, 900), layout(2560, 1440)]
    ratios = {
        "particles": V["gpu_particles"] / V["wpf_particles"],
        "rings": V["gpu_ring_cap"] / V["wpf_ring_cap"],
        "flashes": V["gpu_flash_cap"] / V["wpf_flash_cap"],
        "flash_life_flash": V["gpu_flash_life_0"] / V["wpf_flash_life"],
        "flash_life_other": V["gpu_flash_life_other"] / V["wpf_flash_life"],
        "ar_release": V["ar_live_reach_s"] / V["ar_release"],
        "ar_width_vs_stage": (V["ar_width"] * 100 / (V["note_width"] / 100)),
    }

    print(f"catalog: {V['ids']} ids, {V['available']} available, {V['planned']} planned")
    print(f"caps: particles {V['wpf_particles']} (WPF) vs {V['gpu_particles']} (GPU) = {ratios['particles']:.2f}x")
    print(f"      rings {V['wpf_ring_cap']} vs {V['gpu_ring_cap']}; flashes {V['wpf_flash_cap']} vs {V['gpu_flash_cap']}")
    print(f"flash life: WPF {V['wpf_flash_life']} s; GPU {V['gpu_flash_life_0']} s (Flash) / {V['gpu_flash_life_other']} s (other styles)")
    print(f"AR live release: {V['ar_release']} s flat vs {V['ar_live_reach_s']} s projected = {ratios['ar_release']:.1f}x")
    print(f"AR width share {V['ar_width']:.2f} vs stage {V['note_width'] / 100:.2f} (note width %): a white-key AR note is {rows[0]['ar_white_vs_stage']:.2f}x the stage note")
    print()
    print("| khung | keyTop | lane | nốt WPF/GPU | % phím trắng | % phím đen | nốt AR (phím trắng) | lookahead | thanh 4 s |")
    print("|---|---|---|---|---|---|---|---|---|")
    for r in rows:
        print(f"| {r['height']}p | {r['hit']:.1f} | {r['lane']:.2f} | {r['note_px']:.2f} | {r['note_pct_white']:.1f}% | {r['note_pct_black']:.1f}% | {r['ar_px']:.2f} | {r['lookahead_s']:.2f} s | {min(4.0, V['max_note_seconds']):.2f} s |")

    print()
    print(f"| độ dài nốt | 900p | 1080p | 1440p | AR (Reach {V['ar_lookahead_s']:.1f} s) |")
    print("|---|---|---|---|---|")
    for duration in (0.5, 2.0, 3.0, 4.0, 6.0, 30.0):
        shown = min(duration, V["max_note_seconds"])
        cells = [f"{shown:.2f} s" for _ in rows]
        ar = min(duration, V["ar_lookahead_s"])
        cells.append(f"{ar:.2f} s" + (f" (cắt {duration - ar:.2f})" if duration - ar > 1e-3 else ""))
        print(f"| {duration:g} s | " + " | ".join(cells) + " |")

    if "--check" in sys.argv:
        pinned = {
            "fall_speed": 258.0, "wpf_particles": 2600, "gpu_particles": 24000,
            "note_width": 80.0, "note_min": 16.0, "note_gap": 2.0, "note_fall": 550.0,
            "ar_fall": 1.5, "ar_reach": 4.0, "ar_width": .86, "ar_release": .35, "ar_live_reach_s": 3.0, "ar_lookahead_s": 3.0,
            "wpf_ring_cap": 64, "wpf_flash_cap": 32, "gpu_ring_cap": 96, "gpu_flash_cap": 48,
            "wpf_flash_life": .18, "gpu_flash_life_0": .38, "gpu_flash_life_other": .26,
            "ids": 87, "available": 86, "planned": 1,
            "max_note_seconds": 20.0, "black_fill": .98,
        }
        bad = {k: (V[k], v) for k, v in pinned.items() if abs(float(V[k]) - float(v)) > 1e-9}
        if bad:
            for k, (got, want) in bad.items():
                print(f"DRIFT {k}: source {got}, plan {want}", file=sys.stderr)
            return 1
        print("\nall pinned numbers match the plan")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
