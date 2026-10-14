#!/usr/bin/env python3
"""Losslessly wrap full PNG frames in APNG. No palette quantisation, codec or dropped frames."""
import argparse
import json
import struct
import zlib
from pathlib import Path

SIGNATURE = b"\x89PNG\r\n\x1a\n"


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)


def read_png(data):
    if not data.startswith(SIGNATURE):
        raise ValueError("not PNG")
    offset = 8
    header = None
    compressed = []
    while offset < len(data):
        size = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset + 4:offset + 8]
        body = data[offset + 8:offset + 8 + size]
        if offset + 12 + size > len(data):
            raise ValueError("truncated PNG")
        crc = struct.unpack_from(">I", data, offset + 8 + size)[0]
        if crc != zlib.crc32(kind + body) & 0xffffffff:
            raise ValueError("PNG CRC mismatch")
        if kind == b"IHDR":
            header = body
        elif kind == b"IDAT":
            compressed.append(body)
        elif kind == b"IEND":
            break
        offset += size + 12
    if header is None or not compressed or len(header) != 13:
        raise ValueError("incomplete PNG")
    # The diagnostic encoder emits full RGB/RGBA, no palettes or interlacing.
    w, h, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", header)
    if not w or not h or depth != 8 or color not in (2, 6) or compression or filtering or interlace:
        raise ValueError("unsupported frame PNG")
    return header, b"".join(compressed)


def assemble(frames, fps):
    if not frames or not 1 <= fps <= 60:
        raise ValueError("invalid animation timing")
    parsed = [read_png(f) for f in frames]
    header = parsed[0][0]
    if any(h != header for h, _ in parsed):
        raise ValueError("inconsistent frame headers")
    w, h = struct.unpack_from(">II", header)
    result = bytearray(SIGNATURE + chunk(b"IHDR", header) + chunk(b"acTL", struct.pack(">II", len(frames), 0)))
    sequence = 0
    for index, (_, compressed) in enumerate(parsed):
        result += chunk(b"fcTL", struct.pack(">IIIIIHHBB", sequence, w, h, 0, 0, 1, fps, 0, 0))
        sequence += 1
        if index == 0:
            result += chunk(b"IDAT", compressed)
        else:
            result += chunk(b"fdAT", struct.pack(">I", sequence) + compressed)
            sequence += 1
    return bytes(result + chunk(b"IEND", b""))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    manifest = json.loads((args.directory / "manifest.json").read_text(encoding="utf-8-sig"))
    if len(manifest["captures"]) != 8:
        raise ValueError("expected eight classic/art Stage/AR motion sequences")
    for capture in manifest["captures"]:
        folder = args.directory / capture["name"]
        files = sorted(folder.glob("*.png"))
        if len(files) != capture["frames"] or len(files) != 60:
            raise ValueError("missing motion frames")
        output = args.directory / (capture["name"] + ".png")
        output.write_bytes(assemble([f.read_bytes() for f in files], capture["fps"]))
        print(f"{capture['name']}: {len(files)} frames, {capture['fps']} FPS, {output.stat().st_size} bytes")


if __name__ == "__main__":
    main()
