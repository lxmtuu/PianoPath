#!/usr/bin/env python3
"""Reference implementation of the Keyflow keyboard shader, used to validate the maths.

The C# pipeline in Shading/ cannot be compiled in this sandbox (WPF only builds on Windows and no
.NET SDK is installed here), so this file ports Shading/PianoShaderScene.cs and
Shading/PianoKeyboardRenderer.cs line for line and renders a PNG of the result. It is a design and
algorithm check - it is NOT the WPF app and it is NOT a test of the shipped C# - but it proves the
camera mapping, the geometry layout, the shadow/AO/BRDF terms and the emissive note lights behave
the way the C# describes, and it gives a picture of the intended look.

Output goes to tools/out/ which is not committed.
"""
import math
import os
import struct
import sys
import zlib

W = float(sys.argv[1]) if len(sys.argv) > 1 else 900.0
H = float(sys.argv[2]) if len(sys.argv) > 2 else 210.0
SCALE = float(sys.argv[3]) if len(sys.argv) > 3 else 0.5

WHITE_KEYS = 52
WORLD_WIDTH = 52.0
WHITE_GAP = 0.028
BLACK_WIDTH = 0.52
LIT_REACH = 4.2


def is_black(pitch):
    return pitch % 12 in (1, 3, 6, 8, 10)


def black_key_offset(pitch):
    m = pitch % 12
    if m == 1:
        return -0.08
    if m == 3:
        return 0.08
    if m == 6:
        return -0.10
    if m == 8:
        return 0.00
    if m == 10:
        return 0.10
    return 0.0


WHITES_BELOW = []
_count = 0
for _p in range(128):
    WHITES_BELOW.append(_count)
    if 21 <= _p < 109 and not is_black(_p):
        _count += 1
WHITE_PITCH_AT = [p for p in range(21, 109) if not is_black(p)]
BLACK_AT_BOUNDARY = [0] * (WHITE_KEYS + 1)
for _p in range(21, 109):
    if is_black(_p):
        BLACK_AT_BOUNDARY[WHITES_BELOW[_p]] = _p
KEY_CENTER_X = [(WHITES_BELOW[p] + (black_key_offset(p) if is_black(p) else 0.5)) * (WORLD_WIDTH / WHITE_KEYS) for p in range(128)]


# ---- scene (mirrors PianoShaderScene.From with the Neon Violet preset defaults) -----------------
class Scene:
    BandWidth = int(W)
    BandHeight = int(H)
    RenderScale = SCALE
    CameraHeight = 4.6 + 0.48 * 7.4
    CameraDistance = 9.4 + 0.48 * 6.6
    BedFraction = 0.1 + 0.48 * 0.13
    WhiteDepth = 6.0
    BlackDepth = 3.9
    WhiteLip = 0.45
    BlackHeight = 0.52
    BedDepth = 2.6
    BedDrop = 0.08
    KeyBedDepth = 0.9
    FallboardHeight = 2.4
    PressDepth = 0.1 + 0.40 * 0.3
    WhiteRoughness = 0.62 - 0.72 * 0.5
    BlackRoughness = 0.42 - 0.72 * 0.36
    BedRoughness = 0.6
    FallboardRoughness = 0.14
    Specular = 0.5
    KeyLightIntensity = 0.92 * 3.4
    ShadowStrength = 0.78
    Occlusion = 0.70
    RimIntensity = 0.62 * 1.6
    EmissiveIntensity = 0.85 * 2.2
    Exposure = 1.05
    Saturation = 1.0
    Contrast = 1.0
    Filmic = True
    LightSize = 0.35 + 0.78 * 0.6
    EdgeDarkening = 0.55
    AmbientIntensity = 1.0
    ShadowSamples = 3
    OcclusionSamples = 2
    KeyLightTravel = (-0.30, -0.90, 0.32)
    FillTravel = (0.55, -0.42, 0.72)
    RimTravel = (0.0, -0.35, 0.94)

    @property
    def SBack(self):
        return self.CameraHeight / (self.WhiteDepth + self.CameraDistance)

    @property
    def SLip(self):
        return (self.CameraHeight + self.WhiteLip) / self.CameraDistance

    @property
    def STop(self):
        f = self.BedFraction
        return (self.SBack - f * self.SLip) / (1 - f)


