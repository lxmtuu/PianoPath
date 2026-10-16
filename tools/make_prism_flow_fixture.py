#!/usr/bin/env python3
"""Generate a deterministic P0 MIDI + review manifest, NOT a renderer or app test.

    python3 tools/make_prism_flow_fixture.py
    python3 tools/make_prism_flow_fixture.py --check

Generated files live in ignored tools/out/prism-flow-p0/. No third-party assets,
wall-clock timestamps, random input, or dependency downloads are used.
The MIDI includes physical note-off and CC64 separately; consumers must not
mistake its note intervals for a sustain-aware sounding-state simulation.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PPQ = 480
TICKS_PER_SECOND = 960  # fixed 120 BPM, 500000 microseconds/quarter
DURATION = 28


def vlq(value):
    if not isinstance(value, int) or not 0 <= value <= 0x0FFFFFFF:
        raise ValueError("MIDI VLQ must be an integer in 0..0x0fffffff")
    result = [value & 127]
    value >>= 7
    while value:
        result.insert(0, (value & 127) | 128)
        value >>= 7
    return bytes(result)


def scene():
    notes = []

    def note(start, duration, pitch, velocity=80, channel=0):
        notes.append(dict(start=start, duration=duration, pitch=pitch,
                          velocity=velocity, channel=channel))

    # Boundary keys, long white/black pair, short repeated notes and velocities.
    note(1, .5, 21, 40)
    note(2, .5, 108, 110)
    note(3, 2, 60)
    note(3, 2, 61, 96, 1)
    for i, velocity in enumerate((24, 48, 80, 112, 127)):
        note(6 + i * .25, .0625, 64, velocity)
    for i, pitch in enumerate((36, 43, 48, 55, 60, 61, 64, 67, 72, 79)):
        note(9, 1.5, pitch, 55 + i * 6, i % 2)
    # Hands release at 13s; pedal remains down until 16s.
    for pitch in (48, 60, 64, 67):
        note(12.25, .75, pitch, 88)
    note(18, 3, 60, 76)
    note(18.5, 1, 60, 104, 1)  # same pitch, independent channel
    note(23, 1, 61, 64)
    controls = [dict(time=12, channel=0, controller=64, value=127),
                dict(time=16, channel=0, controller=64, value=0)]
    checkpoints = [
        (0.5, "initial-rest"), (1.25, "lowest-key"), (2.25, "highest-key"),
        (4, "white-black-hold"), (6.03125, "short-note"),
        (9.5, "dense-chord"), (12.5, "pedal-held-keys-down"),
        (13.5, "pedal-held-keys-up"), (16.25, "pedal-release-tail"),
        (19, "same-pitch-two-channels"), (21.25, "release-tail"),
        (27, "final-rest"),
    ]
    return dict(schemaVersion=1, status="input-fixture-not-renderer-evidence",
                durationSeconds=DURATION, ppq=PPQ, tempoMicroseconds=500000,
                renderSeedProposal=20261014, renderFpsToTest=[30, 60, 120],
                notes=notes, controls=controls,
                checkpoints=[dict(time=t, id=name) for t, name in checkpoints],
                manualScenarios=["pause-resume", "seek-forward-backward", "loop",
                                 "mode-switch-while-held", "all-notes-off",
                                 "camera-disconnect", "midi-disconnect",
                                 "ar-delay-zero-and-nonzero", "mirror-crop-pose-change"])


def tick(seconds):
    value = seconds * TICKS_PER_SECOND
    if int(value) != value or value < 0:
        raise ValueError("Fixture timing must be exactly representable in MIDI ticks")
    return int(value)


def midi(data):
    """SMF type 1: conductor + two channel tracks, explicit status bytes.

    At a shared tick: controller before note-off before note-on. Stable order
    within a class makes exported bytes independent of filesystem/platform.
    """
    end = tick(data["durationSeconds"])

    def track(events):
        payload = bytearray()
        previous = 0
        for at, priority, message in sorted(events, key=lambda e: (e[0], e[1])):
            if at < 0 or at > end:
                raise ValueError("Event outside fixture duration")
            payload.extend(vlq(at - previous) + message)
            previous = at
        payload.extend(vlq(end - previous) + b"\xff\x2f\x00")
        return b"MTrk" + struct.pack(">I", len(payload)) + payload

    tracks = [track([(0, 0, b"\xff\x51\x03" + data["tempoMicroseconds"].to_bytes(3, "big")),
                     (0, 1, b"\xff\x58\x04\x04\x02\x18\x08")])]
    for channel in (0, 1):
        events = []
        for cc in data["controls"]:
            if cc["channel"] == channel:
                events.append((tick(cc["time"]), 0,
                               bytes((0xB0 | channel, cc["controller"], cc["value"]))))
        for n in data["notes"]:
            if n["channel"] == channel:
                events.append((tick(n["start"]), 2,
                               bytes((0x90 | channel, n["pitch"], n["velocity"]))))
                events.append((tick(n["start"] + n["duration"]), 1,
                               bytes((0x80 | channel, n["pitch"], 0))))
        tracks.append(track(events))
    return b"MThd" + struct.pack(">IHHH", 6, 1, len(tracks), PPQ) + b"".join(tracks)


def artifacts():
    data = scene()
    raw = midi(data)
    data["midiSha256"] = hashlib.sha256(raw).hexdigest()
    manifest = (json.dumps(data, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    return {"baseline.mid": raw, "manifest.json": manifest}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "tools/out/prism-flow-p0")
    parser.add_argument("--check", action="store_true", help="Compare existing files, do not write")
    args = parser.parse_args()
    expected = artifacts()
    if args.check:
        for name, content in expected.items():
            path = args.output / name
            if not path.exists() or path.read_bytes() != content:
                parser.exit(1, f"Mismatch or missing fixture: {path}\n")
        print("PASS fixture bytes and manifest match generator; no renderer was tested.")
    else:
        args.output.mkdir(parents=True, exist_ok=True)
        for name, content in expected.items():
            (args.output / name).write_bytes(content)
        print(f"Wrote {len(expected)} input fixtures to {args.output}; no renderer was tested.")


if __name__ == "__main__":
    main()
