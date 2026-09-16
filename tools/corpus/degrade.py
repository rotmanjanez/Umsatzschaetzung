import io
import math
import random

import numpy as np
from PIL import Image, ImageFilter

def profile(**kw):
    base = dict(rotate=0.0, skew=0.0, blur=0.0, noise=0.0, jpeg=0, gamma=1.0, contrast=1.0,
                speckle=0.0, vignette=0.0, texture=0.0, shadow=0.0, edge=0.0, gray=0.0, bilevel=0.0,
                black=0.0, white=255.0,
                ink_erode=0.0, ink_bands=0.0, ink_blotch=0.0, ink_dropout=0.0)
    base.update(kw)
    return base


PROFILES = {
    "crisp": profile(),
    "scan_clean": profile(rotate=0.5, skew=0.2, blur=0.7, noise=4.0, jpeg=86, gamma=1.03,
                          speckle=0.00008, vignette=0.05, texture=0.018, gray=0.35),
    "scan_worn": profile(rotate=1.8, skew=0.6, blur=1.5, noise=10.0, jpeg=58, gamma=1.15, contrast=0.9,
                         speckle=0.0006, vignette=0.2, texture=0.05, edge=0.4, gray=0.6),
    "photocopy": profile(rotate=1.4, skew=0.5, blur=1.9, noise=13.0, jpeg=72, gamma=1.45, contrast=1.5,
                         speckle=0.0014, vignette=0.3, texture=0.035, edge=0.9, gray=1.0),
    "photo": profile(rotate=2.4, skew=1.0, blur=1.8, noise=8.0, jpeg=52, gamma=0.9, contrast=0.88,
                     speckle=0.0003, vignette=0.24, texture=0.03, shadow=0.6),
    "fax": profile(rotate=1.2, skew=0.4, blur=2.2, noise=16.0, gamma=1.5, contrast=1.9,
                   speckle=0.0024, vignette=0.1, texture=0.015, edge=0.6, gray=1.0, bilevel=1.0),
    "faded": profile(rotate=1.0, skew=0.3, blur=1.2, noise=5.0, jpeg=74, gamma=0.85,
                     speckle=0.0002, vignette=0.06, texture=0.02, gray=0.8,
                     black=152.0, white=253.0),
    "dark": profile(rotate=1.3, skew=0.4, blur=1.4, noise=9.0, jpeg=60, gamma=1.1,
                    speckle=0.0005, vignette=0.32, texture=0.04, shadow=0.35, gray=0.7,
                    black=4.0, white=143.0),
    "low_ink": profile(rotate=1.1, skew=0.35, blur=1.1, noise=7.0, jpeg=70, gamma=1.08, contrast=0.95,
                       speckle=0.0004, vignette=0.1, texture=0.03, gray=0.75,
                       ink_erode=0.55, ink_bands=0.45, ink_blotch=0.38, ink_dropout=0.018),
    "washed": profile(rotate=1.5, skew=0.5, blur=1.6, noise=11.0, jpeg=56, gamma=1.05,
                      speckle=0.0007, vignette=0.14, texture=0.05, edge=0.3, gray=0.9,
                      black=92.0, white=186.0),
}


def jitter(rng, profile, amount=1.0):
    p = dict(PROFILES[profile])
    for key in ("rotate", "skew"):
        p[key] = rng.uniform(-p[key], p[key]) * amount
    for key in ("blur", "noise", "speckle", "vignette", "texture", "shadow", "edge"):
        p[key] *= rng.uniform(0.6, 1.25)
    for key in ("ink_erode", "ink_bands", "ink_blotch", "ink_dropout"):
        p[key] *= rng.uniform(0.55, 1.4)
    if p["gray"] < 0.95:
        p["gray"] = min(0.94, p["gray"] * rng.uniform(0.6, 1.25)) * amount
    if p["jpeg"]:
        p["jpeg"] = max(30, min(95, int(p["jpeg"] * rng.uniform(0.9, 1.1))))
    p["gamma"] *= rng.uniform(0.94, 1.06)
    p["contrast"] *= rng.uniform(0.92, 1.08)
    spread = p["white"] - p["black"]
    shift = rng.uniform(-0.06, 0.06) * 255
    squeeze = rng.uniform(0.88, 1.12)
    mid = (p["white"] + p["black"]) / 2 + shift
    p["black"] = max(0.0, mid - spread * squeeze / 2)
    p["white"] = min(255.0, mid + spread * squeeze / 2)
    return p


