import argparse
import json
import os
import shutil
import subprocess
import tempfile

from PIL import Image, ImageDraw

COLOURS = {
    "O": (170, 170, 170), "name": (40, 120, 220), "quantity": (0, 160, 90),
    "unit": (0, 200, 160), "unitPrice": (230, 120, 0), "lineNet": (200, 40, 40),
    "vat": (150, 60, 200), "articleId": (110, 110, 0), "invoiceNumber": (220, 0, 140),
    "invoiceDate": (0, 90, 200), "supplier": (0, 60, 120), "netTotal": (200, 0, 60),
    "grossTotal": (140, 0, 0),
}


def load(path, scale):
    if not path.lower().endswith(".pdf"):
        return Image.open(path).convert("RGB")
    work = tempfile.mkdtemp(prefix="overlay-")
    try:
        prefix = os.path.join(work, "p")
        subprocess.run([shutil.which("pdftoppm") or "pdftoppm", "-r", str(96 * scale), "-png",
                        path, prefix], check=True, capture_output=True)
        page = sorted(f for f in os.listdir(work) if f.endswith(".png"))[0]
        return Image.open(os.path.join(work, page)).convert("RGB")
    finally:
        shutil.rmtree(work, ignore_errors=True)


def draw(directory, out, skip_o):
    truth = json.load(open(os.path.join(directory, "truth.json"), encoding="utf-8"))
    made = []
    for page in truth["pages"]:
        image = load(os.path.join(directory, page["image"]), truth["scale"])
        if image.size != (page["width"], page["height"]):
            image = image.resize((page["width"], page["height"]))
        canvas = ImageDraw.Draw(image, "RGBA")
        for word in page["words"]:
            if skip_o and word["f"] == "O":
                continue
            x, y, w, h = word["box"]
            colour = COLOURS.get(word["f"], (0, 0, 0))
            canvas.rectangle([x, y, x + w, y + h], outline=colour + (255,), width=2)
            canvas.rectangle([x, y, x + w, y + h], fill=colour + (36,))
        path = os.path.join(out, f"{os.path.basename(directory)}-{page['image']}.overlay.png")
        image.save(path)
        made.append(path)
    return made


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("directory")
    ap.add_argument("--out", default=".")
    ap.add_argument("--all-words", action="store_true")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    for path in draw(args.directory, args.out, not args.all_words):
        print(path)


if __name__ == "__main__":
    main()
