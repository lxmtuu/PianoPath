#!/usr/bin/env python3
"""Generate docs/samples/ar-keyboard-topdown.png (+ .json): a synthetic "camera picture" of a piano keyboard.

Why a drawn picture instead of a photo: the AR overlay (docs/AR-OVERLAY-ROADMAP.md) has to be checked against
a keyboard seen by a camera, and CI has no camera. A real photo would also put somebody else's piano and
somebody else's copyright into an MIT repo. This script draws an original keyboard from the same geometry the
application uses (Stage/KeyboardLayout.cs: equal white keys, black keys 0.52 of a white key wide, nudged the way
a real keyboard nudges them), then looks at it the way a camera mounted above the keys would: seen through a
known homography, with light falling off toward the corners and a little sensor noise. Same input, same bytes,
every run — the generator is the licence.

The .json beside the picture is the ground truth the checks compare a detector or a calibration against: the
four image corners of the key area (back-left, back-right, front-right, front-left), the 3x3 homography from
keyboard coordinates (u across 0..1, v from the back of the keys 0 to their front 1) to image pixels, and the
image position of the centre of every key's top edge.

    python3 tools/make_ar_sample.py [--keys 88|76|61|49] [output.png]

Keep this file's layout rules in step with Stage/KeyboardLayout.cs; tests/PianoPath.Tests/KeyboardLayoutTests.cs
pins the C# side, and this script's own self-check (--check) pins this side to the same numbers.
"""
import json
import math
import struct
import sys
import zlib

W, H = 1280, 720

# ----------------------------------------------------------------------------- the keyboard (mirror of KeyboardLayout.cs)
BLACK_WIDTH_SHARE = 0.52
BLACK_OFFSET = {1: -0.08, 3: 0.08, 6: -0.10, 8: 0.0, 10: 0.10}
LAYOUTS = {88: 21, 76: 28, 61: 36, 49: 36}  # key count -> lowest MIDI pitch

# Real dimensions, in millimetres, only to give the key area its true shape: a white key is 23.5 mm wide
# and about 150 mm long, a black key about 95 mm long.
WHITE_MM, WHITE_LENGTH_MM, BLACK_LENGTH_MM = 23.5, 150.0, 95.0

# Where the key area lands in the picture (back-left, back-right, front-right, front-left): a camera above
# the keys, turned a little and tilted a little so the picture is a real perspective and not a flat rectangle.
CORNERS = {
    88: [(70, 292), (1214, 268), (1236, 421), (52, 449)],
    76: [(110, 288), (1180, 266), (1200, 428), (92, 452)],
    61: [(190, 276), (1088, 258), (1106, 432), (172, 452)],
    49: [(262, 270), (1010, 254), (1030, 436), (246, 456)],
}


def is_black(pitch):
    return pitch % 12 in BLACK_OFFSET


class Layout:
    def __init__(self, keys):
        self.keys, self.first = keys, LAYOUTS[keys]
        self.whites_below, whites = [], []
        for pitch in range(128):
            self.whites_below.append(len(whites))
            if self.first <= pitch < self.first + keys and not is_black(pitch):
                whites.append(pitch)
        self.whites = whites
        self.white_width = 1.0 / len(whites)

    def center(self, pitch):
        below = self.whites_below[pitch]
        return ((below + BLACK_OFFSET[pitch % 12]) if is_black(pitch) else (below + 0.5)) * self.white_width

    def black_span(self, pitch):
        half = self.white_width * BLACK_WIDTH_SHARE / 2
        return self.center(pitch) - half, self.center(pitch) + half

    def blacks_near(self, white_index):
        """Black keys whose span can reach into a white slot: the pitches just below and just above its white key."""
        found = []
        for pitch in (self.whites[white_index] - 1, self.whites[white_index] + 1):
            if self.first <= pitch < self.first + self.keys and is_black(pitch):
                found.append((pitch,) + self.black_span(pitch))
        return found


# ----------------------------------------------------------------------------- projective maths
def solve(matrix, rhs):
    """Gaussian elimination with partial pivoting; matrix is square, small, and never singular here."""
    n = len(rhs)
    rows = [list(matrix[i]) + [rhs[i]] for i in range(n)]
    for col in range(n):
        pivot = max(range(col, n), key=lambda r: abs(rows[r][col]))
        rows[col], rows[pivot] = rows[pivot], rows[col]
        for r in range(col + 1, n):
            factor = rows[r][col] / rows[col][col]
            for c in range(col, n + 1):
                rows[r][c] -= factor * rows[col][c]
    out = [0.0] * n
    for r in range(n - 1, -1, -1):
        out[r] = (rows[r][n] - sum(rows[r][c] * out[c] for c in range(r + 1, n))) / rows[r][r]
    return out