def srgb_to_linear(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin_to_rgb(c):
    v = c * 12.92 if c <= 0.0031308 else 1.055 * max(c, 0.0) ** (1 / 2.4) - 0.055
    return max(0, min(255, int(round(v * 255))))


def lin(rgb):
    return (srgb_to_linear(rgb[0]), srgb_to_linear(rgb[1]), srgb_to_linear(rgb[2]))


def aces(v):
    v = max(v, 0.0)
    return max(0.0, min(1.0, v * (2.51 * v + 0.03) / (v * (2.43 * v + 0.59) + 0.14)))


def norm(v):
    l = math.sqrt(v[0] ** 2 + v[1] ** 2 + v[2] ** 2)
    return (v[0] / l, v[1] / l, v[2] / l) if l > 1e-9 else (0.0, 1.0, 0.0)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def mul(a, b):
    return (a[0] * b[0], a[1] * b[1], a[2] * b[2])


def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def smoothstep(e0, e1, x):
    if abs(e1 - e0) < 1e-9:
        return 0.0 if x < e0 else 1.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def hash_noise(x, y, seed):
    n = (x * 1619 + y * 31337 + seed * 6971) & 0xFFFFFFFF
    n = ((n << 13) & 0xFFFFFFFF) ^ n
    n = (n * ((n * n * 15731 & 0xFFFFFFFF) + 789221 & 0xFFFFFFFF) + 1376312589) & 0xFFFFFFFF
    return (n & 0x7FFFFFFF) / 0x7FFFFFFF


BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]


