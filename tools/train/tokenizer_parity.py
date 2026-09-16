"""Dump what a C# byte-level BPE needs, and the cases that prove it matches.

`dump` writes the artefacts plus expected ids for every distinct OCR word in the
corpus. The C# side reads cases.jsonl, writes its own ids into the same file
shape, and `check` diffs them. A tokenizer mismatch between training and
inference is silent and costs accuracy that gets blamed on the model.

`selfcheck` runs Bpe below, a byte-level BPE built from the dumped artefacts
alone and importing nothing from transformers. It is the spec the C# port
follows: if it matches cases.jsonl the artefacts are sufficient.
"""
import argparse
import json
import sys
from collections import Counter
from functools import lru_cache
from pathlib import Path

import regex

from model import GOTTBERT, tokenizer

# GPT-2's pre-tokenizer split, needed verbatim on the C# side.
PRETOKEN_RE = r"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+"

def byte_to_unicode():
    """GPT-2's reversible byte alphabet: every byte to a printable codepoint."""
    printable = ([*range(ord("!"), ord("~") + 1)] + [*range(ord("\u00a1"), ord("\u00ac") + 1)]
                 + [*range(ord("\u00ae"), ord("\u00ff") + 1)])
    table, spare = {b: chr(b) for b in printable}, 0
    for b in range(256):
        if b not in table:
            table[b] = chr(256 + spare)
            spare += 1
    return table


EDGE_CASES = [
    "", " ", "€", "1.234,56", "0,00", "MwSt.", "Zu", "zahlen:", "Ä", "ß", "ẞ",
    "Größe", "Straße", "Müller-Lüdenscheidt", "GmbH", "&", "Co.", "KG",
    "20%", "19,00%", "St.", "Stk", "kg", "l", "0O1lI", "—", "„Anführung“",
    "ABCDEFGHIJKLMNOPQRSTUVWXYZ", " ", "﻿", "🧾", "a" * 64,
]


def corpus_words(root, limit=None):
    seen = Counter()
    for p in sorted(Path(root).glob("*/*/page-*.ocr.json")):
        for w in json.loads(p.read_text())["pages"][0]["words"]:
            seen[w["t"]] += 1
        if limit and len(seen) >= limit:
            break
    return seen


def dump(out, corpus, limit):
    out.mkdir(parents=True, exist_ok=True)
    tk = tokenizer()

    from huggingface_hub import hf_hub_download
    for name in ("vocab.json", "merges.txt"):
        (out / name).write_bytes(Path(hf_hub_download(GOTTBERT, name)).read_bytes())

    (out / "byte_to_unicode.json").write_text(
        json.dumps({str(b): c for b, c in byte_to_unicode().items()}, ensure_ascii=False))

    (out / "spec.json").write_text(json.dumps({
        "model": GOTTBERT,
        "vocab_size": tk.vocab_size,
        "pretokenizer_regex": PRETOKEN_RE,
        "unk_id": tk.unk_token_id,
        "specials": {t: tk.convert_tokens_to_ids(t) for t in
                     (tk.bos_token, tk.eos_token, tk.pad_token, tk.unk_token, tk.mask_token)},
        "word_prefix": "Ġ",
        "note": "every OCR word is encoded as ' ' + word, no specials; the "
                "window adds bos/eos itself",
    }, ensure_ascii=False, indent=2))

    words = list(EDGE_CASES)
    if corpus:
        words += [w for w, _ in corpus_words(corpus, limit).most_common()]

    seen, cases = set(), []
    for w in words:
        if w in seen:
            continue
        seen.add(w)
        cases.append({"text": w, "ids": tk.encode(" " + w, add_special_tokens=False)})
    with open(out / "cases.jsonl", "w") as f:
        for c in cases:
            f.write(json.dumps(c, ensure_ascii=False) + "\n")
    print(f"{len(cases)} cases -> {out}")


def check(expected, actual):
    want = {c["text"]: c["ids"] for c in
            (json.loads(l) for l in open(expected) if l.strip())}
    bad = 0
    for line in open(actual):
        if not line.strip():
            continue
        c = json.loads(line)
        if c["text"] not in want:
            print(f"unknown case {c['text']!r}")
            bad += 1
        elif want[c["text"]] != c["ids"]:
            print(f"{c['text']!r}: python {want[c['text']]} != csharp {c['ids']}")
            bad += 1
    missing = len(want) - sum(1 for _ in open(actual) if _.strip())
    print(f"{len(want)} cases, {bad} mismatches" + (f", {missing} not produced" if missing > 0 else ""))
    return bad == 0 and missing <= 0


class Bpe:
    def __init__(self, d=Path("tokenizer")):
        d = Path(d)
        spec = json.loads((d / "spec.json").read_text())
        self.vocab = json.loads((d / "vocab.json").read_text())
        self.unk = spec["unk_id"]
        self.byte = {int(k): v for k, v in json.loads((d / "byte_to_unicode.json").read_text()).items()}
        self.split = regex.compile(spec["pretokenizer_regex"])
        lines = (d / "merges.txt").read_text(encoding="utf-8").split("\n")
        if lines and lines[0].startswith("#"):
            lines = lines[1:]
        self.ranks = {tuple(l.split()): i for i, l in enumerate(lines) if len(l.split()) == 2}

    @lru_cache(maxsize=1 << 16)
    def _merge(self, word):
        parts = list(word)
        while len(parts) > 1:
            best, at = None, -1
            for i in range(len(parts) - 1):
                r = self.ranks.get((parts[i], parts[i + 1]))
                if r is not None and (best is None or r < best):
                    best, at = r, i
            if at < 0:
                break
            parts[at:at + 2] = [parts[at] + parts[at + 1]]
        return parts

    def encode(self, text):
        ids = []
        for piece in self.split.findall(text):
            mapped = "".join(self.byte[b] for b in piece.encode("utf-8"))
            for token in self._merge(mapped):
                ids.append(self.vocab.get(token, self.unk))
        return ids


def selfcheck(d):
    bpe, bad, n = Bpe(d), 0, 0
    for c in (json.loads(l) for l in open(Path(d) / "cases.jsonl") if l.strip()):
        n += 1
        got = bpe.encode(" " + c["text"])
        if got != c["ids"]:
            bad += 1
            if bad <= 10:
                print(f"{c['text']!r}: want {c['ids']} got {got}")
    print(f"{n} cases, {bad} mismatches")
    return bad == 0


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("dump")
    d.add_argument("--out", type=Path, default=Path("tokenizer"))
    d.add_argument("--corpus", type=Path, default=Path("../../fixtures/dataset/gen"))
    d.add_argument("--limit", type=int, default=50000)
    s_ = sub.add_parser("selfcheck")
    s_.add_argument("--dir", type=Path, default=Path("tokenizer"))
    c = sub.add_parser("check")
    c.add_argument("--expected", type=Path, default=Path("tokenizer/cases.jsonl"))
    c.add_argument("--actual", type=Path, required=True)
    a = ap.parse_args()

    if a.cmd == "dump":
        dump(a.out, a.corpus, a.limit)
    elif a.cmd == "selfcheck":
        sys.exit(0 if selfcheck(a.dir) else 1)
    else:
        sys.exit(0 if check(a.expected, a.actual) else 1)


if __name__ == "__main__":
    main()
