"""
Lucky's feature animations (break reminders, focus, battery, activity, work context, interaction).

Re-uses the SDF toon renderer in render_sprites.py (same palette, line weight, outline,
130x120 logical frame stored at 2x) and adds front paws used as hands, hand-drawn props
(glass, laptop, notepad, calculator, letter, headset, brush, popcorn, gift, board...) and
effects (confetti, sweat, sparkles). Frames render in parallel.

Usage:  python actions.py <out_dir> [--only a,b,c] [--preview preview.png] [--jobs N]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np                                                   # noqa: E402
from PIL import Image                                                 # noqa: E402
import render_sprites as rs                                           # noqa: E402
from render_sprites import (C, X, Y, H, W, R, CW, Canvas, sd_ellipse, sd_capsule, sd_tri, sd_polyline,  # noqa: E402
                            arc_pts, smin, mix, soft_mask, fur_noise, tail, collar, head, ruff, leg,
                            pose_stand, pose_lie, finish, FUR_M, FUR_D, WHITE, PINK, AQUA, LINE, TONGUE)

INK = LINE
PAPER = C(252, 252, 246)
BLUE = C(80, 140, 240)
YELLOW = C(255, 214, 110)
UI = C(66, 78, 120)
TAU = 2 * math.pi


# ------------------------------------------------------------------ helpers
def sd_box(cx, cy, hw, hh, ang=0.0, rad=0.0):
    x, y = X - cx, Y - cy
    if ang:
        c, s = math.cos(ang), math.sin(ang)
        x, y = x * c + y * s, -x * s + y * c
    qx, qy = np.abs(x) - (hw - rad), np.abs(y) - (hh - rad)
    return np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - rad


def rot(cx, cy, ang, lx, ly):
    c, s = math.cos(ang), math.sin(ang)
    return cx + lx * c - ly * s, cy + lx * s + ly * c


def outline(cv, d, alpha=0.9, w=0.45):
    cv.flat(np.abs(d) - w, INK, soft=1.0, alpha_mul=alpha)


# ------------------------------------------------------------------ front paw used as a hand
def arm(cv, sx, sy, px, py, far=False, limb=True, paw=True):
    if limb:
        d = sd_capsule(sx, sy, px, py, 5.2, 4.5) + (fur_noise(sx, sy, 91) - 0.5) * 0.4
        white = soft_mask(sd_ellipse(px, py, 7.5, 7.5) + (fur_noise(px, py, 92, 2) - 0.5) * 1.6, 1.0)
        base = mix(np.broadcast_to(FUR_M, (H, W, 3)), WHITE, white)
        if far:
            base = base * 0.86
        cv.part(d, base, 4.6, ao=0.2)
    if not paw:
        return
    cv.part(sd_ellipse(px, py, 5.0, 4.6), WHITE * (0.88 if far else 1.0), 3.6, ao=0.12)
    ang = math.atan2(py - sy, px - sx)
    for k in (-1.3, 1.3):
        bx = px + math.cos(ang) * 3.0 - math.sin(ang) * k
        by = py + math.sin(ang) * 3.0 + math.cos(ang) * k
        cv.flat(sd_capsule(bx - math.cos(ang) * 1.2, by - math.sin(ang) * 1.2, bx, by, 0.3, 0.3), C(130, 140, 170), soft=1.2, alpha_mul=0.8)


# ------------------------------------------------------------------ props
def glass(cv, cx, cy, tilt=0.0, level=0.62):
    outer = sd_box(cx, cy, 4.4, 6.2, tilt, 1.4)
    inner = sd_box(cx, cy, 3.4, 5.2, tilt, 1.0)
    cv.flat(outer, C(220, 240, 252), alpha_mul=0.6)
    wl = cy + 5.0 - level * 10.0 * math.cos(tilt)
    cv.part(np.maximum(inner, wl - Y), C(110, 200, 245), 2.0, gloss=0.45, ao=0, rim=0)
    outline(cv, outer, 0.95)
    a, b = rot(cx, cy, tilt, -2.4, -3.6), rot(cx, cy, tilt, -2.4, 2.6)
    cv.flat(sd_capsule(a[0], a[1], b[0], b[1], 0.5, 0.5), WHITE, alpha_mul=0.85)


def laptop(cv, cx, cy, phase, dark=False):
    sx, sy = cx, cy - 10
    cv.part(sd_box(sx, sy, 12.5, 8.6, 0, 1.6), C(70, 82, 124), 2.0, gloss=0.3, ao=0.15)
    scr = sd_box(sx, sy, 10.6, 6.9, 0, 0.8)
    top, bot = (C(30, 40, 72), C(18, 26, 54)) if dark else (C(205, 238, 255), C(140, 200, 250))
    cv.over(mix(top, bot, np.clip((Y - (sy - 7)) / 14, 0, 1)), np.clip(0.5 - scr * R, 0, 1))
    cols = [AQUA, PINK, YELLOW, C(140, 226, 160)] if dark else [BLUE, AQUA, PINK, C(90, 110, 170)]
    step = int(phase * 8)
    for k in range(4):
        y = sy - 4.6 + k * 3.0
        ln = 3 + ((k * 7 + step * 3) % 10)
        x0 = sx - 8.6 + (k % 2) * 2.0
        cv.flat(sd_capsule(x0, y, x0 + ln, y, 0.7, 0.7), cols[(k + step // 3) % 4])
    if step % 2 == 0:
        cv.flat(sd_box(sx + 4.5, sy + 4.4, 0.8, 0.9), cols[0])
    cv.part(sd_box(cx, cy + 0.5, 15, 2.6, 0, 1.2), C(100, 114, 160), 2.0, gloss=0.25, ao=0.2)
    for k in range(5):
        cv.flat(sd_box(cx - 10 + k * 5, cy - 0.2, 1.6, 0.5, 0, 0.3), C(176, 190, 226), alpha_mul=0.9)


def notepad(cv, cx, cy, tilt, lines):
    pad = sd_box(cx, cy, 8.5, 10.5, tilt, 1.0)
    cv.part(pad, PAPER, 2.0, gloss=0.05, ao=0.2)
    for k in range(5):
        x, y = rot(cx, cy, tilt, -6 + k * 3, -10.4)
        cv.flat(sd_ellipse(x, y, 0.8, 1.3), C(110, 122, 160))
    for k in range(lines):
        a = rot(cx, cy, tilt, -6, -6 + k * 3.2)
        b = rot(cx, cy, tilt, 5 - (k % 2) * 3, -6 + k * 3.2)
        cv.flat(sd_capsule(a[0], a[1], b[0], b[1], 0.45, 0.45), BLUE, soft=1.1, alpha_mul=0.85)


def pen(cv, tx, ty, ang, col=AQUA):
    ex, ey = tx + math.cos(ang) * 13, ty + math.sin(ang) * 13
    cv.part(sd_capsule(tx, ty, ex, ey, 0.6, 1.4), col, 1.2, gloss=0.35, ao=0.1)
    cv.flat(sd_capsule(tx, ty, tx + math.cos(ang) * 2, ty + math.sin(ang) * 2, 0.45, 0.7), INK)


def calculator(cv, cx, cy, tilt, pressed):
    cv.part(sd_box(cx, cy, 7.5, 10, tilt, 1.6), C(84, 100, 160), 2.4, gloss=0.3, ao=0.2)
    sx, sy = rot(cx, cy, tilt, 0, -6)
    cv.part(sd_box(sx, sy, 5.6, 2.2, tilt, 0.6), C(190, 244, 226), 1.0, gloss=0.2, ao=0, rim=0)
    for k in range(3):
        a = rot(cx, cy, tilt, 4 - k * 2.6 - 1.2, -6)
        b = rot(cx, cy, tilt, 4 - k * 2.6, -6)
        if (pressed + k) % 4 != 3:
            cv.flat(sd_capsule(a[0], a[1] - 0.9, b[0], b[1] + 0.9, 0.35, 0.35), C(30, 60, 70))
    for row in range(3):
        for col in range(3):
            n = row * 3 + col
            bx, by = rot(cx, cy, tilt, -4 + col * 4, -0.6 + row * 3.6)
            on = n == pressed
            cv.part(sd_ellipse(bx, by, 1.4 if not on else 1.15, 1.2 if not on else 0.95),
                    PINK if on else C(236, 242, 255), 1.0, gloss=0.2, ao=0, rim=0)


def letter(cv, cx, cy, tilt, lines=4):
    p = sd_box(cx, cy, 8, 9.5, tilt, 0.8)
    cv.part(p, PAPER, 2.0, gloss=0.04, ao=0.18)
    for k in range(lines):
        a = rot(cx, cy, tilt, -5, -5 + k * 3)
        b = rot(cx, cy, tilt, 5 - (k % 3) * 2, -5 + k * 3)
        cv.flat(sd_capsule(a[0], a[1], b[0], b[1], 0.4, 0.4), BLUE, soft=1.1, alpha_mul=0.8)


def envelope(cv, cx, cy, flap_open=True):
    if flap_open:
        cv.part(sd_tri((cx - 10, cy - 6), (cx + 10, cy - 6), (cx, cy - 13)) - 0.4, C(236, 226, 206), 2.0, ao=0.1)


def envelope_front(cv, cx, cy):
    body = sd_box(cx, cy, 10, 6.5, 0, 0.8)
    cv.part(body, C(250, 242, 226), 2.0, gloss=0.04, ao=0.18)
    cv.flat(sd_polyline([(cx - 9.5, cy - 6), (cx, cy + 1), (cx + 9.5, cy - 6)], 0.4), INK, soft=1.0, alpha_mul=0.7)
    cv.part(sd_ellipse(cx, cy + 1, 1.8, 1.8), PINK, 1.2, gloss=0.4, ao=0)


def canvas_board(cv, cx, cy, strokes):
    b = sd_box(cx, cy, 9, 11, -0.1, 0.6)
    cv.part(b, PAPER, 2.0, gloss=0.04, ao=0.18)
    cv.part(np.abs(b) - 0.8, C(150, 170, 214), 0.9, ao=0)
    paths = [
        ([(-6, 5), (-3, 2), (0, 4), (3, 1), (6, 3)], BLUE),
        ([(-6, -1), (-2, -4), (2, -2), (6, -5)], AQUA),
        ([(-4, -7), (-1, -6), (2, -8)], PINK),
        ([(3, 6), (5, 7), (6, 5)], YELLOW),
    ]
    for k in range(min(strokes, len(paths))):
        pts = [rot(cx, cy, -0.1, px, py) for px, py in paths[k][0]]
        cv.flat(sd_polyline(pts, 0.9), paths[k][1], soft=1.0)


def brush(cv, tx, ty, ang, tip=PINK):
    ex, ey = tx + math.cos(ang) * 14, ty + math.sin(ang) * 14
    cv.part(sd_capsule(tx + math.cos(ang) * 3, ty + math.sin(ang) * 3, ex, ey, 0.8, 1.2), C(226, 232, 248), 1.2, gloss=0.3, ao=0.1)
    fx, fy = tx + math.cos(ang) * 3, ty + math.sin(ang) * 3
    cv.part(sd_capsule(fx, fy, fx + math.cos(ang) * 1.6, fy + math.sin(ang) * 1.6, 1.1, 1.1), C(200, 210, 230), 1.0, gloss=0.6, ao=0)
    cv.part(sd_capsule(tx, ty, fx, fy, 0.4, 1.2), tip, 1.0, gloss=0.3, ao=0)


def popcorn(cv, cx, cy, pieces=7):
    x, y = np.abs(X - cx), Y - cy
    w = 9 + (6.5 - 9) * (y + 7) / 14
    d = np.maximum(x - w, np.abs(y) - 7)
    stripes = (np.sin((X - cx) * 1.1) > 0).astype(np.float32)
    base = mix(np.broadcast_to(WHITE, (H, W, 3)), AQUA, stripes[..., None] * 0.9 if False else stripes)
    cv.part(d, base, 3.0, gloss=0.08, ao=0.2)
    rng = np.random.default_rng(3)
    for k in range(pieces):
        px, py = cx - 6.5 + k * 2.2 + rng.uniform(-0.6, 0.6), cy - 7.5 - rng.uniform(0, 3)
        cv.part(sd_ellipse(px, py, 2.0, 1.7) + (fur_noise(px, py, 140 + k, 4) - 0.5) * 1.2, C(255, 246, 214), 1.4, ao=0.05, rim=0)


def kernel(cv, x, y):
    cv.part(sd_ellipse(x, y, 1.9, 1.6) + (fur_noise(x, y, 150, 4) - 0.5) * 1.0, C(255, 246, 214), 1.3, ao=0.05, rim=0)


def gift(cv, cx, cy, s=1.0):
    box = sd_box(cx, cy + 1 * s, 6.5 * s, 5.5 * s, 0, 1.0)
    lid = sd_box(cx, cy - 4.4 * s, 7.3 * s, 1.9 * s, 0, 0.8)
    cv.part(box, AQUA, 3.0, gloss=0.2, ao=0.2)
    cv.part(lid, C(120, 230, 240), 1.6, gloss=0.25, ao=0.1)
    cv.flat(np.maximum(np.minimum(box, lid), np.abs(X - cx) - 1.3 * s), PINK)
    for side in (-1, 1):
        cv.part(sd_ellipse(cx + side * 2.6 * s, cy - 7.6 * s, 2.6 * s, 1.7 * s, side * 0.5), PINK, 1.2, gloss=0.3, ao=0.05)
    cv.part(sd_ellipse(cx, cy - 7.2 * s, 1.3 * s, 1.3 * s), PINK, 1.0, ao=0)


def paper(cv, cx, cy, tilt):
    p = sd_box(cx, cy, 6.2, 7.6, tilt, 0.6)
    cv.part(p, PAPER, 1.8, gloss=0.04, ao=0.18)
    for k in range(3):
        a = rot(cx, cy, tilt, -3.8, -3.5 + k * 3)
        b = rot(cx, cy, tilt, 3.8 - k * 1.2, -3.5 + k * 3)
        cv.flat(sd_capsule(a[0], a[1], b[0], b[1], 0.4, 0.4), BLUE, soft=1.1, alpha_mul=0.8)


def board(cv, cx, cy, wiggle=0.0):
    b = sd_box(cx, cy, 15, 11.5, 0, 1.6)
    cv.part(b, C(250, 252, 255), 2.5, gloss=0.04, ao=0.25)
    cv.part(np.abs(b) - 0.9, AQUA, 1.0, gloss=0.3, ao=0)
    base = cy + 7.5
    for k, (hgt, col) in enumerate(((6, BLUE), (9.5, AQUA), (12.5 + wiggle, PINK))):
        x = cx + 2 + k * 4.2
        cv.part(sd_box(x, base - hgt / 2, 1.5, hgt / 2, 0, 0.4), col, 1.0, gloss=0.2, ao=0, rim=0)
    cv.part(sd_polyline([(cx - 11, cy - 2), (cx - 8.6, cy + 1), (cx - 4.4, cy - 5)], 0.95), C(32, 190, 170), 0.9, ao=0, rim=0)
    cv.flat(sd_capsule(cx - 11, cy + 5, cx - 5, cy + 5, 0.45, 0.45), BLUE, alpha_mul=0.8)


# ------------------------------------------------------------------ effects
def sweat(cv, x, y, s=1.0):
    d = smin(sd_ellipse(x, y, 2.0 * s, 2.2 * s), sd_tri((x - 1.5 * s, y - 0.6 * s), (x + 1.5 * s, y - 0.6 * s), (x, y - 5 * s)), 0.6)
    cv.part(d, C(150, 214, 255), 1.6 * s, gloss=0.5, shin=30, ao=0, rim=0)


def sparkle(cv, x, y, s, col=None):
    col = C(255, 246, 190) if col is None else col
    cv.flat(np.minimum(sd_ellipse(x, y, s, s * 0.22), sd_ellipse(x, y, s * 0.22, s)), col, soft=1.0)


def confetti(cv, phase, seed=5, n=26):
    rng = np.random.default_rng(seed)
    cols = [AQUA, PINK, YELLOW, BLUE, WHITE]
    for k in range(n):
        x0, sp, off, a0 = rng.uniform(6, 124), rng.uniform(0.7, 1.2), rng.uniform(0, 1), rng.uniform(0, TAU)
        t = (phase * sp + off) % 1.0
        x, y = x0 + math.sin(t * TAU * 1.5 + a0) * 4, -6 + t * 96
        d = sd_box(x, y, 1.7, 0.85, a0 + t * 9, 0.2)
        cv.flat(d, cols[k % 5])
        outline(cv, d, 0.55, 0.15)


def rumble(cv, x, y, phase):
    for k in range(2):
        off = math.sin(phase * TAU + k) * 0.8
        cv.flat(sd_polyline(arc_pts(x + k * 3.2 + off, y, 2.2, 3.4, -55, 55, 8), 0.45), INK, soft=1.0, alpha_mul=0.8)


def exclaim(cv, x, y):
    d = np.minimum(sd_capsule(x, y - 7, x, y - 1, 1.7, 1.1), sd_ellipse(x, y + 2.3, 1.45, 1.45))
    cv.part(d, C(255, 96, 128), 1.2, gloss=0.4, ao=0, rim=0)


def check(cv, x, y, s=1.0):
    cv.part(sd_polyline([(x - 3 * s, y), (x - 1 * s, y + 2.4 * s), (x + 3.4 * s, y - 3 * s)], 1.0 * s), C(32, 200, 172), 1.0, ao=0, rim=0)


def motion_lines(cv, x, y):
    for k in range(3):
        cv.flat(sd_capsule(x, y + k * 6, x + 9 - k * 2, y + k * 6, 0.6, 0.6), C(200, 226, 255), alpha_mul=0.8)


# ------------------------------------------------------------------ poses
def pose_act(head_kw=None, arms=None, back=(), mid=(), front=(), fx=(), dy=0.0, wag=0.0, breath=0.0,
             hdx=0.0, hdy=0.0, hind='sit', swing=0.0, tail_pts=None, arms_behind=False):
    """Sitting husky. `arms` = ((far_x, far_y), (near_x, near_y)) turns the front legs into hands."""
    cv = Canvas()
    hk = dict(eyes='open', mouth='neutral')
    hk.update(head_kw or {})
    tail(cv, tail_pts or [(46, 108 + dy), (24, 116 + dy), (14 + wag, 100 + dy), (24 + wag, 86 + dy)], 13)
    if hind == 'sit':
        d = sd_ellipse(47, 104 + dy, 15, 10.5) + (fur_noise(47, 104, 71, 1.6) - 0.5) * 0.9
        cv.part(d, FUR_M * 0.86, 9, ao=0.25)
    if hind == 'dangle':
        leg(cv, 54, 100 + dy, 52 + swing, 118 + dy, True)
    d = sd_ellipse(64, 91 + dy - breath * 0.3, 21, 23 + breath) + (fur_noise(64, 91, 73, 1.5) - 0.5) * 0.9
    chest = soft_mask(sd_ellipse(68, 92 + dy, 12, 19) + (fur_noise(64, 91, 74, 2) - 0.5) * 2.4, 1.0)
    side = np.clip((60 - X) / 14, 0, 1)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, side * 0.8), WHITE, chest)
    cv.part(d, base, 15, ao=0.3)
    if hind == 'dangle':
        leg(cv, 74, 100 + dy, 76 - swing, 118 + dy, False)
    if arms is None:
        leg(cv, 59, 98 + dy, 59, 116 + dy, False)
        leg(cv, 71, 98 + dy, 71, 116 + dy, False)
    elif hind == 'sit':
        cv.part(sd_ellipse(55, 113.5 + dy, 6.4, 3.6), WHITE, 3.0, ao=0.15)
        cv.part(sd_ellipse(75, 113.5 + dy, 6.4, 3.6), WHITE, 3.0, ao=0.15)
    ruff(cv, 68, 76 + dy, 12, 11, 77)
    collar(cv, 66, 71 + dy, 12, 4.4)
    if arms and arms_behind:
        arm(cv, 58, 81 + dy, arms[0][0], arms[0][1] + dy, far=True, paw=False)
        arm(cv, 73, 81 + dy, arms[1][0], arms[1][1] + dy, paw=False)
    head(cv, 66 + hdx, 47 + dy + hdy, 24, **hk)
    for f in back:
        f(cv)
    if arms:
        arm(cv, 58, 81 + dy, arms[0][0], arms[0][1] + dy, far=True, limb=not arms_behind)
    for f in mid:
        f(cv)
    if arms:
        arm(cv, 73, 81 + dy, arms[1][0], arms[1][1] + dy, limb=not arms_behind)
    for f in front:
        f(cv)
    for f in fx:
        f(cv)
    return cv


def pose_between(t):
    """Stand (t=0) -> sit (t=1) in-between, used for the transition animations."""
    cv = Canvas()

    def L(a, b):
        return a + (b - a) * t
    tail(cv, [(L(34, 46), L(86, 108)), (L(14, 24), L(80, 116)), (L(10, 14), L(50, 100)), (L(30, 24), L(50, 86))], 13)
    if t < 0.7:
        leg(cv, L(40, 50), L(93, 100), L(40, 50), 116, True)
    hr = L(4, 15)
    d = sd_ellipse(L(42, 47), L(96, 104), hr, hr * 0.7) + (fur_noise(47, 104, 71, 1.6) - 0.5) * 0.9
    cv.part(d, FUR_M * 0.86, 9, ao=0.2)
    bx, by, brx, bry = L(60, 64), L(90, 91), L(30, 21), L(15.5, 23)
    d = sd_ellipse(bx, by, brx, bry)
    d = smin(d, sd_ellipse(bx + L(22, 4), by - L(3, 0), L(13, 16), L(14, 18)), 6)
    d = d + (fur_noise(64, 91, 73, 1.5) - 0.5) * 0.9
    chest = soft_mask(sd_ellipse(L(85, 68), L(92, 92), L(9, 12), L(12, 19)) + (fur_noise(64, 91, 74, 2) - 0.5) * 2.4, 1.0)
    saddle = np.clip((by - Y) / 9 + 0.2, 0, 1) * (1 - t)
    base = mix(mix(np.broadcast_to(FUR_M, (H, W, 3)), FUR_D, saddle * 0.85), WHITE, chest)
    cv.part(d, base, 13, ao=0.3)
    if t < 0.7:
        leg(cv, L(50, 50), L(93, 100), L(50, 52), 116, False)
    leg(cv, L(78, 59), L(93, 98), L(78, 59), 116, False)
    leg(cv, L(88, 71), L(93, 98), L(88, 71), 116, False)
    ruff(cv, L(85, 68), L(84, 76), L(11, 12), L(13, 11), 77)
    collar(cv, L(87, 66), L(79, 71), L(11, 12), 4.4)
    head(cv, L(92, 66), L(56, 47), L(23, 24))
    return cv


# ------------------------------------------------------------------ animation registry
# name -> (frames, fps, loop, frame_fn(i) -> Canvas | (Canvas, post))
A = {}


def anim(name, n, fps, loop):
    def deco(fn):
        A[name] = (n, fps, loop, fn)
        return fn
    return deco


def P(i, n):
    return i / n


LOOK8 = [(math.cos(k * TAU / 8), math.sin(k * TAU / 8)) for k in range(8)]


@anim('transition_in', 4, 12, False)
def _(i):
    return pose_between((i + 1) / 5)


@anim('transition_out', 4, 12, False)
def _(i):
    return pose_between((4 - i) / 5)


@anim('bubble_talk', 4, 8, True)
def _(i):
    return pose_act(head_kw=dict(mouth=['talk', 'neutral', 'open', 'neutral'][i]), hdy=[0, -0.6, 0, -0.4][i], wag=math.sin(P(i, 4) * TAU) * 3)


# ---------------- F1 breaks
@anim('break_eye', 8, 6, True)
def _(i):
    if i < 4:
        rub = math.sin(i / 4 * TAU) * 1.6
        return pose_act(head_kw=dict(eyes='closed', mouth='worry'), arms=((55 + rub, 46), (77 - rub, 45)))
    return pose_act(head_kw=dict(eyes='blink' if i == 6 else 'open', look=(0.9, -0.85)), wag=math.sin(P(i, 4) * TAU) * 2, hdx=1.0, hdy=-0.8)


@anim('break_water', 8, 6, True)
def _(i):
    seq = [((80, 92), 0.0, 'open', 'neutral'), ((80, 90), 0.1, 'open', 'neutral'), ((84, 76), 0.55, 'happy', 'neutral'),
           ((86, 70), 0.95, 'closed', 'neutral'), ((86, 70), 1.05, 'closed', 'neutral'), ((83, 80), 0.4, 'open', 'neutral'),
           ((92, 86), 0.0, 'happy', 'open'), ((93, 86), 0.0, 'happy', 'open')]
    (gx, gy), tl, eyes, mouth = seq[i]
    lvl = 0.62 if i < 3 else 0.45
    return pose_act(head_kw=dict(eyes=eyes, mouth=mouth), arms=((gx - 9, gy + 4), (gx + 1, gy + 3)),
                    mid=[lambda cv, gx=gx, gy=gy, tl=tl, lvl=lvl: glass(cv, gx, gy - 2, tl, lvl)], wag=math.sin(P(i, 8) * TAU) * 2)


@anim('break_stretch', 10, 7, True)
def _(i):
    paws = [((62, 94), (76, 94)), ((58, 84), (80, 84)), ((54, 68), (82, 66)), ((48, 40), (88, 38)), ((46, 12), (88, 10)),
            ((48, 9), (90, 8)), ((44, 11), (86, 9)), ((47, 14), (89, 12)), ((52, 56), (84, 54)), ((60, 84), (78, 84))]
    sway = [0, 0, 0, 0, -2.5, 0, 2.5, 0, 0, 0][i]
    f, n = paws[i]
    mouth = 'yawn' if i in (4, 5, 6) else 'neutral'
    eyes = 'closed' if 3 <= i <= 7 else 'open'
    return pose_act(head_kw=dict(eyes=eyes, mouth=mouth), arms=((f[0] + sway, f[1]), (n[0] + sway, n[1])),
                    dy=-1.5 if 3 <= i <= 7 else 0, breath=0.8 if 3 <= i <= 7 else 0, hdx=sway * 0.6, arms_behind=3 <= i <= 7)


@anim('break_done', 6, 8, False)
def _(i):
    nod = [0, 2.2, 3.0, 1.2, 0, 0][i]
    return pose_act(head_kw=dict(eyes='happy', mouth='open'), arms=None if i < 1 else ((60, 96), (84, 58 - min(i, 3) * 1.5)), hdy=nod,
                    fx=[lambda cv, i=i: sparkle(cv, 92, 46, 2.5 + i * 0.4)] if i >= 2 else [])


# ---------------- F3 focus
@anim('focus_working', 8, 8, True)
def _(i):
    up = i % 2
    return pose_act(head_kw=dict(look=(0.55, 0.8)), arms=((70, 106 - (0 if up else 2)), (86, 106 - (2 if up else 0))),
                    back=[lambda cv, i=i: laptop(cv, 80, 108, i / 8)], hdy=0.6 + (0.3 if up else 0))


@anim('focus_done', 12, 8, False)
def _(i):
    dy = [0, -3, -7, -9, -7, -3, 0, -2, -5, -2, 0, 0][i]
    up = dy < 0
    return pose_act(head_kw=dict(eyes='happy', mouth='open'), arms=((52, 52), (82, 50)) if up else ((58, 80), (80, 78)), dy=dy,
                    wag=math.sin(P(i, 6) * TAU) * 6,
                    fx=[lambda cv, i=i: confetti(cv, i / 12), lambda cv, i=i: sparkle(cv, 30 + i * 3, 30, 2.5), lambda cv, i=i: sparkle(cv, 104 - i * 2, 22, 3)])


@anim('focus_break', 6, 5, True)
def _(i):
    return pose_lie(eyes='happy', mouth='neutral', breath=math.sin(P(i, 6) * TAU) * 0.9, wag=math.sin(P(i, 6) * TAU) * 3, ears_back=0.15)


@anim('focus_distracted', 6, 8, False)
def _(i):
    return pose_act(head_kw=dict(eyes='closed' if 1 <= i <= 3 else 'open', mouth='worry', brows='worried'), hdx=[0, -2.5, 2.5, -2, 1, 0][i])


# ---------------- F4 battery / overload
@anim('battery_hungry', 8, 6, True)
def _(i):
    rub = math.sin(P(i, 4) * TAU) * 1.2
    look = (-0.9, 0.2) if i < 4 else (0.9, 0.2)
    return pose_act(head_kw=dict(look=look, mouth='worry', brows='worried'), arms=((60 + rub, 99), (74 - rub, 99)),
                    fx=[lambda cv, i=i: rumble(cv, 86, 98, i / 8)])


@anim('battery_critical', 6, 4, True)
def _(i):
    return pose_lie(eyes='half', mouth='worry', breath=math.sin(P(i, 6) * TAU) * 0.35, wag=0, ears_back=0.8, flat=1.0, brows='worried',
                    extra=[lambda cv, i=i: sweat(cv, 70, 80 + i * 0.6, 0.9)])


@anim('battery_charged', 8, 10, False)
def _(i):
    xs = math.cos(P(i, 8) * TAU)
    xs = math.copysign(max(abs(xs), 0.32), xs)
    cv = pose_stand(phase=0, eyes='happy', mouth='open', wag=math.sin(P(i, 4) * TAU) * 9, bob_amp=0,
                    extra=[lambda cv, bob, i=i: sparkle(cv, 30 + i * 4, 40 - i, 3), lambda cv, bob: sparkle(cv, 110, 30, 2.5)])
    return cv, dict(xscale=xs, dy=-[2, 6, 9, 9, 6, 2, 0, 0][i])


@anim('overload_sweat', 8, 8, True)
def _(i):
    fan = math.sin(P(i, 4) * TAU) * 5
    return pose_act(head_kw=dict(mouth='pant', brows='worried'), arms=((60, 98), (90 + fan * 0.3, 54 + fan)),
                    fx=[lambda cv, i=i: sweat(cv, 48, 30 + (i % 4) * 2.5, 1.0), lambda cv, i=i: sweat(cv, 86, 24 + ((i + 2) % 4) * 2.5, 0.8)])


# ---------------- F5 activity
@anim('activity_typing_fast', 8, 10, True)
def _(i):
    a = i % 2 == 0
    return pose_act(head_kw=dict(eyes='happy', mouth='open'), arms=((54, 54), (80, 76)) if a else ((58, 76), (84, 50)),
                    dy=-3 if a else 0, wag=math.sin(P(i, 4) * TAU) * 7,
                    fx=[lambda cv, i=i: sparkle(cv, 26 + (i % 4) * 4, 36, 2.6), lambda cv, i=i: sparkle(cv, 104, 30 + (i % 3) * 3, 2.2)])


@anim('activity_look', 8, 1, True)
def _(i):
    return pose_act(head_kw=dict(look=LOOK8[i]), hdx=LOOK8[i][0] * 0.8, hdy=LOOK8[i][1] * 0.6)


@anim('activity_idle_yawn', 8, 6, False)
def _(i):
    mouth = ['neutral', 'o', 'yawn', 'yawn', 'yawn', 'yawn', 'o', 'neutral'][i]
    eyes = 'closed' if 2 <= i <= 5 else 'open'
    return pose_act(head_kw=dict(eyes=eyes, mouth=mouth, ears_back=0.4 if 2 <= i <= 5 else 0), hdy=-1.2 if 2 <= i <= 5 else 0,
                    breath=0.8 if 2 <= i <= 5 else 0)


@anim('activity_wake', 8, 7, False)
def _(i):
    if i < 4:
        rub = math.sin(i / 4 * TAU) * 1.6
        return pose_act(head_kw=dict(eyes='half', mouth='o'), arms=((55 + rub, 46), (77 - rub, 45)))
    wave = math.sin(P(i, 2) * TAU) * 4
    return pose_act(head_kw=dict(eyes='happy', mouth='open'), arms=((60, 96), (88 + wave, 50)), wag=math.sin(P(i, 4) * TAU) * 5)


@anim('activity_startle', 4, 10, False)
def _(i):
    dy = [0, -6, -4, 0][i]
    return pose_act(head_kw=dict(eyes='wide', mouth='o', ears_back=-0.3), dy=dy,
                    arms=((56, 74), (82, 72)) if i in (1, 2) else None,
                    fx=[lambda cv, dy=dy: exclaim(cv, 92, 22 + dy)] if i else [])


@anim('activity_saved', 4, 8, False)
def _(i):
    return pose_act(head_kw=dict(eyes='happy', mouth='neutral'), hdy=[0, 3, 2, 0][i],
                    fx=[lambda cv, i=i: check(cv, 96, 26 - i, 1.0 + i * 0.1)])


# ---------------- F6 work context
@anim('context_code', 8, 8, True)
def _(i):
    up = i % 2
    return pose_act(head_kw=dict(look=(0.55, 0.8), glasses=True), arms=((70, 106 - (0 if up else 2)), (86, 106 - (2 if up else 0))),
                    back=[lambda cv, i=i: laptop(cv, 80, 108, i / 8, dark=True)], hdy=0.6)


@anim('context_docs', 8, 6, True)
def _(i):
    k = i % 4
    tx, ty = rot(64, 98, -0.15, -5 + k * 3, -6 + (i // 2) * 3.2)
    ang = -1.05
    return pose_act(head_kw=dict(look=(0.2, 0.9)), arms=((56, 102), (tx + math.cos(ang) * 6.5, ty + math.sin(ang) * 6.5)),
                    back=[lambda cv, i=i: notepad(cv, 64, 98, -0.15, 1 + i // 2)],
                    mid=[lambda cv, tx=tx, ty=ty: pen(cv, tx, ty, ang)], hdy=0.8)


@anim('context_sheet', 8, 8, True)
def _(i):
    pressed = [0, 4, 2, 7, 5, 1, 8, 3][i]
    bx, by = rot(64, 98, -0.1, -4 + (pressed % 3) * 4, -0.6 + (pressed // 3) * 3.6)
    return pose_act(head_kw=dict(look=(0.0, 0.9)), arms=((56, 104), (bx + 3.5, by - 3.5 if i % 2 else by - 5.5)),
                    back=[lambda cv, p=pressed: calculator(cv, 64, 98, -0.1, p)], hdy=0.8)


@anim('context_email', 8, 5, True)
def _(i):
    look = (-0.4 + (i % 4) * 0.3, 0.7)
    return pose_act(head_kw=dict(look=look), arms=((62, 92), (84, 92)), hdy=0.6 + (i % 2) * 0.4,
                    back=[lambda cv: envelope(cv, 73, 102), lambda cv: letter(cv, 73, 90, 0.04 * math.sin(i), 4),
                          lambda cv: envelope_front(cv, 73, 102)])


@anim('context_meeting', 6, 4, True)
def _(i):
    return pose_act(head_kw=dict(headset=True, mouth='neutral' if i % 3 else 'talk'), hdy=[0, 0, 1.5, 1.5, 0, 0][i],
                    wag=math.sin(P(i, 6) * TAU) * 2)


@anim('context_design', 8, 6, True)
def _(i):
    tx, ty = 66 + math.sin(P(i, 4) * TAU) * 3, 90 + (i % 4) * 1.6
    ang = -0.6
    return pose_act(head_kw=dict(look=(-0.4, 0.6)), arms=((52, 98), (tx + math.cos(ang) * 7, ty + math.sin(ang) * 7)),
                    back=[lambda cv, i=i: canvas_board(cv, 58, 92, 1 + i // 2)],
                    mid=[lambda cv, tx=tx, ty=ty: brush(cv, tx, ty, ang, [PINK, AQUA, BLUE, YELLOW][(i // 2) % 4])], hdy=0.4)


@anim('context_fun', 8, 6, True)
def _(i):
    path = [(78, 96), (80, 90), (86, 76), (90, 68), (90, 68), (86, 78), (80, 92), (78, 96)]
    px, py = path[i]
    eating = i in (3, 4)
    return pose_act(head_kw=dict(eyes='happy' if eating else 'open', mouth='chew1' if eating else 'neutral', look=(0.6, 0.2)),
                    arms=((60, 100), (px, py)), back=[lambda cv: popcorn(cv, 70, 104)],
                    front=[lambda cv, px=px, py=py: kernel(cv, px + 2.5, py - 3.5)] if 1 <= i <= 3 else [],
                    wag=math.sin(P(i, 8) * TAU) * 3)


@anim('context_praise', 8, 10, False)
def _(i):
    together = i % 2 == 1
    return pose_act(head_kw=dict(eyes='happy', mouth='open'), arms=((68, 84), (76, 83)) if together else ((58, 86), (88, 84)),
                    wag=math.sin(P(i, 4) * TAU) * 7,
                    fx=[lambda cv, i=i: sparkle(cv, 72, 76, 3.2), lambda cv: sparkle(cv, 90, 70, 2)] if together else [])


@anim('context_day_summary', 6, 4, True)
def _(i):
    return pose_act(head_kw=dict(eyes='blink' if i == 4 else 'happy' if i % 2 else 'open', mouth='talk' if i % 2 else 'open'),
                    arms=((57, 90), (86, 88)), back=[lambda cv, i=i: board(cv, 71, 98, math.sin(P(i, 6) * TAU) * 0.8)], hdy=-0.5)


# ---------------- F7 interaction
@anim('dodge', 4, 12, False)
def _(i):
    cv = pose_stand(phase=0.25 * i, eyes='wide', mouth='o', bob_amp=0, extra=[lambda cv, bob: motion_lines(cv, 6, 82)] if i in (1, 2) else [])
    return cv, dict(dy=-[0, 5, 3, 0][i])


@anim('dragged', 6, 8, True)
def _(i):
    sw = math.sin(P(i, 6) * TAU) * 4
    return pose_act(head_kw=dict(eyes='wide' if i < 3 else 'happy', mouth='o' if i < 3 else 'open'), arms=((48, 58), (86, 56)), dy=-4,
                    hind='dangle', swing=sw, tail_pts=[(46, 102), (36, 110), (30 + sw * 0.5, 112), (24 + sw, 106)])


@anim('interact_on', 6, 8, False)
def _(i):
    wave = math.sin(P(i, 3) * TAU) * 4
    return pose_act(head_kw=dict(eyes='sparkle', mouth='open'), arms=((60, 96), (88 + wave, 50)), wag=math.sin(P(i, 3) * TAU) * 6,
                    fx=[lambda cv, i=i: sparkle(cv, 98, 36 - i, 2.8)])


@anim('interact_off', 6, 8, False)
def _(i):
    return pose_act(head_kw=dict(eyes='happy' if i < 3 else 'closed' if i < 5 else 'open'), hdy=[0, 2, 3, 2, 1, 0][i])


# ------------------------------------------------------------------ rendering
def render_frame(job):
    name, i = job
    res = A[name][3](i)
    cv, post = (res if isinstance(res, tuple) else (res, None))
    img = finish(cv)
    if post:
        w, h = img.size
        xs, dyp = post.get('xscale', 1.0), post.get('dy', 0.0)
        out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        nw = max(2, int(round(w * abs(xs))))
        im2 = img.resize((nw, h), Image.LANCZOS)
        if xs < 0:
            im2 = im2.transpose(Image.FLIP_LEFT_RIGHT)
        cx = 62 * rs.OUT_SCALE
        out.alpha_composite(im2, (int(round(cx - cx * abs(xs))), 0))
        if dyp:
            shifted = Image.new('RGBA', (w, h), (0, 0, 0, 0))
            shifted.alpha_composite(out, (0, 0), (0, int(-dyp * rs.OUT_SCALE)))
            out = shifted
        img = out
    return name, i, img


def main():
    from multiprocessing import Pool
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    only = None
    if '--only' in sys.argv:
        only = sys.argv[sys.argv.index('--only') + 1].split(',')
    jobs_n = int(sys.argv[sys.argv.index('--jobs') + 1]) if '--jobs' in sys.argv else max(1, (os.cpu_count() or 2) - 1)
    names = [n for n in A if only is None or n in only]
    jobs = [(n, i) for n in names for i in range(A[n][0])]
    frames = {}
    with Pool(jobs_n) as pool:
        for k, (name, i, img) in enumerate(pool.imap_unordered(render_frame, jobs, chunksize=1)):
            frames[(name, i)] = img
            if k % 20 == 0:
                print(f'{k + 1}/{len(jobs)}', flush=True)
    rows = []
    with open(os.path.join(out, 'anims.txt'), 'a' if only else 'w', encoding='utf-8') as man:
        for n in names:
            cnt, fps, loop, _ = A[n]
            imgs = [frames[(n, i)] for i in range(cnt)]
            w2, h2 = imgs[0].size
            sheet = Image.new('RGBA', (w2 * cnt, h2), (0, 0, 0, 0))
            for i, im in enumerate(imgs):
                sheet.paste(im, (i * w2, 0))
            sheet.save(os.path.join(out, n + '.png'), optimize=True)
            man.write(f'{n},{cnt},{fps},{1 if loop else 0}\n')
            rows.append((n, imgs))
    print('done', len(names), 'animations,', len(jobs), 'frames')
    if '--preview' in sys.argv:
        pv_path = sys.argv[sys.argv.index('--preview') + 1]
        w2, h2 = rows[0][1][0].size
        sc = 0.5
        tw, th = int(w2 * sc), int(h2 * sc)
        cols = 12
        pv = Image.new('RGBA', (tw * cols, th * len(rows)), (18, 32, 70, 255))
        for j, (n, imgs) in enumerate(rows):
            for i, im in enumerate(imgs[:cols]):
                pv.alpha_composite(im.resize((tw, th), Image.LANCZOS), (i * tw, j * th))
        pv.convert('RGB').save(pv_path)
        print('preview', pv_path)


if __name__ == '__main__':
    main()
