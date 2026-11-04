"""
Lucky sprite renderer.

Draws the husky with signed-distance fields (SDF): smooth-unioned soft shapes,
normal-based lighting (diffuse + specular + rim), procedural fur grain anchored
to each body part, ambient occlusion between parts, and a baked white sticker
outline. Frames are rendered at 4x and stored at 2x of the 130x120 logical canvas.

Usage:  python render_sprites.py <out_dir> [--preview preview.png]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

R = 4                       # render scale (px per logical unit)
CW, CH = 130, 120           # logical canvas, ground at y = 116
W, H = CW * R, CH * R
OUT_SCALE = 2               # stored sprite scale
ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
X = (xs + 0.5) / R
Y = (ys + 0.5) / R

L = np.array([-0.45, -0.7, 0.56], np.float32); L /= np.linalg.norm(L)
HV = L + np.array([0, 0, 1], np.float32); HV /= np.linalg.norm(HV)


def C(*v):
    return np.array(v, np.float32)


FUR_D = C(60, 74, 108)
FUR_M = C(114, 131, 166)
FUR_L = C(150, 166, 198)
WHITE = C(247, 250, 255)
PINK = C(250, 186, 206)
NOSE = C(30, 34, 48)
LINE = C(48, 54, 78)
AQUA = C(38, 186, 208)
SILVER = C(222, 230, 244)
TONGUE = C(240, 120, 148)
MOUTH = C(112, 42, 64)


# ---------------------------------------------------------------- SDF primitives
def sd_ellipse(cx, cy, rx, ry, ang=0.0):
    x, y = X - cx, Y - cy
    if ang:
        c, s = math.cos(ang), math.sin(ang)
        x, y = x * c + y * s, -x * s + y * c
    k = np.sqrt((x / rx) ** 2 + (y / ry) ** 2)
    return (k - 1) * min(rx, ry)


def sd_capsule(ax, ay, bx, by, ra, rb):
    pax, pay = X - ax, Y - ay
    bax, bay = bx - ax, by - ay
    h = np.clip((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-6), 0, 1)
    return np.sqrt((pax - bax * h) ** 2 + (pay - bay * h) ** 2) - (ra + (rb - ra) * h)


def sd_tri(p0, p1, p2):
    p0, p1, p2 = [np.array(p, np.float32) for p in (p0, p1, p2)]
    e0, e1, e2 = p1 - p0, p2 - p1, p0 - p2
    v0x, v0y = X - p0[0], Y - p0[1]
    v1x, v1y = X - p1[0], Y - p1[1]
    v2x, v2y = X - p2[0], Y - p2[1]

    def seg(vx, vy, e):
        h = np.clip((vx * e[0] + vy * e[1]) / (e @ e), 0, 1)
        return vx - e[0] * h, vy - e[1] * h

    q0, q1, q2 = seg(v0x, v0y, e0), seg(v1x, v1y, e1), seg(v2x, v2y, e2)
    s = np.sign(e0[0] * e2[1] - e0[1] * e2[0])
    d0 = np.minimum(np.minimum(q0[0] ** 2 + q0[1] ** 2, q1[0] ** 2 + q1[1] ** 2), q2[0] ** 2 + q2[1] ** 2)
    c0 = s * (v0x * e0[1] - v0y * e0[0])
    c1 = s * (v1x * e1[1] - v1y * e1[0])
    c2 = s * (v2x * e2[1] - v2y * e2[0])
    inside = np.minimum(np.minimum(c0, c1), c2)
    return -np.sqrt(d0) * np.sign(inside)


def sd_polyline(pts, r):
    d = None
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        di = sd_capsule(ax, ay, bx, by, r, r)
        d = di if d is None else np.minimum(d, di)
    return d


def arc_pts(cx, cy, rx, ry, a0, a1, n=14):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / (n - 1))),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / (n - 1)))) for i in range(n)]


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)


def vnoise(x, y, seed=0.0):
    xi, yi = np.floor(x), np.floor(y)
    xf, yf = x - xi, y - yi
    u, v = xf * xf * (3 - 2 * xf), yf * yf * (3 - 2 * yf)

    def h(ix, iy):
        return np.abs(np.sin(ix * 127.1 + iy * 311.7 + seed * 74.7) * 43758.5453) % 1.0

    a, b = h(xi, yi), h(xi + 1, yi)
    c, d = h(xi, yi + 1), h(xi + 1, yi + 1)
    return (a + (b - a) * u) + ((c + (d - c) * u) - (a + (b - a) * u)) * v


def fur_noise(cx, cy, seed, freq=1.3):
    """Fur grain anchored to a part's local coordinates (doesn't swim between frames)."""
    lx, ly = (X - cx) * freq, (Y - cy) * freq * 0.55
    return 0.65 * vnoise(lx, ly, seed) + 0.35 * vnoise(lx * 2.3, ly * 2.3, seed + 3)


def soft_mask(d, soft=0.6):
    return np.clip(0.5 - d / soft, 0, 1)


def blur(a, r):
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(r))
    return np.asarray(im, np.float32) / 255


# ---------------------------------------------------------------- shading / compositing
class Canvas:
    def __init__(self):
        self.p = np.zeros((H, W, 3), np.float32)   # premultiplied rgb
        self.a = np.zeros((H, W), np.float32)

    def occlude(self, alpha, strength=0.3, radius=7, off=(3, 5)):
        sh = np.roll(np.roll(alpha, off[1], axis=0), off[0], axis=1)
        sh = blur(sh, radius)
        self.p *= (1 - strength * sh)[..., None]

    def over(self, rgb, a):
        self.p = rgb * a[..., None] + self.p * (1 - a[..., None])
        self.a = a + self.a * (1 - a)

    def part(self, d, base, round_r, gloss=0.06, shin=18, rim=0.35, grain=None, ao=0.28, alpha_mul=1.0):
        t = np.clip(-d / round_r, 0, 1)
        z = round_r * np.sqrt(np.clip(1 - (1 - t) ** 2, 0, 1))
        gy, gx = np.gradient(z)
        nx, ny = -gx * R, -gy * R
        nz = np.ones_like(nx)
        inv = 1 / np.sqrt(nx * nx + ny * ny + nz * nz)
        nx, ny, nz = nx * inv, ny * inv, nz * inv
        diff = np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0, 1)
        spec = np.clip(nx * HV[0] + ny * HV[1] + nz * HV[2], 0, 1) ** shin * gloss
        b = base if base.ndim == 3 else np.broadcast_to(base, (H, W, 3))
        col = b * (0.5 + 0.62 * diff)[..., None]
        if grain is not None:
            col = col * (0.955 + 0.09 * grain)[..., None]
        rim_side = np.clip(-nx * 0.7 - ny * 0.7 + 0.15, 0, 1)
        col = col + C(175, 210, 255) * ((1 - nz) ** 2.2 * rim * rim_side)[..., None]
        col = col + 255 * spec[..., None]
        a = np.clip(0.5 - d * R, 0, 1) * alpha_mul
        if ao > 0:
            self.occlude(a, ao)
        self.over(np.clip(col, 0, 255), a)
        return a

    def flat(self, d, rgb, soft=1.0, alpha_mul=1.0):
        a = np.clip(0.5 - d * R / soft, 0, 1) * alpha_mul
        self.over(np.broadcast_to(rgb, (H, W, 3)), a)


def mix(a, b, t):
    t = np.clip(t, 0, 1)[..., None] if np.ndim(t) else t
    return a + (b - a) * t


# ---------------------------------------------------------------- body parts
def leg(cv, hx, hy, fx, fy, far):
    d = sd_capsule(hx, hy, fx, fy - 2.5, 5.6, 4.9)
    d = d + (fur_noise(hx, hy, 11) - 0.5) * 0.5
    white = soft_mask(-(Y - (hy + 7.5)) + (fur_noise(hx, hy, 12, 2) - 0.5) * 2.0, 1.2)
    base = mix(np.broadcast_to(FUR_M, (H, W, 3)), WHITE, white)
    if far:
        base = base * 0.8
    cv.part(d, base, 5.0, grain=fur_noise(hx, hy, 13, 2.2), ao=0.2)
    dp = sd_ellipse(fx + 1.2, fy - 1.6, 6.4, 3.6)
    cv.part(dp, WHITE * (0.82 if far else 1.0), 3.0, gloss=0.05, ao=0.18)
    for k in (-1.6, 1.8):
        cv.flat(sd_capsule(fx + 1.2 + k, fy - 2.6, fx + 1.2 + k, fy - 0.8, 0.32, 0.32), C(130, 140, 170), soft=1.2, alpha_mul=0.8)


def tail(cv, pts, w, wag_seed=0):
    (ax, ay), (bx, by), (cx_, cy_), (dx, dy) = pts
    n = 16
    d = None
    centers = []
    for i in range(n):
        t = i / (n - 1); u = 1 - t
        px = u ** 3 * ax + 3 * u * u * t * bx + 3 * u * t * t * cx_ + t ** 3 * dx
        py = u ** 3 * ay + 3 * u * u * t * by + 3 * u * t * t * cy_ + t ** 3 * dy
        r = w * 0.5 * (0.75 + 0.5 * math.sin(math.pi * min(1, t * 1.05)))
        if t > 0.86:
            r = max(r, w * 0.6)
        centers.append((px, py, r))
        di = sd_ellipse(px, py, r, r)
        d = di if d is None else smin(d, di, 2.5)
    d = d + (fur_noise(ax, ay, 21, 1.8) - 0.5) * 1.1
    tx, ty, tr = centers[-1]
    tip = soft_mask(sd_ellipse(tx, ty, tr * 1.8, tr * 1.8) + (fur_noise(tx, ty, 22, 2) - 0.5) * 3, 1.5)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, soft_mask(Y - (ay - 6), 12) * 0.0), WHITE, tip)
    cv.part(d, base, w * 0.55, grain=fur_noise(ax, ay, 23, 2.4), ao=0.22)


def collar(cv, cx, cy, rx, ry):
    ring = np.abs(sd_ellipse(cx, cy, rx, ry)) - 1.35
    ring = np.maximum(ring, (cy - 0.5) - Y)
    cv.part(ring, AQUA, 1.4, gloss=0.35, shin=30, ao=0.25)
    tx, ty = cx + 1.5, cy + ry + 3.6
    cv.part(sd_ellipse(tx, ty, 3.4, 3.4), SILVER, 3.0, gloss=0.6, shin=40, ao=0.25)
    for (ox, oy, rr) in ((0, 0.9, 1.1), (-1.2, -0.6, 0.45), (0, -1.1, 0.45), (1.2, -0.6, 0.45)):
        cv.flat(sd_ellipse(tx + ox, ty + oy, rr, rr * 0.9), C(150, 162, 190), soft=1.2, alpha_mul=0.7)


def eye(cv, ex, ey, e, mode):
    if mode in ('happy', 'closed', 'blink'):
        if mode == 'happy':
            pts = arc_pts(ex, ey + e * 0.35, e * 0.95, e * 0.95, 200, 340)
        elif mode == 'closed':
            pts = arc_pts(ex, ey - e * 0.25, e * 0.9, e * 0.75, 20, 160)
        else:
            pts = [(ex - e, ey + 0.2), (ex + e, ey + 0.2)]
        cv.flat(sd_polyline(pts, 0.75), LINE, soft=1.1)
        if mode == 'closed':
            for a in (40, 80, 120):
                px, py = ex + e * 0.9 * math.cos(math.radians(a)), ey - e * 0.25 + e * 0.75 * math.sin(math.radians(a))
                cv.flat(sd_capsule(px, py, px + (a - 80) * 0.02, py + 1.3, 0.3, 0.25), LINE, soft=1.1, alpha_mul=0.7)
        return
    rx, ry = e, e * 1.12
    d = sd_ellipse(ex, ey, rx, ry)
    tt = np.clip((Y - (ey - ry)) / (2 * ry), 0, 1)
    top, mid, bot = C(20, 52, 128), C(48, 132, 230), C(158, 226, 255)
    col = np.where((tt < 0.5)[..., None], mix(top, mid, tt * 2), mix(mid, bot, (tt - 0.5) * 2))
    col = col * (0.72 + 0.28 * np.clip(-d * R / 3.5, 0, 1))[..., None]
    a = np.clip(0.5 - d * R, 0, 1)
    cv.over(col, a)
    pr = e * (0.62 if mode == 'wide' else 0.5)
    cv.flat(sd_ellipse(ex + e * 0.06, ey + e * 0.1, pr, pr * 1.08), C(12, 16, 30))
    cv.flat(sd_polyline(arc_pts(ex, ey + e * 0.05, e * 0.72, e * 0.8, 35, 145), 0.35), C(160, 255, 245), soft=1.3, alpha_mul=0.65)
    cv.flat(sd_ellipse(ex - e * 0.38, ey - e * 0.44, e * 0.36, e * 0.34), WHITE)
    cv.flat(sd_ellipse(ex + e * 0.36, ey + e * 0.36, e * 0.15, e * 0.15), WHITE, alpha_mul=0.9)
    lid = arc_pts(ex, ey + e * 0.05, e * 1.08, e * 1.2, 196, 344)
    lid.append((ex + e * 1.32, ey - e * 0.62))
    cv.flat(sd_polyline(lid, 0.62), LINE, soft=1.0)


def head(cv, cx, cy, r, eyes='open', mouth='neutral', ears_back=0.0, tilt=0.0):
    eb = ears_back
    # ears
    for far, (p0, p1, p2) in ((True, ((cx - r * 0.92, cy - r * 0.2), (cx - r * 0.66 - eb * 4, cy - r * 1.46 + eb * 6), (cx - r * 0.06, cy - r * 0.8))),
                              (False, ((cx + 0.0, cy - r * 0.88), (cx + r * 0.5 - eb * 5, cy - r * 1.52 + eb * 6), (cx + r * 0.9, cy - r * 0.3)))):
        d = sd_tri(p0, p1, p2) - 2.2
        base = mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, 0.55)
        cv.part(d, base * (0.88 if far else 1.0), 4.5, grain=fur_noise(p1[0], p1[1], 31, 2), ao=0.15)
        gx_, gy_ = (p0[0] + p1[0] + p2[0]) / 3, (p0[1] + p1[1] + p2[1]) / 3 + 2
        q = [(gx_ + (px - gx_) * 0.58, gy_ + (py - gy_) * 0.58) for px, py in (p0, p1, p2)]
        di = sd_tri(*q) - 1.2
        pin = mix(PINK, C(232, 150, 184), np.clip((Y - q[1][1]) / 14, 0, 1))
        cv.part(di + (fur_noise(gx_, gy_, 33, 3) - 0.5) * 0.6, pin, 2.5, ao=0.0, rim=0.1)
        fl = soft_mask(sd_ellipse(gx_, gy_ + 5, 4.5, 3.2) + (fur_noise(gx_, gy_, 34, 3.5) - 0.5) * 3.2, 1.0)
        cv.over(np.broadcast_to(WHITE, (H, W, 3)), fl * np.clip(0.5 - di * R, 0, 1) * 0.9)
    # head base with fluffy cheeks
    d = sd_ellipse(cx, cy, r * 1.02, r * 0.93)
    d = smin(d, sd_ellipse(cx - r * 0.8, cy + r * 0.36, r * 0.36, r * 0.3), 3)
    d = smin(d, sd_ellipse(cx + r * 0.6, cy + r * 0.44, r * 0.32, r * 0.28), 3)
    fluff = np.clip((Y - cy) / r, 0, 1) * 1.5 + 0.25
    d = d + (fur_noise(cx, cy, 41, 1.6) - 0.5) * fluff * 0.75
    white = sd_ellipse(cx, cy + r * 0.52, r * 0.98, r * 0.56)
    white = np.minimum(white, sd_ellipse(cx - r * 0.38, cy - r * 0.06, r * 0.31, r * 0.29))
    white = np.minimum(white, sd_ellipse(cx + r * 0.37, cy - r * 0.08, r * 0.29, r * 0.27))
    white = np.minimum(white, sd_ellipse(cx - r * 0.02, cy - r * 0.42, r * 0.085, r * 0.44))
    wm = soft_mask(white + (fur_noise(cx, cy, 42, 2.2) - 0.5) * 1.6, 0.9)
    cap = np.clip((cy - Y) / r * 1.3 + 0.35, 0, 1)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, cap * 0.75), WHITE, wm)
    cv.part(d, base, r * 0.75, gloss=0.05, grain=fur_noise(cx, cy, 43, 2.6), ao=0.3)
    # blush
    cv.flat(sd_ellipse(cx - r * 0.66, cy + r * 0.3, r * 0.2, r * 0.1), C(255, 150, 185), soft=6, alpha_mul=0.55)
    cv.flat(sd_ellipse(cx + r * 0.16, cy + r * 0.32, r * 0.15, r * 0.08), C(255, 150, 185), soft=6, alpha_mul=0.45)
    # muzzle
    cv.part(sd_ellipse(cx + r * 0.8, cy + r * 0.36, r * 0.44, r * 0.3), WHITE, r * 0.3, gloss=0.08, ao=0.22)
    for k in range(3):
        cv.flat(sd_ellipse(cx + r * (0.66 + k * 0.1), cy + r * (0.45 - k * 0.03), 0.35, 0.35), C(140, 150, 180), alpha_mul=0.8)
    # nose
    cv.part(sd_ellipse(cx + r * 1.15, cy + r * 0.22, r * 0.17, r * 0.125), NOSE, r * 0.12, gloss=0.8, shin=45, ao=0.15, rim=0.1)
    # mouth
    if mouth == 'neutral':
        cv.flat(sd_polyline([(cx + r * 1.13, cy + r * 0.33), (cx + r * 1.09, cy + r * 0.45)], 0.45), LINE, soft=1.1)
        cv.flat(sd_polyline(arc_pts(cx + r * 1.0, cy + r * 0.43, r * 0.09, r * 0.08, 0, 180, 8), 0.45), LINE, soft=1.1)
        cv.flat(sd_polyline(arc_pts(cx + r * 1.18, cy + r * 0.43, r * 0.09, r * 0.08, 0, 180, 8), 0.45), LINE, soft=1.1)
    else:
        open_ = 1.0 if mouth == 'open' else (0.55 if mouth == 'chew1' else 0.2)
        cv.flat(sd_polyline([(cx + r * 1.13, cy + r * 0.33), (cx + r * 1.09, cy + r * 0.43)], 0.45), LINE, soft=1.1)
        cv.part(sd_ellipse(cx + r * 1.02, cy + r * 0.52, r * 0.2, r * 0.15 * open_ + 0.6), MOUTH, 1.5, ao=0.1, rim=0)
        if mouth == 'open':
            cv.part(sd_ellipse(cx + r * 1.03, cy + r * 0.68, r * 0.135, r * 0.2), TONGUE, r * 0.12, gloss=0.35, shin=25, ao=0.12)
            cv.flat(sd_capsule(cx + r * 1.03, cy + r * 0.6, cx + r * 1.03, cy + r * 0.78, 0.3, 0.2), C(200, 80, 110), alpha_mul=0.7)
    # eyes
    eye(cv, cx - r * 0.38, cy - r * 0.06, r * 0.21, eyes)
    eye(cv, cx + r * 0.36, cy - r * 0.08, r * 0.195, eyes)


def body(cv, cx, cy, bob=0.0, breath=0.0):
    d = sd_ellipse(cx, cy - bob, 30, 15.5 + breath)
    d = smin(d, sd_ellipse(cx + 22, cy - 3 - bob, 13, 14), 6)
    d = smin(d, sd_ellipse(cx - 22, cy - bob, 13, 14.5), 6)
    d = d + (fur_noise(cx, cy, 51, 1.5) - 0.5) * 0.7
    saddle = np.clip(((cy - bob) - Y) / 9 + 0.2, 0, 1)
    belly = soft_mask(sd_ellipse(cx + 2, cy + 10 - bob, 25, 8.5) + (fur_noise(cx, cy, 52, 2) - 0.5) * 2.2, 1.0)
    chest = soft_mask(sd_ellipse(cx + 25, cy + 2 - bob, 9, 12) + (fur_noise(cx, cy, 53, 2) - 0.5) * 2.2, 1.0)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, saddle * 0.85), WHITE, np.maximum(belly, chest))
    cv.part(d, base, 13, grain=fur_noise(cx, cy, 54, 2.2), ao=0.3)


def ruff(cv, cx, cy, rx, ry, seed=61):
    d = sd_ellipse(cx, cy, rx, ry) + (fur_noise(cx, cy, seed, 2.4) - 0.5) * 2.0
    cv.part(d, WHITE, min(rx, ry) * 0.8, grain=fur_noise(cx, cy, seed + 1, 3), ao=0.25)


# ---------------------------------------------------------------- poses
def pose_stand(phase=0.0, run=False, eyes='open', mouth='neutral', wag=0.0, bob_amp=1.4, eat=False, chew='chew1'):
    cv = Canvas()
    s = math.sin(phase * 2 * math.pi)
    amp = 6.5 if run else 4.0
    bob = abs(s) * bob_amp
    tail(cv, [(34, 86 - bob), (14, 80), (10 + wag * 0.4, 50 + wag), (30 + wag * 0.3, 50 + wag * 0.5)], 13)
    leg(cv, 40, 93 - bob, 40 + s * amp, 116, True)
    leg(cv, 78, 93 - bob, 78 - s * amp, 116, True)
    body(cv, 60, 90, bob)
    leg(cv, 50, 93 - bob, 50 - s * amp, 116, False)
    leg(cv, 88, 93 - bob, 88 + s * amp, 116, False)
    if eat:
        ruff(cv, 86, 86 - bob, 11, 12)
        collar(cv, 88, 85 - bob, 10, 4.2)
        head(cv, 95, 90 - bob, 20, eyes='happy', mouth=chew, tilt=0.3)
    else:
        ruff(cv, 85, 84 - bob, 11, 13)
        collar(cv, 87, 79 - bob, 11, 4.4)
        head(cv, 92, 56 - bob, 23, eyes=eyes, mouth=mouth, ears_back=0.5 if run else 0.0)
    return cv


def pose_sit(wag=0.0, eyes='open', mouth='neutral', breath=0.0):
    cv = Canvas()
    tail(cv, [(46, 108), (24, 116), (14 + wag, 100), (24 + wag, 86)], 13)
    d = sd_ellipse(47, 104, 15, 10.5) + (fur_noise(47, 104, 71, 1.6) - 0.5) * 0.9
    cv.part(d, FUR_M * 0.86, 9, grain=fur_noise(47, 104, 72, 2.2), ao=0.25)
    d = sd_ellipse(64, 91 - breath * 0.3, 21, 23 + breath) + (fur_noise(64, 91, 73, 1.5) - 0.5) * 0.9
    chest = soft_mask(sd_ellipse(68, 92, 12, 19) + (fur_noise(64, 91, 74, 2) - 0.5) * 2.4, 1.0)
    side = np.clip((60 - X) / 14, 0, 1)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, side * 0.8), WHITE, chest)
    cv.part(d, base, 15, grain=fur_noise(64, 91, 75, 2.2), ao=0.3)
    leg(cv, 59, 98, 59, 116, False)
    leg(cv, 71, 98, 71, 116, False)
    ruff(cv, 68, 76, 12, 11, 77)
    collar(cv, 66, 71, 12, 4.4)
    head(cv, 66, 47, 24, eyes=eyes, mouth=mouth)
    return cv


def pose_sleep(breath=0.0):
    cv = Canvas()
    d = sd_ellipse(58, 104 - breath * 0.5, 33, 12.5 + breath) + (fur_noise(58, 104, 81, 1.5) - 0.5) * 0.9
    saddle = np.clip((100 - Y) / 8 + 0.3, 0, 1)
    belly = soft_mask(sd_ellipse(62, 113, 26, 5) + (fur_noise(58, 104, 82, 2) - 0.5) * 2, 1.0)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, saddle * 0.8), WHITE, belly)
    cv.part(d, base, 11, grain=fur_noise(58, 104, 83, 2.2), ao=0.25)
    cv.part(sd_ellipse(98, 112.5, 9, 4), WHITE, 3.5, ao=0.2)
    cv.part(sd_ellipse(105, 113.5, 7.5, 3.4), WHITE, 3.0, ao=0.2)
    tail(cv, [(30, 103), (10, 110), (56, 113), (95, 108)], 12)
    collar(cv, 80, 92, 9, 3.6)
    head(cv, 84, 95, 20, eyes='closed', ears_back=0.35)
    return cv


# ---------------------------------------------------------------- output
def finish(cv):
    a = np.clip(cv.a, 0, 1)
    rgb = np.where(a[..., None] > 1e-4, cv.p / np.maximum(a[..., None], 1e-4), 0)
    # white die-cut sticker outline
    am = Image.fromarray((a * 255).astype(np.uint8), 'L')
    dil = am.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.GaussianBlur(1.2))
    da = np.asarray(dil, np.float32) / 255
    oc = C(244, 248, 255)
    out_p = rgb * a[..., None] + oc * (da * (1 - a))[..., None]
    out_a = a + da * (1 - a)
    # downsample in premultiplied space
    big = np.dstack([out_p, out_a * 255]).astype(np.float32)
    w2, h2 = CW * OUT_SCALE, CH * OUT_SCALE
    ch = [Image.fromarray(big[..., i]).resize((w2, h2), Image.LANCZOS) for i in range(4)]
    p = np.dstack([np.asarray(c, np.float32) for c in ch[:3]])
    al = np.clip(np.asarray(ch[3], np.float32), 0, 255)
    rgb2 = np.where(al[..., None] > 0.5, p / np.maximum(al[..., None] / 255, 1e-3), 0)
    return Image.fromarray(np.dstack([np.clip(rgb2, 0, 255), al]).astype(np.uint8), 'RGBA')


def animations():
    anims = {}
    anims['walk'] = [pose_stand(phase=i / 8, wag=math.sin(i / 8 * 2 * math.pi) * 3) for i in range(8)]
    anims['run'] = [pose_stand(phase=i / 6, run=True, eyes='wide', mouth='open', wag=math.sin(i / 6 * 4 * math.pi) * 7, bob_amp=2.5) for i in range(6)]
    anims['happy'] = [pose_stand(phase=0, eyes='happy', mouth='open', wag=math.sin(i / 6 * 2 * math.pi) * 9, bob_amp=0) for i in range(6)]
    anims['sit'] = [pose_sit(wag=math.sin(i / 6 * 2 * math.pi) * 3, breath=math.sin(i / 6 * 2 * math.pi) * 0.6) for i in range(6)]
    anims['sitblink'] = [pose_sit(eyes='blink')]
    anims['sithappy'] = [pose_sit(wag=math.sin(i / 4 * 2 * math.pi) * 5, eyes='happy', mouth='open') for i in range(4)]
    anims['sleep'] = [pose_sleep(breath=math.sin(i / 6 * 2 * math.pi) * 0.9) for i in range(6)]
    anims['eat'] = [pose_stand(phase=0, eat=True, chew=('chew1' if i % 2 == 0 else 'chew2'), wag=math.sin(i / 4 * 2 * math.pi) * 6, bob_amp=0) for i in range(4)]
    return anims


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    preview = None
    if '--preview' in sys.argv:
        preview = sys.argv[sys.argv.index('--preview') + 1]
    rows = []
    for name, frames in animations().items():
        imgs = [finish(cv) for cv in frames]
        w2, h2 = imgs[0].size
        sheet = Image.new('RGBA', (w2 * len(imgs), h2), (0, 0, 0, 0))
        for i, im in enumerate(imgs):
            sheet.paste(im, (i * w2, 0))
        sheet.save(os.path.join(out, name + '.png'), optimize=True)
        rows.append(imgs)
        print(f'{name}: {len(imgs)} frames')
    if preview:
        w2, h2 = rows[0][0].size
        cols = max(len(r) for r in rows)
        pv = Image.new('RGBA', (w2 * cols, h2 * len(rows)), (18, 32, 70, 255))
        for j, r in enumerate(rows):
            for i, im in enumerate(r):
                pv.alpha_composite(im, (i * w2, j * h2))
        pv.convert('RGB').save(preview)
        print('preview', preview)


if __name__ == '__main__':
    main()
