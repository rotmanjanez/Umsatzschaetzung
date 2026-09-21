"""Tag the val pages with a trained model and write them as a page.jsonl.

Scoring is C#: assembly and metrics both live in tools/eval, so the number comes
out of the same Assemble the app ships rather than a second implementation.

    python3 predict.py --rows page.jsonl --run runs/b --dump pred.jsonl
    dotnet run --project tools/eval -- --rows pred.jsonl --split val
"""
import argparse
import json
from collections import defaultdict
from pathlib import Path

import numpy as np
import torch

from data import MAX_LEN, MAX_ROWS, OVERLAP, stitch, windows
from model import Tagger, n_cols_of, n_layers, quantise_box, tokenizer
from schema import STRUCT_LABELS, LABELS, ROLES, read_jsonl


# LABELS grew from 13 to 19 when the label classes were appended; the value classes
# kept their ids, so LABELS[:n] decodes an older, narrower head exactly.
def head_size(state):
    return state["word_head.weight"].shape[0]


def softmax(x):
    e = np.exp(x - x.max(-1, keepdims=True))
    return e / e.sum(-1, keepdims=True)


@torch.no_grad()
def tag_page(model, tk, page, device):
    ws = list(windows(page, tk))
    word_logits, role_logits, col_logits, cell_logits = [], [], [], []
    struct = bool(getattr(model, "n_cols", None))
    for w in ws:
        out = model(torch.tensor(w["input_ids"])[None].to(device),
                    torch.tensor(w["bbox"])[None].to(device),
                    torch.ones(1, len(w["input_ids"]), dtype=torch.long, device=device),
                    torch.tensor(w["row_pool"])[None].to(device))
        word_logits.append(out[0][0].float().cpu().numpy())
        role_logits.append((w["row_pool"], out[1][0].float().cpu().numpy()))
        if struct:
            col_logits.append(out[2][0].float().cpu().numpy())
            cell_logits.append(out[3][0].float().cpu().numpy())

    n = sum(len(w["input_ids"]) - 2 for w in ws) - sum(
        max(0, ws[i - 1]["offset"] + len(ws[i - 1]["input_ids"]) - 2 - ws[i]["offset"])
        for i in range(1, len(ws)))
    seq = stitch(ws, word_logits, max(n, 1))
    cseq = stitch(ws, col_logits, max(n, 1)) if struct else None
    lseq = stitch(ws, cell_logits, max(n, 1)) if struct else None

    roles = defaultdict(list)
    for w, (pool, rl) in zip(ws, role_logits):
        present = [int(np.flatnonzero(pool[i])[0]) for i in range(pool.shape[0])]
        for i, at in enumerate(present):
            roles[w["offset"] + at - 1].append(rl[i])

    names = STRUCT_LABELS if struct else LABELS[:seq.shape[-1]]
    out, at = [], 0
    for word in page["words"]:
        sub = tk.encode(" " + word["t"], add_special_tokens=False)
        if not sub or at >= len(seq):
            continue
        role = roles.get(at)
        k = int(seq[at].argmax())
        rec = {"t": word["t"], "box": word["box"], "row": word["row"],
               "pred": names[k], "conf": round(float(softmax(seq[at])[k]), 3),
               "role": ROLES[int(np.mean(role, 0).argmax())] if role else word.get("role", "line-item")}
        if struct:
            # v13: predicted column (0 = none) and cell start; the truth, when the rows carry
            # it, rides along under *_t so structscore.py can score the dump on its own
            rec["col"] = int(cseq[at].argmax())
            rec["cell_start"] = int(lseq[at].argmax())
            for src, dst in (("field", "field_t"), ("col", "col_t"), ("cell_start", "cell_t"),
                             ("role", "role_t"), ("tbl", "tbl_t")):
                if src in word:
                    rec[dst] = word[src]
        out.append(rec)
        at += len(sub)
    return out


