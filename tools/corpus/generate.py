import argparse
import concurrent.futures as futures
import hashlib
import json
import os
import random
import shutil
import sys
import tempfile
import time
import traceback

import numpy as np
from PIL import Image

import content
import degrade
import layout
import render

PROFILE_MIX = ["scan_clean", "scan_worn", "photocopy", "washed", "scan_clean", "photo",
               "faded", "scan_worn", "dark", "crisp", "fax", "low_ink"]
FORMATS = ["jpg", "jpg", "jpg", "pdf"]


def clip(box, size, keep=0.55):
    x, y, w, h = box
    x0, y0 = max(0.0, x), max(0.0, y)
    x1, y1 = min(float(size[0]), x + w), min(float(size[1]), y + h)
    if x1 <= x0 or y1 <= y0:
        return None
    if (x1 - x0) * (y1 - y0) < keep * w * h:
        return None
    return [x0, y0, x1 - x0, y1 - y0]


def digest(*parts):
    return hashlib.sha256("|".join(str(p) for p in parts).encode()).hexdigest()[:12]


def split_of(template_id, val_share):
    return "val" if int(digest("split", template_id), 16) % 1000 < val_share * 1000 else "train"


def save(image, path, fmt, quality, scale, mono):
    if mono:
        image = image.convert("L")
    if fmt == "jpg":
        image.save(path, "JPEG", quality=quality, subsampling=2, optimize=True)
    elif fmt == "pdf":
        image.save(path, "PDF", resolution=96.0 * scale, quality=quality)
    else:
        image.save(path, "PNG", optimize=True)


def variation(invoice, meta, seed, index, out_dir, scale, val_share, profile, formats):
    rng = random.Random(seed)
    nprng = np.random.default_rng(int(digest(seed), 16) % (2 ** 32))
    template_id = digest("tpl", seed)
    spec = layout.fit(layout.template(template_id), meta)
    choices = [f for f in FORMATS if f in formats] or list(formats)
    if profile == "crisp" and "png" in formats:
        choices = ["png"]
    fmt = rng.choice(choices)
    mono = degrade.PROFILES[profile]["gray"] >= 0.95
    name = f"v{index:02d}-{profile}-{template_id}"
    path = os.path.join(out_dir, name)
    os.makedirs(path, exist_ok=True)
    work = tempfile.mkdtemp(prefix="corpus-")
    try:
        doc, probes, used = render.build(spec, invoice, meta, seed, work)
        shots = render.shoot(doc, probes, work, scale)
        params = degrade.jitter(nprng, profile)
        pages = []
        written = 0
        for i, (png, probe) in enumerate(shots):
            with Image.open(png) as raw:
                image = raw.convert("RGB")
            if rng.random() < 0.12:
                image = degrade.stamp(image, nprng)
            if rng.random() < 0.08:
                image = degrade.scribble(image, nprng)
            image, matrix = degrade.apply(image, params, nprng)
            boxes = [[w["box"][0] * scale, w["box"][1] * scale,
                      w["box"][2] * scale, w["box"][3] * scale] for w in probe["words"]]
            warped = degrade.warp_boxes(matrix, boxes)
            kept = [(w, clip(box, image.size)) for w, box in zip(probe["words"], warped)]
            kept = [(w, box) for w, box in kept if box]
            regions = [(r, clip(box, image.size, keep=0.2)) for r, box in zip(
                probe["regions"], degrade.warp_boxes(matrix, [
                    [r["box"][0] * scale, r["box"][1] * scale,
                     r["box"][2] * scale, r["box"][3] * scale] for r in probe["regions"]]))]
            regions = [(r, box) for r, box in regions if box]
            page_name = f"page-{i + 1}.{fmt}"
            page_path = os.path.join(path, page_name)
            save(image, page_path, fmt, rng.randint(62, 82), scale, mono)
            written += os.path.getsize(page_path)
            pages.append({
                "image": page_name,
                "width": image.width,
                "height": image.height,
                "words": [{"t": w["t"], "f": w["f"], "l": w["l"],
                           "box": [round(v, 1) for v in box]} for w, box in kept],
                "regions": [{"role": r["role"], "l": r["l"], "box": [round(v, 1) for v in box]}
                            for r, box in regions],
            })
        record = {
            "template": template_id,
            "split": split_of(template_id, val_share),
            "profile": profile,
            "format": fmt,
            "mono": mono,
            "scale": scale,
            "degradation": {k: (round(v, 3) if isinstance(v, float) else v)
                            for k, v in params.items()},
            "layout": {k: used[k] for k in ("page_format", "columns", "glue_unit", "no_header_row", "header_style",
                                            "table_style", "align", "font", "size", "narrow_name",
                                            "logo", "address_corner", "meta_style", "totals_style",
                                            "footer", "uppercase_headers", "group_headings",
                                            "second_row_details", "meta_table", "meta_place",
                                            "sender_place", "wordmark_caps", "title_style",
                                            "logo_side", "pos_format", "cell_currency",
                                            "totals_side", "pageno_place", "decor_qr")},
            "headers": {c: used["headers"][c] for c in used["columns"]},
            "pages": pages,
        }
        with open(os.path.join(path, "truth.json"), "w", encoding="utf-8") as f:
            json.dump(record, f, ensure_ascii=False, indent=1)
        return {"name": name, "template": template_id, "split": record["split"],
                "profile": profile, "format": fmt, "pages": len(pages), "bytes": written}
    finally:
        shutil.rmtree(work, ignore_errors=True)