def dither(x, y):
    return (BAYER[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0 - 0.5


def d_ggx(roughness, noh):
    a = max(roughness * roughness, 1e-4)
    a2 = a * a
    d = noh * noh * (a2 - 1) + 1
    return a2 / (math.pi * d * d + 1e-7)


def g_smith(roughness, nov, nol):
    k = max(roughness * roughness, 1e-4) * 0.5
    return (nov / (nov * (1 - k) + k)) * (nol / (nol * (1 - k) + k))


def fresnel(cos_theta, f0):
    p = (1 - max(0.0, min(1.0, cos_theta))) ** 5
    return (f0 + (1 - f0) * p,) * 3


def evaluate(albedo, roughness, f0, n, v, l):
    h = norm(add(v, l))
    nov = max(dot(n, v), 1e-4)
    nol = max(dot(n, l), 0.0)
    noh = max(dot(n, h), 0.0)
    voh = max(dot(v, h), 0.0)
    k = d_ggx(roughness, noh) * g_smith(roughness, nov, nol) / (4 * nov * nol + 1e-4)
    f = fresnel(voh, f0)[0]
    kd = 1.0 - f
    return tuple(albedo[i] * kd / math.pi + f * k for i in range(3))


EPS = 1e-4
SHADOW_REACH = 14.0


def intersect_slabs(oy, od, dy, dd, y0, y1, d0, d1):
    ty0 = (y0 - oy) / dy
    ty1 = (y1 - oy) / dy
    td0 = (d0 - od) / dd
    td1 = (d1 - od) / dd
    enter = max(min(ty0, ty1), min(td0, td1))
    exit_ = min(max(ty0, ty1), max(td0, td1))
    if enter > exit_ or exit_ <= EPS:
        return None
    return max(enter, EPS), min(ty0, ty1) >= min(td0, td1)


def intersect_box(o, d, x0, x1, y0, y1, d0, d1, max_t):
    enter, exit_ = EPS, max_t
    for comp, lo, hi in ((0, x0, x1), (1, y0, y1), (2, d0, d1)):
        dc = d[comp]
        oc = o[comp]
        if abs(dc) < 1e-9:
            if oc < lo or oc > hi:
                return False
        else:
            inv = 1.0 / dc
            t0 = (lo - oc) * inv
            t1 = (hi - oc) * inv
            if t0 > t1:
                t0, t1 = t1, t0
            enter = max(enter, t0)
            exit_ = min(exit_, t1)
            if enter > exit_:
                return False
    return True


class Renderer:
    def __init__(self, scene, lit, focus=-1):
        self.s = scene
        self.down = {p: True for p, _, _ in lit}
        self.tint = {p: lin(c) for p, c, _ in lit}
        self.amount = {p: a for p, _, a in lit}
        ordered = sorted(lit, key=lambda e: KEY_CENTER_X[e[0]])
        self.lit_x = [KEY_CENTER_X[e[0]] for e in ordered]
        self.lit_pos = []
        self.lit_col = []
        self.lit_amt = []
        for p, c, a in ordered:
            black = is_black(p)
            surface = (scene.BlackHeight if black else 0.0) - scene.PressDepth
            self.lit_pos.append((KEY_CENTER_X[p], surface + (0.3 if black else 0.38),
                                 (scene.BlackDepth if black else scene.WhiteDepth) * 0.5))
            self.lit_col.append(lin(c))
            self.lit_amt.append(a * scene.EmissiveIntensity)
        self.focus = focus
        self.key_light = scale(lin((255, 246, 232)), scene.KeyLightIntensity)
        self.fill = scale(lin((240, 92, 255)), 0.45)
        self.rim = scale(lin((198, 110, 255)), scene.RimIntensity)
        self.sky = scale(lin((70, 78, 110)), scene.AmbientIntensity)
        self.ground = scale(lin((12, 11, 18)), scene.AmbientIntensity)
        self.white = lin((238, 235, 228))
        self.black = lin((26, 26, 32))
        self.keybed = lin((14, 13, 20))
        self.bed = lin((22, 21, 30))
        self.board = lin((12, 12, 18))
        self.to_key = norm(scale(scene.KeyLightTravel, -1))
        self.to_fill = norm(scale(scene.FillTravel, -1))
        self.to_rim = norm(scale(scene.RimTravel, -1))
        f0v = scene.Specular * 0.16
        self.f0 = f0v
        ref = (0, 1, 0) if abs(self.to_key[1]) < 0.95 else (1, 0, 0)
        self.side_a = norm(cross(ref, self.to_key))
        self.side_b = norm(cross(self.to_key, self.side_a))
        black_focus = focus > 0 and is_black(focus)
        self.focus_center = (KEY_CENTER_X[focus] if focus > 0 else -99,
                             (scene.BlackHeight if black_focus else 0.0) - scene.PressDepth,
                             (scene.BlackDepth if black_focus else scene.WhiteDepth) * 0.55)

    def shadowed(self, p, n, to_light):
        s = self.s
        o = add(p, scale(n, 0.004))
        lo = min(o[0], o[0] + to_light[0] * SHADOW_REACH) - BLACK_WIDTH
        hi = max(o[0], o[0] + to_light[0] * SHADOW_REACH) + BLACK_WIDTH
        for b in range(max(0, math.floor(lo)), min(WHITE_KEYS, math.ceil(hi)) + 1):
            pitch = BLACK_AT_BOUNDARY[b]
            if pitch <= 0:
                continue
            sink = s.PressDepth if pitch in self.down else 0.0
            bc = KEY_CENTER_X[pitch]
            if intersect_box(o, to_light, bc - BLACK_WIDTH * 0.5, bc + BLACK_WIDTH * 0.5,
                             -sink, s.BlackHeight - sink, 0, s.BlackDepth, SHADOW_REACH):
                return True
        bd = s.WhiteDepth + s.BedDepth
        return intersect_box(o, to_light, -1e6, 1e6, -s.BedDrop, s.FallboardHeight, bd, bd + 0.6, SHADOW_REACH)

    def occlusion(self, p, n, ax, ay):
        s = self.s
        if s.OcclusionSamples <= 0 or s.Occlusion <= 0:
            return 1.0
        tangent = (1.0, 0.0, 0.0)
        bitangent = (0, 0, 1) if n[1] > 0.5 else (0, 1, 0)
        o = add(p, scale(n, 0.004))
        hits = 0
        for i in range(s.OcclusionSamples):
            r = math.sqrt(hash_noise(ax, ay, 500 + i * 2))
            ang = hash_noise(ax, ay, 501 + i * 2) * math.pi * 2
            h = math.sqrt(max(0.0, 1 - r * r))
            d = norm(add(add(scale(tangent, r * math.cos(ang)), scale(bitangent, r * math.sin(ang))), scale(n, h)))
            lo = min(o[0], o[0] + d[0] * 0.85) - BLACK_WIDTH
            hi = max(o[0], o[0] + d[0] * 0.85) + BLACK_WIDTH
            found = False
            for b in range(max(0, math.floor(lo)), min(WHITE_KEYS, math.ceil(hi)) + 1):
                pitch = BLACK_AT_BOUNDARY[b]
                if pitch <= 0:
                    continue
                sink = s.PressDepth if pitch in self.down else 0.0
                bc = KEY_CENTER_X[pitch]
                if intersect_box(o, d, bc - BLACK_WIDTH * 0.5, bc + BLACK_WIDTH * 0.5,
                                 -sink, s.BlackHeight - sink, 0, s.BlackDepth, 0.85):
                    found = True
                    break
            bd = s.WhiteDepth + s.BedDepth
            if not found:
                found = intersect_box(o, d, -1e6, 1e6, -s.BedDrop, s.FallboardHeight, bd, bd + 0.6, 0.85)
            if found:
                hits += 1
        return 1 - s.Occlusion * (hits / s.OcclusionSamples)

    def shade(self, ax, ay, world_x, oy, od, dy, dd, lit_start, out, offset):
        s = self.s
        best = None
        material = -1
        pitch = -1
        top = False
        bx0 = bx1 = bd0 = bd1 = 0.0

        floor_x = math.floor(world_x)
        wi = int(max(0, min(WHITE_KEYS - 1, floor_x)))
        wp = WHITE_PITCH_AT[wi]
        sink = s.PressDepth if wp in self.down else 0.0
        wx0 = wi + WHITE_GAP * 0.5
        wx1 = wi + 1 - WHITE_GAP * 0.5
        if wx0 <= world_x <= wx1:
            hit = intersect_slabs(oy, od, dy, dd, -s.WhiteLip - sink, -sink, 0, s.WhiteDepth)
            if hit and hit[0] < (best[0] if best else 1e18):
                best, material, pitch, top = hit, 0, wp, hit[1]
                bx0, bx1, bd0, bd1 = wx0, wx1, 0.0, s.WhiteDepth

        b0 = int(max(0, min(WHITE_KEYS, floor_x)))
        b1 = int(max(0, min(WHITE_KEYS, floor_x + 1)))
        for b in range(b0, b1 + 1):
            bp = BLACK_AT_BOUNDARY[b]
            if bp > 0:
                bc = KEY_CENTER_X[bp]
                b_x0 = bc - BLACK_WIDTH * 0.5
                b_x1 = bc + BLACK_WIDTH * 0.5
                if b_x0 <= world_x <= b_x1:
                    bsink = s.PressDepth if bp in self.down else 0.0
                    hit = intersect_slabs(oy, od, dy, dd, -bsink, s.BlackHeight - bsink, 0, s.BlackDepth)
                    if hit and hit[0] < (best[0] if best else 1e18):
                        best, material, pitch, top = hit, 1, bp, hit[1]
                        bx0, bx1 = b_x0, b_x1
                        bd0, bd1 = 0.0, s.BlackDepth

        if material < 0:
            hit = intersect_slabs(oy, od, dy, dd, -1.4, -s.KeyBedDepth, -0.3, s.WhiteDepth)
            if hit:
                best, material, top = hit, 2, hit[1]
                bx0, bx1, bd0, bd1 = 0.0, WORLD_WIDTH, -0.3, s.WhiteDepth
        if material < 0:
            hit = intersect_slabs(oy, od, dy, dd, -1.4, -s.BedDrop, s.WhiteDepth, s.WhiteDepth + s.BedDepth)
            if hit:
                best, material, top = hit, 3, hit[1]
                bx0, bx1, bd0, bd1 = 0.0, WORLD_WIDTH, s.WhiteDepth, s.WhiteDepth + s.BedDepth
        if material < 0:
            bd = s.WhiteDepth + s.BedDepth
            hit = intersect_slabs(oy, od, dy, dd, -s.BedDrop, s.FallboardHeight, bd, bd + 0.6)
            if hit:
                best, material, top = hit, 4, hit[1]
                bx0, bx1, bd0, bd1 = 0.0, WORLD_WIDTH, bd, bd + 0.6
        if material < 0:
            out[offset:offset + 4] = (0, 0, 0, 255 if self.focus <= 0 else 0)
            return

        t = best[0]
        p = (world_x, oy + dy * t, od + dd * t)
        n = (0.0, 1.0, 0.0) if top else (0.0, 0.0, -1.0)
        if material in (0, 1) and top:
            bevel = 0.045 if material == 1 else 0.038
            ex = min(p[0] - bx0, bx1 - p[0])
            if ex < bevel:
                sign = -1.0 if p[0] < (bx0 + bx1) * 0.5 else 1.0
                fac = 1.0 - ex / bevel
                n = norm((n[0] + sign * fac * 0.36, n[1], n[2]))
            if p[2] < bevel:
                fac = 1.0 - p[2] / bevel
                n = norm((n[0], n[1], n[2] - fac * 0.44))
        v = norm((0.0, -dy, -dd))
        if material == 0:
            albedo, rough = self.white, s.WhiteRoughness
        elif material == 1:
            albedo, rough = self.black, s.BlackRoughness
        elif material == 2:
            albedo, rough = self.keybed, s.BedRoughness
        elif material == 3:
            albedo, rough = self.bed, s.BedRoughness
        else:
            albedo, rough = self.board, s.FallboardRoughness

        emissive = (0.0, 0.0, 0.0)
        if pitch > 0 and pitch in self.down:
            lc = self.tint[pitch]
            albedo = lerp3(albedo, lc, 0.5 if material == 1 else 0.34)
            emissive = scale(lc, s.EmissiveIntensity * self.amount[pitch])

        rad = [0.0, 0.0, 0.0]
        for i in range(max(1, s.ShadowSamples)):
            ja = (hash_noise(ax, ay, i * 2) - 0.5) * 2 * s.LightSize
            jb = (hash_noise(ax, ay, i * 2 + 1) - 0.5) * 2 * s.LightSize
            to_light = norm(add(add(self.to_key, scale(self.side_a, ja)), scale(self.side_b, jb)))
            ndl = dot(n, to_light)
            if ndl <= 0:
                continue
            vis = (1 - s.ShadowStrength) if self.shadowed(p, n, to_light) else 1.0
            b = evaluate(albedo, rough, self.f0, n, v, to_light)
            for c in range(3):
                rad[c] += b[c] * self.key_light[c] * ndl * vis
        rad = [c / max(1, s.ShadowSamples) for c in rad]

        fd = dot(n, self.to_fill)
        if fd > 0:
            b = evaluate(albedo, rough, self.f0, n, v, self.to_fill)
            for c in range(3):
                rad[c] += b[c] * self.fill[c] * fd
        rd = dot(n, self.to_rim)
        if rd > 0:
            b = evaluate(albedo, rough, self.f0, n, v, self.to_rim)
            f = rd ** 1.6
            for c in range(3):
                rad[c] += b[c] * self.rim[c] * f

        if s.RimIntensity > 0:
            to_halo = (0.0, 0.4 - p[1], s.WhiteDepth - p[2])
            hdist = math.sqrt(to_halo[0] ** 2 + to_halo[1] ** 2 + to_halo[2] ** 2)
            if hdist > 1e-4:
                hdir = (to_halo[0] / hdist, to_halo[1] / hdist, to_halo[2] / hdist)
                hndl = max(0.0, dot(n, hdir))
                if hndl > 0:
                    hatten = 1.0 / (1.0 + hdist * hdist * 0.12)
                    hb = evaluate(albedo, rough, self.f0, n, v, hdir)
                    for c in range(3):
                        rad[c] += hb[c] * self.rim[c] * (hndl * hatten * 1.25)

        occ = self.occlusion(p, n, ax, ay)
        sky_f = max(0.0, min(1.0, n[1] * 0.5 + 0.5))
        amb = lerp3(self.ground, self.sky, sky_f)
        for c in range(3):
            rad[c] += albedo[c] * amb[c] * s.AmbientIntensity * occ

        incident = (-v[0], -v[1], -v[2])
        refl = (incident[0] - n[0] * 2 * dot(incident, n), incident[1] - n[1] * 2 * dot(incident, n), incident[2] - n[2] * 2 * dot(incident, n))
        rsky = max(0.0, min(1.0, refl[1] * 0.5 + 0.5))
        env = lerp3(self.ground, self.sky, rsky)
        fr = fresnel(dot(n, v), self.f0)[0] * (1 - rough) * occ
        for c in range(3):
            rad[c] += env[c] * 1.1 * fr

        k = lit_start
        while k < len(self.lit_x):
            if self.lit_x[k] - world_x > LIT_REACH:
                break
            delta = sub(self.lit_pos[k], p)
            d2 = delta[0] ** 2 + delta[1] ** 2 + delta[2] ** 2
            if d2 > 1e-6:
                dist = math.sqrt(d2)
                tl = (delta[0] / dist, delta[1] / dist, delta[2] / dist)
                ndl = dot(n, tl)
                if ndl > 0:
                    f = ndl * self.lit_amt[k] / (1 + d2 * 0.55)
                    for c in range(3):
                        rad[c] += albedo[c] * self.lit_col[k][c] * f
            k += 1

        for c in range(3):
            rad[c] += emissive[c]

        if material in (0, 1):
            ex = min(p[0] - bx0, bx1 - p[0])
            ed = min(p[2] - bd0, bd1 - p[2])
            f1 = 1 - s.EdgeDarkening * s.Occlusion * (1 - smoothstep(0, 0.05, ex)) * 0.9
            f2 = 1 - s.EdgeDarkening * (1 - smoothstep(0, 0.1, ed)) * 0.3
            for c in range(3):
                rad[c] *= f1 * f2
            if top and ed < 0.2:
                boost = 0.06 * (1 - ed / 0.2)
                for c in range(3):
                    rad[c] += self.key_light[c] * boost

        rad = [c * s.Exposure for c in rad]
        if s.Filmic:
            rad = [aces(c) for c in rad]
        else:
            rad = [max(0.0, min(1.0, c)) for c in rad]
        if abs(s.Saturation - 1) > 1e-4:
            luma = 0.2126 * rad[0] + 0.7152 * rad[1] + 0.0722 * rad[2]
            rad = [luma + (c - luma) * s.Saturation for c in rad]
        if abs(s.Contrast - 1) > 1e-4:
            rad = [(c - 0.5) * s.Contrast + 0.5 for c in rad]
        d = dither(ax, ay)
        rgb = [lin_to_rgb(c + d / 255.0) for c in rad]

        influence = 1.0
        if self.focus > 0 and pitch != self.focus:
            if material in (0, 1):
                influence = 0.0
            else:
                dx = p[0] - self.focus_center[0]
                dyv = p[1] - self.focus_center[1]
                dz = p[2] - self.focus_center[2]
                influence = 1 - smoothstep(0.2, 2.4, math.sqrt(dx * dx + dyv * dyv + dz * dz))
        if influence <= 0:
            out[offset:offset + 4] = (0, 0, 0, 0)
            return
        a = max(0, min(255, int(round(influence * 255))))
        blend = a / 255.0
        out[offset:offset + 4] = (int(rgb[2] * blend), int(rgb[1] * blend), int(rgb[0] * blend), a)

    def render(self, view_x, view_y, view_w, view_h):
        s = self.s
        pw = max(1, int(math.ceil(view_w * s.RenderScale)))
        ph = max(1, int(math.ceil(view_h * s.RenderScale)))
        rows = [bytearray(pw * 4) for _ in range(ph)]
        for row in range(ph):
            vy = view_y + (row + 0.5) / s.RenderScale
            sc = max(s.STop + vy / s.BandHeight * (s.SLip - s.STop), 1e-4)
            inv = 1.0 / sc
            length = math.sqrt(1 + inv * inv)
            dy = -1.0 / length
            dd = inv / length
            lit_start = 0
            line = rows[row]
            for col in range(pw):
                vx = view_x + (col + 0.5) / s.RenderScale
                world_x = vx / s.BandWidth * WORLD_WIDTH
                while lit_start < len(self.lit_x) and self.lit_x[lit_start] < world_x - LIT_REACH:
                    lit_start += 1
                self.shade(view_x + col, view_y + row, world_x, s.CameraHeight, -s.CameraDistance,
                           dy, dd, lit_start, line, col * 4)
        return pw, ph, rows


def write_png(path, pw, ph, rows, alpha_channel=True):
    raw = bytearray()
    for line in rows:
        raw.append(0)
        for x in range(pw):
            b, g, r, a = line[x * 4], line[x * 4 + 1], line[x * 4 + 2], line[x * 4 + 3]
            # pre-multiplied overlay tiles are composited over the base bake before display
            if alpha_channel and a < 255:
                f = a / 255.0 if a else 0.0
                r = int(r / f) if f else 0
                g = int(g / f) if f else 0
                b = int(b / f) if f else 0
            raw += bytes((min(255, r), min(255, g), min(255, b)))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", pw, ph, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as handle:
        handle.write(png)


def main():
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    os.makedirs(out_dir, exist_ok=True)
    scene = Scene()
    print(f"band {scene.BandWidth}x{scene.BandHeight}, render scale {scene.RenderScale}, "
          f"camera h={scene.CameraHeight:.2f} d={scene.CameraDistance:.2f}")
    print(f"sTop={scene.STop:.4f} sBack={scene.SBack:.4f} sLip={scene.SLip:.4f}")

    base = Renderer(scene, [])
    pw, ph, rows = base.render(0, 0, scene.BandWidth, scene.BandHeight)
    write_png(os.path.join(out_dir, "keyboard-unlit.png"), pw, ph, rows)

    lit = [(60, (255, 64, 96), 1.0), (64, (96, 220, 255), 1.0), (67, (255, 200, 90), 1.0),
           (72, (150, 120, 255), 1.0), (58, (120, 255, 180), 1.0)]
    overlay = Renderer(scene, lit, focus=-1)
    _, _, lit_rows = overlay.render(0, 0, scene.BandWidth, scene.BandHeight)
    write_png(os.path.join(out_dir, "keyboard-lit.png"), pw, ph, lit_rows)
    print("wrote tools/out/keyboard-unlit.png and tools/out/keyboard-lit.png")

    # quick sanity numbers
    def band(rows, from_world, to_world):
        x0 = int(from_world / WORLD_WIDTH * pw)
        x1 = int(to_world / WORLD_WIDTH * pw)
        total = count = 0
        for y in range(ph // 3, ph * 2 // 3):
            line = rows[y]
            for x in range(x0, x1):
                total += (line[x * 4] + line[x * 4 + 1] * 2 + line[x * 4 + 2]) // 4
                count += 1
        return total / max(1, count)

    ebony = band(rows, 24.75, 25.25)
    ivory = band(rows, 25.45, 25.95)
    print(f"ebony column mean luma = {ebony:.1f}, ivory column mean luma = {ivory:.1f}, ratio = {ebony / max(1e-6, ivory):.3f}")


if __name__ == "__main__":
    main()
