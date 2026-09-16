"""Shared constants and IO for the model-A pipeline."""
import json
from pathlib import Path

FIELDS = ["quantity", "unit", "name", "articleId", "unitPrice", "lineNet", "vat",
          "invoiceNumber", "invoiceDate", "supplier", "netTotal", "grossTotal"]
LABELS = ["O"] + FIELDS
LABEL_ID = {n: i for i, n in enumerate(LABELS)}

ROLES = ["header", "column-header", "line-item", "line-wrap", "continuation",
         "total", "footer", "group", "carry"]
ROLE_ID = {n: i for i, n in enumerate(ROLES)}


def variations(root):
    """Yield (truth_path, truth_dict) for every rendered variation under root."""
    for p in sorted(Path(root).glob("*/*/truth.json")):
        yield p, json.loads(p.read_text())


def ocr_pages(variation_dir, page_index):
    """The OCR dump for one page, or None when it was never produced."""
    p = Path(variation_dir) / f"page-{page_index + 1}.ocr.json"
    if not p.exists():
        return None
    return json.loads(p.read_text())["pages"][0]


def write_jsonl(path, rows):
    with open(path, "w") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False, separators=(",", ":")) + "\n")


def read_jsonl(path):
    with open(path) as f:
        for line in f:
            if line.strip():
                yield json.loads(line)