def one(args):
    index, seed, root, variations, scale, val_share, formats, page_mix = args
    if page_mix:
        layout.PAGE_MIX = page_mix
    invoice, meta = content.make(digest(seed, "inv", index), index)
    name = f"inv-{index:06d}"
    out_dir = os.path.join(root, name)
    os.makedirs(out_dir, exist_ok=True)
    invoice = dict(invoice)
    invoice["fileName"] = name
    with open(os.path.join(out_dir, "expected.json"), "w", encoding="utf-8") as f:
        json.dump(invoice, f, ensure_ascii=False, indent=1)
    with open(os.path.join(out_dir, "source.json"), "w", encoding="utf-8") as f:
        json.dump({"category": meta["category"], "kind": meta["kind"], "defect": meta["defect"],
                   "supplier": meta["supplier"], "customer": meta["customer"],
                   "lines": len(invoice["lines"])}, f, ensure_ascii=False, indent=1)
    order = list(PROFILE_MIX)
    random.Random(digest(seed, "profiles", index)).shuffle(order)
    made = []
    for v in range(variations):
        try:
            made.append(variation(invoice, meta, digest(seed, index, v), v, out_dir, scale,
                                  val_share, order[v % len(order)], formats))
        except Exception:
            traceback.print_exc()
    return {"invoice": name, "category": meta["category"], "kind": meta["kind"],
            "lines": len(invoice["lines"]), "variations": made,
            "expected_variations": variations}


def parse_page_mix(text):
    """'letter=38,a5=14' -> the (names, weights) pair layout.PAGE_MIX expects."""
    if not text.strip():
        return None
    names, weights = [], []
    for part in text.split(","):
        name, _, weight = part.partition("=")
        name = name.strip()
        if name not in layout.PAGE_FORMATS:
            raise SystemExit(f"unknown page format {name!r}; "
                             f"known: {', '.join(sorted(layout.PAGE_FORMATS))}")
        names.append(name)
        weights.append(float(weight) if weight.strip() else 1.0)
    return (names, weights)


def human(size):
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if size < 1024 or unit == "TB":
            return f"{size:,.0f} {unit}" if unit == "B" else f"{size:,.1f} {unit}"
        size /= 1024


def clock(seconds):
    seconds = int(max(0, seconds))
    if seconds < 60:
        return f"{seconds}s"
    if seconds < 3600:
        return f"{seconds // 60}m {seconds % 60:02d}s"
    return f"{seconds // 3600}h {(seconds % 3600) // 60:02d}m"


