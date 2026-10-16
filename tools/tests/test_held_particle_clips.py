import importlib.util
import struct
import unittest
import zlib
from pathlib import Path

spec = importlib.util.spec_from_file_location("clips", Path(__file__).resolve().parents[1] / "make_held_particle_clips.py")
clips = importlib.util.module_from_spec(spec)
spec.loader.exec_module(clips)


def png(rgb, width=1):
    return clips.SIGNATURE + clips.chunk(b"IHDR", struct.pack(">IIBBBBB", width, 1, 8, 2, 0, 0, 0)) + clips.chunk(b"IDAT", zlib.compress(b"\0" + rgb * width)) + clips.chunk(b"IEND", b"")


class HeldClipTests(unittest.TestCase):
    def test_frames_remain_lossless_with_exact_sequence_and_timing(self):
        output = clips.assemble([png(b"\xff\0\0"), png(b"\0\0\xff")], 30)
        offset, sequences, raw = 8, [], []
        while offset < len(output):
            size = struct.unpack_from(">I", output, offset)[0]
            kind, data = output[offset+4:offset+8], output[offset+8:offset+8+size]
            if kind == b"fcTL":
                values = struct.unpack(">IIIIIHHBB", data)
                sequences.append(values[0])
                self.assertEqual(values[5:7], (1, 30))
                self.assertEqual(values[7:], (0, 0))
            if kind == b"IDAT": raw.append(zlib.decompress(data))
            if kind == b"fdAT":
                sequences.append(struct.unpack_from(">I", data)[0])
                raw.append(zlib.decompress(data[4:]))
            offset += 12 + size
        self.assertEqual(sequences, [0, 1, 2])
        self.assertEqual(raw, [b"\0\xff\0\0", b"\0\0\0\xff"])

    def test_inconsistent_sizes_rejected(self):
        with self.assertRaises(ValueError): clips.assemble([png(b"123"), png(b"123", 2)], 30)

    def test_bad_crc_and_truncation_rejected(self):
        bad = bytearray(png(b"123")); bad[25] ^= 1
        with self.assertRaises(ValueError): clips.read_png(bad)
        with self.assertRaises((ValueError, struct.error)): clips.read_png(png(b"123")[:30])

    def test_empty_frames_and_invalid_rate_rejected(self):
        for frames, fps in [([],30), ([png(b"123")],0), ([png(b"123")],61)]:
            with self.assertRaises(ValueError): clips.assemble(frames,fps)
