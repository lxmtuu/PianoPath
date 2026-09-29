#!/usr/bin/env python3
"""Generate docs/samples/stage-backdrop.png: the sample stage backdrop used by the README.

Why a generated picture instead of a photo: the app draws a user's own image behind the keys, so the
documentation wants a preview of that feature. A real screenshot would go stale on the next UI change
(docs/LOCALIZATION.md closes that loop by rendering the previews in CI), and a stock photo would put
third-party artwork into an MIT repo with no licence attached. This file therefore draws an original
backdrop from a few gradients and a fixed star catalogue: same input, same bytes, every run, and the
generator is the licence.

    python3 tools/make_stage_background.py [output.png]

The CI job renders its backdrop preview with --background-image=docs/samples/stage-backdrop.png, so
regenerating the picture and re-running the workflow is all it takes to refresh that README image too.
"""
import math
import random
import struct
import sys
import zlib

W, H = 1280, 720
SEED = 20260930  # the star field is a catalogue, not a roll of the dice: fixed so the file is stable

# --------------------------------------------------------------------------- sky
# Stops down the canvas: night at the top, a cool haze in the middle, a warm floor bounce at the
# bottom. Interpolated per row, so one row of colour is computed 720 times, not a million.
STOPS = [(0.00, (5, 7, 16)), (0.42, (11, 18, 44)), (0.68, (32, 28, 62)), (0.86, (64, 40, 52)),
         (1.00, (14, 12, 22))]


def row_colours():
    rows = []
    for y in range(H):
        t = y / (H - 1)
        for i in range(len(STOPS) - 1):
            (a, ca), (b, cb) = STOPS[i], STOPS[i + 1]
            if t <= b:
                f = 0.0 if b == a else (t - a) / (b - a)
                f = f * f * (3 - 2 * f)  # smoothstep, so no visible band edges
                rows.append(tuple(ca[c] + (cb[c] - ca[c]) * f for c in range(3)))
                break
    return rows


def gauss_1d(centre, sigma, span):
    """Separable half of a soft bloom: the two halves multiply into a Gaussian falloff."""
    return [math.exp(-((v - centre) ** 2) / (2 * sigma * sigma)) for v in span] if sigma > 0 else None


def build_bloom(cx, cy, radius, colour):
    """Soft radial light: colour * exp(-d^2), written as the product of two 1-D curves."""
    xs = gauss_1d(cx, radius, range(W))
    ys = gauss_1d(cy, radius, range(H))
    return [(colour[0] * ys[y], colour[1] * ys[y], colour[2] * ys[y], xs) for y in range(H)]


def vignette():
    """Darken the corners: 1 - k*dx^2 - k*dy^2, again separable."""
    k = 0.42
    fx = [1 - k * ((x - (W - 1) / 2) / (W / 2)) ** 2 for x in range(W)]
    fy = [1 - k * ((y - (H - 1) / 2) / (H / 2)) ** 2 for y in range(H)]
    return [max(0.0, fy[y]) for y in range(H)], fx


def stars():
    """Deterministic catalogue: mostly faint, a handful bright, all above the horizon haze."""
    rnd = random.Random(SEED)
    field = []
    for i in range(190):
        x = rnd.uniform(0, W)
        y = rnd.uniform(0, H * 0.72) ** 1.15  # crowd them towards the top of the sky
        if y > H * 0.7:
            continue
        sigma = rnd.choice([0.55, 0.55, 0.75, 1.0, 1.4])
        amp = rnd.uniform(16, 60) * (1.0 if sigma < 1.2 else 1.6)
        warm = rnd.uniform(0.0, 0.35)
        field.append((x, y, sigma, amp, (255 - 40 * warm, 250 - 70 * warm, 255 - 120 * warm)))
    return field


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "docs/samples/stage-backdrop.png"
    base = row_colours()
    moon = build_bloom(W * 0.78, H * 0.24, 190.0, (74, 86, 128))       # cool bloom, upper right
    floor = build_bloom(W * 0.16, H * 0.92, 240.0, (66, 38, 18))       # warm bounce, lower left
    vig_y, vig_x = vignette()
    pixels = bytearray(W * H * 3)

    for y in range(H):
        br, bg, bb = base[y]
        m = moon[y]
        f = floor[y]
        vy = vig_y[y]
        row = y * W * 3
        for x in range(W):
            v = vy * vig_x[x]
            r = (br + m[0] * m[3][x] + f[0] * f[3][x]) * v
            g = (bg + m[1] * m[3][x] + f[1] * f[3][x]) * v
            b = (bb + m[2] * m[3][x] + f[2] * f[3][x]) * v
            i = row + x * 3
            pixels[i] = 255 if r >= 255 else 0 if r < 0 else int(r)
            pixels[i + 1] = 255 if g >= 255 else 0 if g < 0 else int(g)
            pixels[i + 2] = 255 if b >= 255 else 0 if b < 0 else int(b)

    for (sx, sy, sigma, amp, tint) in stars():  # splat the point spread functions on top
        reach = max(2, int(sigma * 4))
        for dy in range(-reach, reach + 1):
            py = int(sy) + dy
            if py < 0 or py >= H:
                continue
            for dx in range(-reach, reach + 1):
                px = int(sx) + dx
                if px < 0 or px >= W:
                    continue
                d2 = (px + 0.5 - sx) ** 2 + (py + 0.5 - sy) ** 2
                a = amp * math.exp(-d2 / (2 * sigma * sigma))
                if a < 0.6:
                    continue
                i = (py * W + px) * 3
                for c in range(3):
                    v = pixels[i + c] + a * tint[c] / 255.0
                    pixels[i + c] = 255 if v >= 255 else int(v)

    # 4x4 Bayer dither, +/- 0.5: banding-free gradients that still compress, because every 2x2 block
    # of neighbours picks from the same sixteen thresholds rather than from independent noise.
    bayer = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
    for y in range(H):
        row = y * W * 3
        for x in range(W):
            t = (bayer[y & 3][x & 3] - 7.5) * 0.14
            i = row + x * 3
            for c in range(3):
                v = pixels[i + c] + t
                pixels[i + c] = 255 if v >= 255 else 0 if v < 0 else int(v)

    raw = b"".join(b"\x00" + pixels[y * W * 3:(y + 1) * W * 3] for y in range(H))

    def chunk(kind, payload):
        return (struct.pack(">I", len(payload)) + kind + payload
                + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    with open(out, "wb") as handle:
        handle.write(png)
    print(f"{out}: {W}x{H}, {len(png) / 1024:.0f} KiB")


if __name__ == "__main__":
    main()