class Progress:
    def __init__(self, total, width=28):
        self.total = total
        self.width = width
        self.start = time.time()
        self.done = 0
        self.pages = 0
        self.bytes = 0
        self.failed = 0
        self.tty = sys.stderr.isatty()

    def update(self, result):
        self.done += 1
        for v in result["variations"]:
            self.pages += v["pages"]
            self.bytes += v.get("bytes", 0)
        self.failed += result["expected_variations"] - len(result["variations"])
        self.draw()

    def draw(self, final=False):
        elapsed = time.time() - self.start
        rate = self.pages / elapsed if elapsed > 0 else 0
        left = (self.total - self.done) / (self.done / elapsed) if self.done and elapsed else 0
        filled = int(self.width * self.done / self.total) if self.total else self.width
        bar = "\u2588" * filled + "\u2591" * (self.width - filled)
        line = (f"[{bar}] {self.done}/{self.total} invoices  {self.pages:,} pages  "
                f"{rate:.1f} pg/s  {human(self.bytes)}  "
                + (f"elapsed {clock(elapsed)}" if final else f"eta {clock(left)}"))
        if self.failed:
            line += f"  {self.failed} failed"
        if self.tty:
            sys.stderr.write("\r\x1b[2K" + line)
            if final:
                sys.stderr.write("\n")
            sys.stderr.flush()
        elif final or self.done % 25 == 0:
            print(line, flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--count", type=int, default=10)
    ap.add_argument("--variations", type=int, default=6)
    ap.add_argument("--seed", default="umsatz-1")
    ap.add_argument("--scale", type=int, default=3, choices=[2, 3, 4])
    ap.add_argument("--val-share", type=float, default=0.15)
    ap.add_argument("--workers", type=int, default=os.cpu_count() or 4)
    ap.add_argument("--start", type=int, default=0)
    ap.add_argument("--verbose", action="store_true", help="one line per invoice")
    ap.add_argument("--formats", default="png,jpg,pdf",
                    help="comma-separated subset of png,jpg,pdf")
    ap.add_argument("--page-mix", default="",
                    help="override the page-format weights, e.g. "
                         "'letter=38,a5=14,b5=12,legal=14,folio=12,a4_land=10'. "
                         "Names come from layout.PAGE_FORMATS. Used to extend an "
                         "existing A4-only corpus with the other formats.")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    formats = tuple(f.strip() for f in args.formats.split(",") if f.strip())
    page_mix = parse_page_mix(args.page_mix)
    jobs = [(i, args.seed, args.out, args.variations, args.scale, args.val_share, formats, page_mix)
            for i in range(args.start, args.start + args.count)]
    done = []
    bar = Progress(len(jobs))
    bar.draw()
    with futures.ProcessPoolExecutor(max_workers=args.workers) as pool:
        for result in pool.map(one, jobs):
            done.append(result)
            if args.verbose:
                print(f"{result['invoice']} {result['category']:<12} {result['kind']:<14} "
                      f"{result['lines']:>3} lines  {len(result['variations'])} variations",
                      flush=True)
            bar.update(result)
    bar.draw(final=True)
    manifest = {
        "seed": args.seed,
        "count": len(done),
        "variations": args.variations,
        "scale": args.scale,
        "dpi": 96 * args.scale,
        "val_share": args.val_share,
        "formats": list(formats),
        "page_mix": args.page_mix or "default",
        "invoices": done,
    }
    with open(os.path.join(args.out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    templates = {v["template"]: v["split"] for d in done for v in d["variations"]}
    print(f"{len(done)} invoices, {sum(len(d['variations']) for d in done)} variations, "
          f"{bar.pages:,} pages, {len(templates)} templates "
          f"({sum(1 for s in templates.values() if s == 'val')} val), {human(bar.bytes)}")


if __name__ == "__main__":
    main()