# The ONNX path mirrors Tagging/Tagger.cs rather than tag_page: same windowing,
# summed word logits, and a per-token role head averaged over a row's first
# subwords. Tagging through it measures quantisation end to end.
def onnx_page(sess, tk, page, bos, eos):
    ids, boxes, row_of, starts, kept = [], [], [], [], []
    for w in page["words"]:
        sub = tk.encode(" " + w["t"], add_special_tokens=False)
        if not sub:
            continue
        box = quantise_box(w["box"], page["w"], page["h"])
        starts.append(len(ids))
        kept.append(w)
        for k, s in enumerate(sub):
            ids.append(s)
            boxes.append(box)
            row_of.append(w["row"] if k == 0 else -1)
    if not kept:
        return []

    word_acc = None
    role_acc = defaultdict(lambda: np.zeros(len(ROLES)))
    outs = [o.name for o in sess.get_outputs()]
    struct = "col_logits" in outs
    col_acc = cell_acc = None

    body = MAX_LEN - 2
    step = max(1, body - OVERLAP)
    for s in range(0, len(ids), step):
        e = min(s + body, len(ids))
        n = e - s + 2
        inp = np.zeros((1, n), dtype=np.int64)
        bb = np.zeros((1, n, 4), dtype=np.int64)
        inp[0, 0], inp[0, n - 1] = bos, eos
        inp[0, 1:n - 1] = ids[s:e]
        bb[0, 1:n - 1] = boxes[s:e]
        res = sess.run(outs, {"input_ids": inp, "bbox": bb,
                              "attention_mask": np.ones((1, n), dtype=np.int64)})
        wl, rl = res[0], res[1]
        if word_acc is None:
            word_acc = np.zeros((len(ids), wl.shape[-1]), dtype=np.float64)
            if struct:
                col_acc = np.zeros((len(ids), res[2].shape[-1]), dtype=np.float64)
                cell_acc = np.zeros((len(ids), 2), dtype=np.float64)
        word_acc[s:e] += wl[0, 1:n - 1]
        if struct:
            col_acc[s:e] += res[2][0, 1:n - 1]
            cell_acc[s:e] += res[3][0, 1:n - 1]

        members = {}
        for i in range(s, e):
            r = row_of[i]
            if r < 0:
                continue
            if r not in members:
                if len(members) == MAX_ROWS:
                    continue
                members[r] = []
            members[r].append(i - s + 1)
        for r, at in members.items():
            role_acc[r] += rl[0, at].mean(0)
        if e == len(ids):
            break

    names = STRUCT_LABELS if struct else LABELS[:word_acc.shape[-1]]
    out = []
    for w, st in zip(kept, starts):
        rec = {"t": w["t"], "box": w["box"], "row": w["row"],
               "pred": names[int(word_acc[st].argmax())],
               "conf": round(float(softmax(word_acc[st]).max()), 3),
               "role": ROLES[int(role_acc[w["row"]].argmax())] if w["row"] in role_acc
               else "line-item"}
        if struct:
            rec["col"] = int(col_acc[st].argmax())
            rec["cell_start"] = int(cell_acc[st].argmax())
            for src, dst in (("field", "field_t"), ("col", "col_t"), ("cell_start", "cell_t"),
                             ("role", "role_t"), ("tbl", "tbl_t")):
                if src in w:
                    rec[dst] = w[src]
        out.append(rec)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rows", type=Path, required=True)
    ap.add_argument("--run", type=Path, required=True)
    ap.add_argument("--split", default="val")
    ap.add_argument("--dump", type=Path, required=True,
                    help="where to write the tagged pages, for tools/eval to score")
    ap.add_argument("--truth-only", action="store_true", help="the assembly ceiling, no model")
    ap.add_argument("--keep-fine", action="store_true",
                    help="write the v11 fine classes (buyer, gtin ...) into the dump instead of "
                         "folding them into O; the C# scorer only knows the first 19 names")
    ap.add_argument("--onnx", type=Path,
                    help="tag with the int8 ONNX through the C# inference path instead "
                         "of PyTorch, so quantisation is measured end to end")
    a = ap.parse_args()

    pages = [p for p in read_jsonl(a.rows) if p.get("split") == a.split]
    model = tk = device = sess = None
    if a.onnx:
        import onnxruntime as ort
        tk = tokenizer()
        o = ort.SessionOptions()
        o.intra_op_num_threads, o.inter_op_num_threads = 4, 1
        sess = ort.InferenceSession(str(a.onnx), o, providers=["CPUExecutionProvider"])
    elif not a.truth_only:
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        tk = tokenizer()
        state = torch.load(a.run / "model.pt", map_location=device, weights_only=True)
        model = Tagger(n_labels=head_size(state), layers=n_layers(state), n_cols=n_cols_of(state)).to(device).eval()
        model.load_state_dict(state)

    rows, got = defaultdict(list), defaultdict(list)
    for p in pages:
        key = p["template"]  # one variation, not one invoice: variations of the
                             # same invoice are separate documents
        rows[key].append(p)
        if a.truth_only:
            got[key].append(p["words"])
        elif sess is not None:
            got[key].append(onnx_page(sess, tk, p, tk.bos_token_id, tk.eos_token_id))
        else:
            got[key].append(tag_page(model, tk, p, device))

    coarse = set(LABELS[:19]) | set(STRUCT_LABELS)

    def fold(words):
        if a.keep_fine:
            return words
        for w in words:
            for key in ("pred", "field"):
                if key in w and w[key] not in coarse:
                    w[key] = "O"
        return words

    with a.dump.open("w") as f:
        for k in rows:
            for p, words in zip(rows[k], got[k]):
                words = fold(words)
                f.write(json.dumps({**{n: p[n] for n in
                                       ("split", "invoice", "template", "page", "w", "h")},
                                    "words": words}, ensure_ascii=False) + "\n")
    print(f"{len(pages)} Seiten -> {a.dump}")


if __name__ == "__main__":
    main()
