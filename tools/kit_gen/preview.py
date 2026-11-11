"""Software preview renderer (no GPU): toon-shaded tiles with outlines, laid out as labelled contact sheets."""
import math
import numpy as np
from PIL import Image, ImageDraw

def render(model, size=300, yaw=35, pitch=22, bg=(232, 238, 246)):
    S = size * 2; baked = model.baked()
    lo, hi = model.bounds(); ctr = (lo + hi) / 2
    ya, pa = math.radians(yaw), math.radians(pitch)
    Ry = np.array([[math.cos(ya), 0, math.sin(ya)], [0, 1, 0], [-math.sin(ya), 0, math.cos(ya)]])
    Rx = np.array([[1, 0, 0], [0, math.cos(pa), -math.sin(pa)], [0, math.sin(pa), math.cos(pa)]])
    R = Rx @ Ry
    allv = np.vstack([b[0] for b in baked.values()]); p = (allv - ctr) @ R.T
    ext = max(np.abs(p[:, :2]).max(), 1e-6) * 1.12
    img = np.tile(np.array(bg, float) / 255, (S, S, 1)); zb = np.full((S, S), -1e9); L = np.array([-.45, .75, .5]); L = L / np.linalg.norm(L)
    for m, (v, n, c, f) in baked.items():
        pv = (v - ctr) @ R.T; pn = n @ R.T
        sx = (pv[:, 0] / ext * .5 + .5) * S; sy = (.5 - pv[:, 1] / ext * .5) * S; sz = pv[:, 2]
        glow = m.startswith('glow'); two = m == 'matte2s'
        for a, b, d in f:
            x0, x1, x2, y0, y1, y2 = sx[a], sx[b], sx[d], sy[a], sy[b], sy[d]
            area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
            if abs(area) < 1e-9 or (area > 0 and not two): continue      # back face
            xa, xb = int(max(min(x0, x1, x2), 0)), int(min(max(x0, x1, x2) + 1, S)); ya_, yb = int(max(min(y0, y1, y2), 0)), int(min(max(y0, y1, y2) + 1, S))
            if xa >= xb or ya_ >= yb: continue
            X, Y = np.meshgrid(np.arange(xa, xb) + .5, np.arange(ya_, yb) + .5)
            w0 = ((x1 - X) * (y2 - Y) - (x2 - X) * (y1 - Y)) / area; w1 = ((x2 - X) * (y0 - Y) - (x0 - X) * (y2 - Y)) / area; w2 = 1 - w0 - w1
            ins = (w0 >= 0) & (w1 >= 0) & (w2 >= 0); z = w0 * sz[a] + w1 * sz[b] + w2 * sz[d]
            sub = zb[ya_:yb, xa:xb]; ok = ins & (z > sub)
            if not ok.any(): continue
            nn = w0[..., None] * pn[a] + w1[..., None] * pn[b] + w2[..., None] * pn[d]; cc = w0[..., None] * c[a] + w1[..., None] * c[b] + w2[..., None] * c[d]
            ndl = (nn @ L) / np.maximum(np.linalg.norm(nn, axis=2), 1e-9)
            if two: ndl = np.abs(ndl)
            lit = np.clip((ndl * .5 + .5 - .47) / .04, 0, 1)[..., None]
            shade = cc * (np.array([.66, .6, .8]) * (1 - lit) + lit)
            if m == 'metal': shade = shade * (1 + .35 * np.sin(nn[..., 1:2] * 9))
            if glow: shade = np.clip(cc * 1.25 + .15, 0, 1)
            sub[ok] = z[ok]; img[ya_:yb, xa:xb][ok] = np.clip(shade, 0, 1)[ok]
    # outline where depth jumps
    d = zb.copy(); d[d < -1e8] = -ext * 4; e = np.zeros_like(d)
    for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)): e = np.maximum(e, np.roll(d, (dy, dx), (0, 1)) - d)
    edge = np.clip((e - ext * .05) / (ext * .03), 0, 1)[..., None]
    img = img * (1 - edge * .8) + np.array([.2, .13, .16]) * edge * .8
    return Image.fromarray((img * 255).astype('uint8')).resize((size, size), Image.LANCZOS)

def sheet(models, path, cols=5, size=300, title=None, **kw):
    rows = (len(models) + cols - 1) // cols; top = 40 if title else 0
    im = Image.new('RGB', (cols * size, rows * (size + 22) + top), (232, 238, 246)); dr = ImageDraw.Draw(im)
    if title: dr.text((12, 12), title, fill=(40, 40, 60))
    for i, m in enumerate(models):
        x, y = (i % cols) * size, (i // cols) * (size + 22) + top
        im.paste(render(m, size, **kw), (x, y)); dr.text((x + 8, y + size + 4), f"{m.name}  ({m.tris()} tris)", fill=(40, 40, 60))
    im.save(path); return path
