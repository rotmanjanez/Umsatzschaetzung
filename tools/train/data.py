"""page.jsonl -> 512-subword windows with boxes, word labels and row pooling."""
import random

import numpy as np
import torch

from model import quantise_box
from schema import LABEL_ID, ROLE_ID, read_jsonl

IGNORE = -100
MAX_LEN = 512
OVERLAP = 128
MAX_ROWS = 96

CONFUSABLE = {"0": "O", "O": "0", "1": "l", "l": "1", "I": "1", ",": ".", ".": ",",
              "5": "S", "S": "5", "8": "B", "B": "8"}


def augment(words, h, rng):
    """The OCR failures the corpus does not produce on its own (models.md §2)."""
    out = []
    for w in words:
        if rng.random() < 0.015:
            continue
        x, y, bw, bh = w["box"]
        j = 0.01 * h
        box = [x + rng.uniform(-j, j), y + rng.uniform(-j, j), bw, bh]
        t = w["t"]
        if len(t) > 1 and rng.random() < 0.02:
            i = rng.randrange(len(t))
            t = t[:i] + CONFUSABLE.get(t[i], t[i]) + t[i + 1:]
        out.append({**w, "t": t, "box": box})

    i = 0
    while i < len(out):
        w = out[i]
        if len(w["t"]) > 3 and rng.random() < 0.005:
            c = rng.randrange(1, len(w["t"]))
            x, y, bw, bh = w["box"]
            frac = c / len(w["t"])
            out[i:i + 1] = [{**w, "t": w["t"][:c], "box": [x, y, bw * frac, bh]},
                            {**w, "t": w["t"][c:], "box": [x + bw * frac, y, bw * (1 - frac), bh]}]
            i += 2
            continue
        if (i + 1 < len(out) and out[i + 1]["row"] == w["row"] and rng.random() < 0.005):
            n = out[i + 1]
            x, y, bw, bh = w["box"]
            nx, ny, nbw, nbh = n["box"]
            out[i:i + 2] = [{**w, "t": w["t"] + n["t"],
                             "box": [x, min(y, ny), max(x + bw, nx + nbw) - x, max(bh, nbh)]}]
        i += 1
    return out


def encode_page(page, tk, rng=None):
    """One page to flat per-subword arrays; the first subword of a word carries its label."""
    words = page["words"]
    if rng is not None:
        words = augment(words, page["h"], rng)

    ids, boxes, labels, rows = [], [], [], []
    for w in words:
        sub = tk.encode(" " + w["t"], add_special_tokens=False)
        if not sub:
            continue
        box = quantise_box(w["box"], page["w"], page["h"])
        for k, s in enumerate(sub):
            ids.append(s)
            boxes.append(box)
            labels.append(LABEL_ID.get(w.get("field", "O"), 0) if k == 0 else IGNORE)
            rows.append(w["row"] if k == 0 else -1)

    role_of = {}
    for w in words:
        role_of.setdefault(w["row"], ROLE_ID.get(w.get("role", "header"), 0))
    return np.array(ids), np.array(boxes), np.array(labels), np.array(rows), role_of


def windows(page, tk, rng=None, max_len=MAX_LEN, overlap=OVERLAP):
    ids, boxes, labels, rows, role_of = encode_page(page, tk, rng)
    body = max_len - 2
    step = max(1, body - overlap)
    starts = list(range(0, max(1, len(ids)), step))
    starts = [s for i, s in enumerate(starts) if i == 0 or s < len(ids)]
    for s in starts:
        e = min(s + body, len(ids))
        yield _window(ids[s:e], boxes[s:e], labels[s:e], rows[s:e], role_of, s, tk)
        if e == len(ids):
            break


def _window(ids, boxes, labels, rows, role_of, offset, tk):
    bos, eos = tk.bos_token_id, tk.eos_token_id
    edge = np.zeros((1, 4), dtype=np.int64)
    w = {
        "input_ids": np.concatenate([[bos], ids, [eos]]).astype(np.int64),
        "bbox": np.concatenate([edge, boxes.reshape(-1, 4), edge]).astype(np.int64),
        "labels": np.concatenate([[IGNORE], labels, [IGNORE]]).astype(np.int64),
        "offset": offset,
    }
    present = [r for r in dict.fromkeys(rows.tolist()) if r >= 0][:MAX_ROWS]
    pool = np.zeros((len(present), len(w["input_ids"])), dtype=np.float32)
    for i, r in enumerate(present):
        at = np.flatnonzero(rows == r) + 1
        pool[i, at] = 1.0 / len(at)
    w["row_pool"] = pool
    w["role_labels"] = np.array([role_of[r] for r in present], dtype=np.int64)
    return w


class Windows(torch.utils.data.Dataset):
    def __init__(self, path, tk, split=None, augment=False, seed=0):
        self.pages = [p for p in read_jsonl(path) if split is None or p.get("split") == split]
        self.tk, self.augment, self.seed = tk, augment, seed
        self.index = []
        for i, p in enumerate(self.pages):
            n = sum(1 for _ in windows(p, tk))
            self.index += [(i, k) for k in range(n)]

    def __len__(self):
        return len(self.index)

    def __getitem__(self, i):
        page, k = self.index[i]
        rng = random.Random(self.seed * 1000003 + i) if self.augment else None
        # augmentation changes the subword count, so the window count can differ
        # from the one the index was built with
        ws = list(windows(self.pages[page], self.tk, rng))
        return ws[min(k, len(ws) - 1)]


def collate(batch, pad_id=1):
    t = max(len(b["input_ids"]) for b in batch)
    r = max(1, max(len(b["role_labels"]) for b in batch))
    out = {
        "input_ids": torch.full((len(batch), t), pad_id, dtype=torch.long),
        "bbox": torch.zeros(len(batch), t, 4, dtype=torch.long),
        "attention_mask": torch.zeros(len(batch), t, dtype=torch.long),
        "labels": torch.full((len(batch), t), IGNORE, dtype=torch.long),
        "row_pool": torch.zeros(len(batch), r, t),
        "role_labels": torch.full((len(batch), r), IGNORE, dtype=torch.long),
    }
    for i, b in enumerate(batch):
        n = len(b["input_ids"])
        out["input_ids"][i, :n] = torch.from_numpy(b["input_ids"])
        out["bbox"][i, :n] = torch.from_numpy(b["bbox"])
        out["attention_mask"][i, :n] = 1
        out["labels"][i, :n] = torch.from_numpy(b["labels"])
        m = len(b["role_labels"])
        if m:
            out["row_pool"][i, :m, :n] = torch.from_numpy(b["row_pool"])
            out["role_labels"][i, :m] = torch.from_numpy(b["role_labels"])
    return out


def class_weights(dataset, n, key, cap=20.0):
    counts = np.zeros(n)
    for i in range(len(dataset)):
        v = dataset[i][key]
        for x in v[v >= 0]:
            counts[x] += 1
    w = 1.0 / np.sqrt(np.maximum(counts, 1))
    w = w / w[counts > 0].min()
    return torch.tensor(np.minimum(w, cap), dtype=torch.float32)


def stitch(page_windows, logits, length):
    """Average overlapping windows back onto one per-subword sequence."""
    acc = np.zeros((length, logits[0].shape[-1]))
    hits = np.zeros(length)
    for w, l in zip(page_windows, logits):
        body = l[1:-1]
        s = w["offset"]
        acc[s:s + len(body)] += body
        hits[s:s + len(body)] += 1
    return acc / np.maximum(hits, 1)[:, None]