def homography(src, dst):
    """3x3 matrix (row major, h22 = 1) taking four src points to four dst points."""
    a, b = [], []
    for (x, y), (u, v) in zip(src, dst):
        a.append([x, y, 1, 0, 0, 0, -u * x, -u * y]); b.append(u)
        a.append([0, 0, 0, x, y, 1, -v * x, -v * y]); b.append(v)
    h = solve(a, b) + [1.0]
    return [h[0:3], h[3:6], h[6:9]]


def invert(m):
    (a, b, c), (d, e, f), (g, h, i) = m
    det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    return [[(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det],
            [(f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det],
            [(d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det]]


def apply(m, x, y):
    w = m[2][0] * x + m[2][1] * y + m[2][2]
    return (m[0][0] * x + m[0][1] * y + m[0][2]) / w, (m[1][0] * x + m[1][1] * y + m[1][2]) / w


# ----------------------------------------------------------------------------- drawing
def smooth(edge0, edge1, x):
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def noise(x, y, seed):
    """Cheap deterministic hash noise in -1..1: a sensor's grain, the same on every run."""
    n = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 32767.5 - 1.0


def shade(layout, u, v, aspect_u_per_px, px, py):
    """Colour of one point of the keyboard plane. u 0..1 across, v 0 at the back of the keys and 1 at their front."""
    # Pixel footprint in key units, so edges soften by about one pixel whatever the zoom.
    fu = max(1e-6, aspect_u_per_px)
    fv = fu * (WHITE_MM * len(layout.whites)) / WHITE_LENGTH_MM
    if v < -0.55 or v > 1.45 or u < -0.06 or u > 1.06:
        # The desk around the instrument: dark wood, a slow grain across it.
        grain = 0.5 + 0.5 * math.sin(py * 0.05 + 3 * math.sin(px * 0.004))
        return (38 + 14 * grain, 30 + 10 * grain, 25 + 8 * grain)
    if v < 0:
        # The fallboard behind the keys: black lacquer with a soft sheen.
        sheen = 18 * smooth(-0.55, -0.05, v) + 10 * math.exp(-((u - 0.5) ** 2) * 14)
        return (14 + sheen, 14 + sheen, 18 + sheen)
    if v > 1:
        # The key slip in front of the keys: a darker strip with a bright lip.
        lip = smooth(1.0, 1.0 + fv, v) * (1 - smooth(1.0 + fv, 1.0 + 3 * fv, v))
        return (34 + 60 * lip, 30 + 52 * lip, 28 + 46 * lip)
    if u < 0 or u > 1:
        return (30, 28, 30)
    index = min(len(layout.whites) - 1, int(u * len(layout.whites)))
    local = u * len(layout.whites) - index  # 0..1 inside this white slot
    # Gap between two white keys: a dark hairline that does not reach past the front edge.
    gap = 1 - smooth(0.012, 0.012 + 1.2 * fu * len(layout.whites), min(local, 1 - local))
    base = 236 - 22 * v + 6 * math.sin(index * 1.7)  # a hint of difference from key to key, and a darker front
    colour = [base, base - 3, base - 11]
    colour = [c * (1 - 0.75 * gap) for c in colour]
    # Black keys: stand in the back 0.62 of the key length, with a lit top and a shadow thrown to the right.
    black_end = BLACK_LENGTH_MM / WHITE_LENGTH_MM
    for _, left, right in layout.blacks_near(index):
        if v < black_end + 3 * fv and left - 0.4 * layout.white_width <= u <= right + 1.2 * layout.white_width:
            inside_u = smooth(left - fu, left + fu, u) * (1 - smooth(right - fu, right + fu, u))
            inside_v = 1 - smooth(black_end - fv, black_end + fv, v)
            body = inside_u * inside_v
            top = 1 - smooth(0.0, 0.5 * black_end, v)  # slight sheen toward the back
            black = [18 + 22 * top * (1 - v), 18 + 22 * top * (1 - v), 22 + 26 * top * (1 - v)]
            # The lit chamfer along the front end of the black key.
            chamfer = inside_u * smooth(black_end - 4 * fv, black_end - fv, v) * (1 - smooth(black_end - fv, black_end + fv, v))
            black = [c + 55 * chamfer for c in black]
            # Shadow of the black key on the white one beside it, falling to the right.
            shadow_u = smooth(right, right + 0.9 * layout.white_width, u) * (1 - smooth(right + 0.9 * layout.white_width, right + 1.2 * layout.white_width, u))
            shadow = 0.45 * shadow_u * inside_v * (1 - body)
            colour = [c * (1 - shadow) * (1 - body) + b * body for c, b in zip(colour, black)]
    return tuple(colour)


def render(keys):
    layout = Layout(keys)
    corners = CORNERS[keys]
    # Keyboard coordinates: u 0..1 across; v 0..1 from the back of the keys to the front.
    to_image = homography([(0, 0), (1, 0), (1, 1), (0, 1)], corners)
    to_plane = invert(to_image)
    # How many key units one pixel covers across the keys, for edge softness.
    left_px, right_px = apply(to_image, 0, 0.5), apply(to_image, 1, 0.5)
    aspect = 1.0 / max(1.0, math.hypot(right_px[0] - left_px[0], right_px[1] - left_px[1]))
    rows = []
    for y in range(H):
        row = bytearray([0])
        for x in range(W):
            u, v = apply(to_plane, x + 0.5, y + 0.5)
            r, g, b = shade(layout, u, v, aspect, x, y)
            # Light that falls off toward the corners, a warm lamp from the upper left, and sensor grain.
            dx, dy = (x - W / 2) / (W / 2), (y - H / 2) / (H / 2)
            light = 1.0 - 0.22 * (dx * dx + dy * dy) + 0.05 * (-dx - dy)
            grain = 3.0 * noise(x, y, 7)
            row += bytes((max(0, min(255, int(r * light * 1.02 + grain))),
                          max(0, min(255, int(g * light + grain))),
                          max(0, min(255, int(b * light * 0.96 + grain)))))
        rows.append(bytes(row))
    return layout, to_image, rows


def write_png(path, rows):
    def chunk(kind, payload):
        body = kind + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b""))
    with open(path, "wb") as handle:
        handle.write(png)
    return len(png)


def truth(layout, to_image):
    return {
        "image": [W, H],
        "keys": layout.keys,
        "firstPitch": layout.first,
        "whiteKeys": len(layout.whites),
        "corners": [list(map(lambda n: round(n, 3), p)) for p in CORNERS[layout.keys]],
        "cornerOrder": "back-left, back-right, front-right, front-left (of the key area)",
        "homography": [[round(n, 9) for n in row] for row in to_image],
        "homographyMaps": "keyboard (u across 0..1, v back-to-front 0..1) -> image pixels",
        "keyTopCentres": {str(p): [round(n, 3) for n in apply(to_image, layout.center(p), 0.25 if is_black(p) else 0.8)]
                          for p in range(layout.first, layout.first + layout.keys)},
    }


def self_check():
    """The numbers this script must share with Stage/KeyboardLayout.cs; fails loudly if they drift."""
    full = Layout(88)
    assert len(full.whites) == 52 and full.first == 21
    assert abs(full.center(21) - 0.5 / 52) < 1e-12 and abs(full.center(22) - 1.1 / 52) < 1e-12
    for keys, whites in ((88, 52), (76, 45), (61, 36), (49, 29)):
        assert len(Layout(keys).whites) == whites, (keys, whites)
    # The picture's corners must make a key area of about the real aspect, or the sample teaches the wrong shape.
    for keys, corners in CORNERS.items():
        top = math.dist(corners[0], corners[1]); left = math.dist(corners[0], corners[3])
        real = (WHITE_LENGTH_MM) / (WHITE_MM * len(Layout(keys).whites))
        assert 0.7 < (left / top) / real < 1.6, (keys, left / top, real)
    print("self-check ok")


def main():
    args = sys.argv[1:]
    if "--check" in args:
        self_check()
        return
    keys = 88
    if "--keys" in args:
        index = args.index("--keys")
        keys = int(args[index + 1])
        del args[index:index + 2]
        if keys not in LAYOUTS:
            sys.exit(f"--keys must be one of {sorted(LAYOUTS, reverse=True)}")
    out = args[0] if args else "docs/samples/ar-keyboard-topdown.png"
    self_check()
    layout, to_image, rows = render(keys)
    size = write_png(out, rows)
    with open(out.rsplit(".", 1)[0] + ".json", "w", encoding="utf-8") as handle:
        json.dump(truth(layout, to_image), handle, indent=1)
        handle.write("\n")
    print(f"{out}: {W}x{H}, {size / 1024:.0f} KiB, {keys} keys")


if __name__ == "__main__":
    main()