def matrix(p, w, h):
    a = math.radians(p["rotate"])
    shear = math.tan(math.radians(p["skew"]))
    cos, sin = math.cos(a), math.sin(a)
    m = np.array([[cos, -sin + shear, 0.0], [sin, cos, 0.0], [0.0, 0.0, 1.0]])
    cx, cy = w / 2.0, h / 2.0
    centre = np.array([[1, 0, cx], [0, 1, cy], [0, 0, 1.0]])
    back = np.array([[1, 0, -cx], [0, 1, -cy], [0, 0, 1.0]])
    return centre @ m @ back


def warp_boxes(m, boxes):
    out = []
    for x, y, w, h in boxes:
        pts = np.array([[x, y, 1], [x + w, y, 1], [x + w, y + h, 1], [x, y + h, 1]]).T
        q = (m @ pts)[:2]
        x0, y0 = q[0].min(), q[1].min()
        out.append([float(x0), float(y0), float(q[0].max() - x0), float(q[1].max() - y0)])
    return out


def grain(rng, shape, strength):
    h, w = shape
    fine = rng.standard_normal(shape, dtype=np.float32) * (255 * strength)
    small = (max(4, w // 12), max(4, h // 12))
    blotch = rng.standard_normal((small[1], small[0]), dtype=np.float32) * (255 * strength * 0.3)
    blotch = np.asarray(Image.fromarray((blotch + 128).clip(0, 255).astype(np.uint8), "L")
                        .resize((w, h), Image.BICUBIC), dtype=np.float32) - 128.0
    return fine + blotch


def vignette_gain(shape, strength):
    h, w = shape
    xs = np.linspace(-1.0, 1.0, w, dtype=np.float32)[None, :]
    ys = np.linspace(-1.0, 1.0, h, dtype=np.float32)[:, None]
    r = np.sqrt(xs ** 2 + ys ** 2) * np.float32(1.0 / math.sqrt(2.0))
    return (1.0 - strength * r ** 2.2).clip(0.0, 1.0)


def edge_gain(shape, strength, rng):
    h, w = shape
    gain = np.ones(shape, dtype=np.float32)
    band = max(4, int(min(w, h) * 0.012 * strength))
    for side in range(4):
        if rng.random() < 0.45:
            continue
        thickness = max(2, int(band * rng.uniform(0.5, 1.6)))
        shade = np.float32(rng.uniform(0.25, 0.75))
        if side == 0:
            gain[:thickness] *= shade
        elif side == 1:
            gain[-thickness:] *= shade
        elif side == 2:
            gain[:, :thickness] *= shade
        else:
            gain[:, -thickness:] *= shade
    return gain


def ink_coverage(rng, shape, blotch, bands):
    """Wie viel Farbe je Pixel ankommt, 0 = gar keine, 1 = voll.

    Zwei Ursachen: eine grobfleckige Komponente (leere Kartusche, trockene Walze)
    und waagrechte Streifen (Druckkopf, Walzenumfang).
    """
    h, w = shape
    cov = np.ones(shape, dtype=np.float32)
    if blotch > 0.001:
        # Zwei Oktaven: grob für leere Stellen auf der Seite, fein damit einzelne
        # Striche aufbrechen statt nur gleichmäßig blass zu werden.
        for divisor, share in ((70, 0.62), (340, 0.38)):
            small = (max(3, w // divisor), max(3, h // divisor))
            low = rng.standard_normal((small[1], small[0]), dtype=np.float32)
            low = np.asarray(Image.fromarray((low * 70 + 128).clip(0, 255).astype(np.uint8), "L")
                             .resize((w, h), Image.BICUBIC), dtype=np.float32)
            cov -= blotch * share * ((128.0 - low) / 128.0).clip(0.0, 1.0)
    if bands > 0.001:
        ys = np.arange(h, dtype=np.float32)
        streak = np.zeros(h, dtype=np.float32)
        for _ in range(int(rng.integers(2, 6))):
            period = float(rng.uniform(h / 26.0, h / 5.0))
            streak += np.sin(ys * (2.0 * math.pi / period) + float(rng.uniform(0.0, 6.28)))
        streak = (streak / 3.0 + rng.standard_normal(h, dtype=np.float32) * 0.22).clip(0.0, 1.0)
        cov -= bands * streak[:, None]
    return cov.clip(0.04, 1.0)


def starve(image, p, rng):
    """Schlechter Druck, bevor überhaupt gescannt wird: dünne, aufgebrochene Striche.

    Greift nur die Farbe an, nicht das Papier — `(255 - arr)` ist auf Weiß null,
    also bleibt der Untergrund stehen und nur die Schrift wird blass. Das
    unterscheidet den Modus von `faded`, das den ganzen Tonwertumfang staucht.
    """
    arr = np.asarray(image, dtype=np.float32)
    if p["ink_erode"] > 0.01:
        # Ein Maximumfilter hellt auf; auf dünnen Strichen frisst er sie an.
        thin = np.asarray(image.filter(ImageFilter.MaxFilter(3)), dtype=np.float32)
        arr = arr + (thin - arr) * min(1.0, p["ink_erode"])
    cov = ink_coverage(rng, arr.shape[:2], p["ink_blotch"], p["ink_bands"])
    arr = arr + (255.0 - arr) * (1.0 - (cov if arr.ndim == 2 else cov[:, :, None]))
    if p["ink_dropout"] > 1e-6:
        holes = rng.random(arr.shape[:2], dtype=np.float32) < p["ink_dropout"]
        if arr.ndim == 3:
            holes = holes[:, :, None]
        arr = np.where(holes, 255.0 - (255.0 - arr) * 0.12, arr)
    np.clip(arr, 0, 255, out=arr)
    return Image.fromarray(arr.astype(np.uint8), image.mode)


def apply(image, p, rng):
    w, h = image.size
    m = matrix(p, w, h)
    mono = p["gray"] >= 0.95
    if p["rotate"] or p["skew"]:
        inv = np.linalg.inv(m)
        image = image.transform((w, h), Image.AFFINE, tuple(inv[:2].flatten()),
                                resample=Image.BICUBIC, fillcolor=(255, 255, 255))
    if mono:
        image = image.convert("L")
    elif p["gray"] > 0.05:
        image = Image.blend(image, image.convert("L").convert("RGB"), min(1.0, p["gray"]))
    if p["ink_erode"] > 0.01 or p["ink_bands"] > 0.001 or p["ink_blotch"] > 0.001:
        # Vor dem Weichzeichnen: der Druck war schon schlecht, bevor der Scanner ihn sah.
        image = starve(image, p, rng)
    if p["blur"] > 0.02:
        image = image.filter(ImageFilter.GaussianBlur(p["blur"]))

    arr = np.asarray(image, dtype=np.uint8).astype(np.float32)
    plane = arr if arr.ndim == 2 else arr[:, :, 0]
    shape = plane.shape

    gain = None
    if p["shadow"] > 0.01:
        grad = np.linspace(1.0 - p["shadow"] * 0.55, 1.0, w, dtype=np.float32)[None, :]
        gain = np.repeat(grad[:, ::-1] if rng.random() < 0.5 else grad, h, axis=0)
    if p["vignette"] > 0.005:
        mask = vignette_gain(shape, p["vignette"])
        gain = mask if gain is None else gain * mask
    if p["edge"] > 0.02:
        bands = edge_gain(shape, p["edge"], rng)
        gain = bands if gain is None else gain * bands

    add = None
    if p["texture"] > 0.001:
        add = grain(rng, shape, p["texture"])
    if p["noise"] > 0.1:
        hiss = rng.standard_normal(shape, dtype=np.float32) * p["noise"]
        add = hiss if add is None else add + hiss

    if gain is not None or add is not None:
        work = plane if arr.ndim == 2 else arr
        if gain is not None:
            work = work * (gain if arr.ndim == 2 else gain[:, :, None])
        if add is not None:
            work = work + (add if arr.ndim == 2 else add[:, :, None])
        arr = work

    if abs(p["gamma"] - 1.0) > 0.01:
        np.clip(arr, 0, 255, out=arr)
        arr = 255.0 * np.power(arr * (1.0 / 255.0), p["gamma"], dtype=np.float32)
    if abs(p["contrast"] - 1.0) > 0.01:
        arr = 128.0 + (arr - 128.0) * p["contrast"]
    if p["black"] > 1.0 or p["white"] < 254.0:
        arr = p["black"] + arr * ((p["white"] - p["black"]) / 255.0)
    if p["bilevel"] > 0.5:
        arr = np.where(arr < rng.uniform(140, 190), 0.0, 255.0)

    np.clip(arr, 0, 255, out=arr)
    image = Image.fromarray(arr.astype(np.uint8))

    if p["speckle"] > 1e-6:
        image = speckle(image, p["speckle"], rng)
    if p["bilevel"] > 0.5:
        image = image.filter(ImageFilter.GaussianBlur(0.5))
    if p["jpeg"]:
        buf = io.BytesIO()
        image.save(buf, "JPEG", quality=p["jpeg"])
        buf.seek(0)
        image = Image.open(buf)
        image.load()
    return image, m


def speckle(image, density, rng):
    arr = np.asarray(image).copy()
    h, w = arr.shape[:2]
    flat = arr.ndim == 2
    count = int(w * h * density)
    if count <= 0:
        return image
    ys = rng.integers(0, h, count)
    xs = rng.integers(0, w, count)
    dark = rng.random(count) < 0.72
    light = ~dark
    for pick, lo, hi in ((dark, 0, 70), (light, 200, 256)):
        n = int(pick.sum())
        if not n:
            continue
        values = rng.integers(lo, hi, n if flat else (n, 1))
        arr[ys[pick], xs[pick]] = values if flat else values
    return Image.fromarray(arr)


def stamp(image, rng, accent=(150, 30, 40)):
    from PIL import ImageDraw
    w, h = image.size
    layer = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    r = int(min(w, h) * rng.uniform(0.06, 0.1))
    cx = int(rng.uniform(0.25, 0.8) * w)
    cy = int(rng.uniform(0.45, 0.85) * h)
    colour = accent + (rng.integers(90, 170),)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=colour, width=max(2, r // 12))
    draw.ellipse([cx - int(r * 0.82), cy - int(r * 0.82), cx + int(r * 0.82), cy + int(r * 0.82)],
                 outline=colour, width=max(1, r // 20))
    draw.line([cx - int(r * 0.6), cy, cx + int(r * 0.6), cy], fill=colour, width=max(2, r // 14))
    layer = layer.rotate(rng.uniform(-25, 25), resample=Image.BICUBIC, center=(cx, cy))
    layer = layer.filter(ImageFilter.GaussianBlur(rng.uniform(0.4, 1.4)))
    return Image.alpha_composite(image.convert("RGBA"), layer).convert("RGB")


def scribble(image, rng):
    from PIL import ImageDraw
    w, h = image.size
    layer = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    ink = (rng.integers(10, 60), rng.integers(20, 80), rng.integers(90, 190), rng.integers(150, 220))
    x = rng.uniform(0.08, 0.7) * w
    y = rng.uniform(0.3, 0.9) * h
    points = [(x, y)]
    for _ in range(rng.integers(4, 10)):
        x += rng.uniform(-0.03, 0.09) * w
        y += rng.uniform(-0.02, 0.02) * h
        points.append((x, y))
    draw.line(points, fill=ink, width=max(2, int(min(w, h) * 0.003)), joint="curve")
    layer = layer.filter(ImageFilter.GaussianBlur(0.7))
    return Image.alpha_composite(image.convert("RGBA"), layer).convert("RGB")
