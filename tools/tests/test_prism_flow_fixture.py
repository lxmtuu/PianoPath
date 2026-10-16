"""Input-fixture tests only; not evidence about MIDIReader, WPF, D3D or AR."""
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "make_prism_flow_fixture.py"
SPEC = importlib.util.spec_from_file_location("prism_fixture", SCRIPT)
fixture = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(fixture)


def read_vlq(raw, pos):
    value = 0
    for _ in range(4):
        byte = raw[pos]
        pos += 1
        value = (value << 7) | (byte & 127)
        if not byte & 128:
            return value, pos
    raise ValueError("Unterminated VLQ")


def decode_tracks(raw):
    """Independent small SMF reader: reject truncation and unsupported messages."""
    if raw[:4] != b"MThd":
        raise ValueError("Missing SMF header")
    length, fmt, count, ppq = struct.unpack(">IHHH", raw[4:14])
    if (length, fmt, count, ppq) != (6, 1, 3, 480):
        raise ValueError("Unexpected header")
    pos, tracks = 14, []
    for _ in range(count):
        if raw[pos:pos + 4] != b"MTrk":
            raise ValueError("Missing track")
        size = struct.unpack(">I", raw[pos + 4:pos + 8])[0]
        pos += 8
        end = pos + size
        if end > len(raw):
            raise ValueError("Truncated track")
        events, at = [], 0
        while pos < end:
            delta, pos = read_vlq(raw, pos)
            at += delta
            status = raw[pos]
            pos += 1
            if status == 255:
                kind = raw[pos]
                pos += 1
                size, pos = read_vlq(raw, pos)
                message = (status, kind, raw[pos:pos + size])
                pos += size
            elif status & 0xF0 in (0x80, 0x90, 0xB0):
                message = (status, raw[pos], raw[pos + 1])
                if message[1] >= 128 or message[2] >= 128:
                    raise ValueError("Invalid channel data")
                pos += 2
            else:
                raise ValueError("Unexpected MIDI status")
            events.append((at, message))
        if pos != end or events[-1][1] != (255, 47, b""):
            raise ValueError("Track must end at EOT")
        tracks.append(events)
    if pos != len(raw):
        raise ValueError("Trailing bytes")
    return tracks


class PrismFixtureTests(unittest.TestCase):
    def test_committed_product_inputs_match_generator(self):
        directory = SCRIPT.parents[1] / "docs/samples/prism-flow-p0"
        for name, expected in fixture.artifacts().items():
            with self.subTest(file=name):
                self.assertEqual((directory / name).read_bytes(), expected)

    def test_deterministic_manifest_and_hash(self):
        self.assertEqual(fixture.artifacts(), fixture.artifacts())
        files = fixture.artifacts()
        manifest = json.loads(files["manifest.json"])
        self.assertEqual(manifest["midiSha256"], hashlib.sha256(files["baseline.mid"]).hexdigest())
        self.assertEqual(manifest["status"], "input-fixture-not-renderer-evidence")

    def test_midi_round_trip_matches_note_and_controller_inputs(self):
        data = fixture.scene()
        tracks = decode_tracks(fixture.midi(data))
        self.assertIn((0, (255, 81, b"\x07\xa1\x20")), tracks[0])
        for channel in (0, 1):
            actual = [(at, msg) for at, msg in tracks[channel + 1] if msg[0] != 255]
            expected = []
            for n in data["notes"]:
                if n["channel"] == channel:
                    expected.extend([(fixture.tick(n["start"]), (0x90 | channel, n["pitch"], n["velocity"])),
                                     (fixture.tick(n["start"] + n["duration"]), (0x80 | channel, n["pitch"], 0))])
            for cc in data["controls"]:
                if cc["channel"] == channel:
                    expected.append((fixture.tick(cc["time"]), (0xB0 | channel, cc["controller"], cc["value"])))
            self.assertCountEqual(actual, expected)
        self.assertTrue(all(t[-1][0] == 28 * 960 for t in tracks))

    def test_balanced_notes_and_pedal(self):
        tracks = decode_tracks(fixture.midi(fixture.scene()))
        held = set()
        for events in tracks[1:]:
            for _, msg in events:
                status, key, value = msg
                channel = status & 15
                if status & 0xF0 == 0x90:
                    self.assertNotIn((channel, key), held)
                    held.add((channel, key))
                elif status & 0xF0 == 0x80:
                    self.assertIn((channel, key), held)
                    held.remove((channel, key))
        self.assertFalse(held)
        self.assertEqual([(t, msg[2]) for t, msg in tracks[1] if msg[:2] == (0xB0, 64)],
                         [(12 * 960, 127), (16 * 960, 0)])

    def test_checkpoints_cover_hold_short_and_rest(self):
        data = fixture.scene()
        checkpoints = {c["id"]: c["time"] for c in data["checkpoints"]}
        self.assertEqual(len(checkpoints), len(data["checkpoints"]))
        def held(t):
            return [n for n in data["notes"] if n["start"] <= t < n["start"] + n["duration"]]
        self.assertFalse(held(checkpoints["initial-rest"]))
        self.assertFalse(held(checkpoints["final-rest"]))
        self.assertFalse(held(checkpoints["pedal-held-keys-up"]))
        self.assertEqual(len(held(checkpoints["white-black-hold"])), 2)
        self.assertEqual(len(held(checkpoints["dense-chord"])), 10)
        self.assertEqual(len(held(checkpoints["short-note"])), 1)
        self.assertEqual({n["pitch"] for n in held(checkpoints["same-pitch-two-channels"])}, {60})

    def test_vlq_boundaries_and_invalid_timing(self):
        for value in (0, 127, 128, 16383, 16384, 0x0FFFFFFF):
            encoded = fixture.vlq(value)
            self.assertEqual(read_vlq(encoded, 0), (value, len(encoded)))
        for value in (-1, 0x10000000, 1.5):
            with self.assertRaises(ValueError):
                fixture.vlq(value)
        with self.assertRaises(ValueError):
            fixture.tick(.0001)

    def test_truncated_midi_rejected(self):
        with self.assertRaises(ValueError):
            decode_tracks(fixture.midi(fixture.scene())[:-1])


if __name__ == "__main__":
    unittest.main()
