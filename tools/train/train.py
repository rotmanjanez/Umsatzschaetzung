"""Training: LiLT + GottBERT, word head and row head, fp16 on Turing."""
import argparse
import json
import math
import time
from pathlib import Path

import torch
from torch.utils.data import DataLoader

import data
from data import IGNORE, Windows, class_weights, collate
from model import Tagger, n_layers, tokenizer, truncate_state
from schema import LABELS, MAX_COLS, ROLES, STRUCT_LABELS

NAMES = list(LABELS)  # narrowed by --n-labels


def param_groups(model, lr_body, lr_head, lr_layout):
    layout = [p for n, p in model.body.named_parameters() if "layout" in n]
    body = [p for n, p in model.body.named_parameters() if "layout" not in n]
    heads = list(model.word_head.parameters()) + list(model.role_head.parameters())
    if getattr(model, "n_cols", None):
        heads += list(model.col_head.parameters()) + list(model.cell_head.parameters())
    return [{"params": body, "lr": lr_body},
            {"params": layout, "lr": lr_layout},
            {"params": heads, "lr": lr_head}]


def schedule(step, total, warmup):
    if step < warmup:
        return step / max(1, warmup)
    p = (step - warmup) / max(1, total - warmup)
    return 0.5 * (1 + math.cos(math.pi * min(1.0, p)))


class Counts:
    """Per-class hit/predicted/true counts for one head."""

    def __init__(self, n, device):
        self.n = n
        self.hit = torch.zeros(n, dtype=torch.long, device=device)
        self.pred = torch.zeros(n, dtype=torch.long, device=device)
        self.true = torch.zeros(n, dtype=torch.long, device=device)

    def add(self, pred, true):
        self.pred += torch.bincount(pred, minlength=self.n)
        self.true += torch.bincount(true, minlength=self.n)
        self.hit += torch.bincount(true[pred == true], minlength=self.n)

    def report(self, names, macro_from=0):
        f1 = 2 * self.hit / (self.pred + self.true).clamp(min=1)
        recall = self.hit / self.true.clamp(min=1)
        per_class = {names[c]: {"f1": f1[c].item(), "recall": recall[c].item(),
                                "support": int(self.true[c].item())}
                     for c in range(self.n) if self.true[c] > 0}
        seen = f1[macro_from:][self.true[macro_from:] > 0]
        return seen.mean().item(), per_class


@torch.no_grad()
def evaluate(model, loader, device):
    """Word-head macro-F1 over the twelve fields, role-head macro-F1 over the roles."""
    model.eval()
    word = Counts(len(NAMES), device)
    role = Counts(len(ROLES), device)
    struct = getattr(model, "n_cols", None)
    col = Counts(MAX_COLS + 1, device) if struct else None
    cell = Counts(2, device) if struct else None
    for b in loader:
        b = {k: v.to(device) for k, v in b.items()}
        with torch.autocast("cuda", torch.float16, enabled=device.type == "cuda"):
            out = model(b["input_ids"], b["bbox"], b["attention_mask"], b["row_pool"])
        wl, rl = out[0], out[1]
        keep = b["labels"] != IGNORE
        word.add(wl.argmax(-1)[keep], b["labels"][keep])
        keep = b["role_labels"] != IGNORE
        role.add(rl.argmax(-1)[keep], b["role_labels"][keep])
        if struct:
            keep = b["col_labels"] != IGNORE
            col.add(out[2].argmax(-1)[keep], b["col_labels"][keep])
            keep = b["cell_labels"] != IGNORE
            cell.add(out[3].argmax(-1)[keep], b["cell_labels"][keep])
    model.train()
    macro, per_class = word.report(NAMES, macro_from=1)
    role_macro, role_per_class = role.report(ROLES)
    m = {"macro": macro, "o_f1": per_class.get("O", {}).get("f1", 0.0),
         "per_class": per_class, "role_macro": role_macro,
         "role_per_class": role_per_class}
    if struct:
        # column accuracy over table words (column 0 = "no table" excluded), cell-start F1
        table = col.true[1:].sum().clamp(min=1)
        m["col_acc"] = (col.hit[1:].sum() / table).item()
        _, cell_pc = cell.report(["I", "B"])
        m["cell_f1"] = cell_pc.get("B", {}).get("f1", 0.0)
        m["col_per_index"] = {str(i): {"acc": (col.hit[i] / col.true[i].clamp(min=1)).item(),
                                       "support": int(col.true[i].item())}
                              for i in range(1, MAX_COLS + 1) if col.true[i] > 0}
        # the selection metric for a structure run: word macro (metadata), column accuracy,
        # cell-start F1 and role macro, equally weighted
        m["struct"] = (macro + m["col_acc"] + m["cell_f1"] + role_macro) / 4
    return m


def show(m, where):
    extra = (f"  col acc {m['col_acc']:.4f}  cell-start F1 {m['cell_f1']:.4f}  struct {m['struct']:.4f}"
             if "struct" in m else "")
    print(f"{where}  word macro-F1 {m['macro']:.4f}  O F1 {m['o_f1']:.4f}  "
          f"role macro-F1 {m['role_macro']:.4f}{extra}", flush=True)


def show_per_class(m):
    for title, key in (("word", "per_class"), ("role", "role_per_class")):
        print(f"\nper-class {title} F1 (val)")
        for name, v in sorted(m[key].items(), key=lambda kv: -kv[1]["support"]):
            print(f"  {name:15s} F1 {v['f1']:.4f}  recall {v['recall']:.4f}  n {v['support']}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rows", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--epochs", type=int, default=6)
    ap.add_argument("--batch", type=int, default=16)
    ap.add_argument("--accum", type=int, default=4)
    ap.add_argument("--lr-body", type=float, default=3e-5)
    ap.add_argument("--lr-head", type=float, default=3e-4)
    ap.add_argument("--lr-layout", type=float, default=3e-5,
                    help="the LiLT layout stream is pretrained, so it is not a head")
    ap.add_argument("--freeze-layout", action="store_true")
    ap.add_argument("--row-weight", type=float, default=0.3)
    ap.add_argument("--role-boost", default="", help="name=factor,... on the role class weights")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--max-steps", type=int, default=0, help="smoke runs")
    ap.add_argument("--init", type=Path,
                    help="warm-start from PATH/model.pt; both head widths must match")
    ap.add_argument("--layers", type=int, default=12,
                    help="keep only the bottom N LiLT blocks (size/latency study)")
    ap.add_argument("--struct", action="store_true",
                    help="v13: structure targets (reduced word classes + column + cell-start heads)")
    ap.add_argument("--col-weight", type=float, default=0.5)
    ap.add_argument("--cell-weight", type=float, default=0.5)
    ap.add_argument("--n-labels", type=int, default=len(LABELS),
                    help="head width; classes with id >= N fold into O (ablation)")
    ap.add_argument("--eval-only", action="store_true",
                    help="score an existing checkpoint in --out and stop")
    a = ap.parse_args()
    global NAMES
    data.LABEL_CAP = a.n_labels if a.n_labels < len(LABELS) else None
    names = NAMES = LABELS[:a.n_labels]
    if a.struct:
        data.STRUCT = True
        data.LABEL_CAP = None
        names = NAMES = list(STRUCT_LABELS)
    n_cols = MAX_COLS if a.struct else None

    torch.manual_seed(a.seed)
    a.out.mkdir(parents=True, exist_ok=True)
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    tk = tokenizer()

    val = Windows(a.rows, tk, split="val")
    if a.eval_only:
        state = torch.load(a.out / "model.pt", map_location=device, weights_only=True)
        model = Tagger(n_labels=len(names), layers=n_layers(state), n_cols=n_cols).to(device).eval()
        model.load_state_dict(state)
        vdl = DataLoader(val, batch_size=a.batch, collate_fn=collate, num_workers=a.workers)
        m = evaluate(model, vdl, device)
        show(m, "val")
        show_per_class(m)
        return

    train = Windows(a.rows, tk, split="train", augment=True, seed=a.seed)
    print(f"{len(train)} train windows, {len(val)} val windows")

    model = Tagger(n_labels=len(names), layers=a.layers, n_cols=n_cols).to(device)
    if a.init:
        state = truncate_state(torch.load(a.init / "model.pt", map_location=device,
                                          weights_only=True), a.layers)
        for head, width in (("word_head", len(names)), ("role_head", len(ROLES))):
            got = state[f"{head}.weight"].shape[0]
            if got != width and not (a.struct and head == "word_head"):
                raise SystemExit(f"--init {a.init}: {head} is {got} wide, this run needs {width}")
        if a.struct:
            state = {k: v for k, v in state.items() if not k.startswith("word_head.")}
            missing, unexpected = model.load_state_dict(state, strict=False)
            print(f"initialised from {a.init}/model.pt ({a.layers} layers); new heads: "
                  f"{sorted({k.split('.')[0] for k in missing})}", flush=True)
        else:
            model.load_state_dict(state)
            print(f"initialised from {a.init}/model.pt ({a.layers} layers)", flush=True)
    if a.freeze_layout:
        for n, p in model.body.named_parameters():
            if "layout" in n:
                p.requires_grad_(False)

    word_w = class_weights(train, len(names), "labels").to(device)
    role_w = class_weights(train, len(ROLES), "role_labels")
    for part in filter(None, a.role_boost.split(",")):
        name, factor = part.split("=")
        role_w[ROLES.index(name)] *= float(factor)
    print("role weights " + " ".join(f"{n}={w:.2f}" for n, w in zip(ROLES, role_w.tolist())))
    role_w = role_w.to(device)
    word_loss = torch.nn.CrossEntropyLoss(weight=word_w, ignore_index=IGNORE, label_smoothing=0.05)
    role_loss = torch.nn.CrossEntropyLoss(weight=role_w, ignore_index=IGNORE)
    col_loss = torch.nn.CrossEntropyLoss(ignore_index=IGNORE)
    cell_loss = torch.nn.CrossEntropyLoss(ignore_index=IGNORE)

    opt = torch.optim.AdamW(param_groups(model, a.lr_body, a.lr_head, a.lr_layout),
                            betas=(0.9, 0.98), weight_decay=0.01)
    base = [g["lr"] for g in opt.param_groups]
    scaler = torch.amp.GradScaler("cuda", enabled=device.type == "cuda")

    dl = DataLoader(train, batch_size=a.batch, shuffle=True, collate_fn=collate,
                    num_workers=a.workers, drop_last=True)
    vdl = DataLoader(val, batch_size=a.batch, collate_fn=collate, num_workers=a.workers)

    total = a.max_steps or (len(dl) // a.accum) * a.epochs
    warmup = max(1, int(0.05 * total))
    step, best, log = 0, -1.0, []
    t0 = time.perf_counter()

    for epoch in range(a.epochs):
        for i, b in enumerate(dl):
            b = {k: v.to(device, non_blocking=True) for k, v in b.items()}
            with torch.autocast("cuda", torch.float16, enabled=device.type == "cuda"):
                out = model(b["input_ids"], b["bbox"], b["attention_mask"], b["row_pool"])
                wl, rl = out[0], out[1]
                loss = (word_loss(wl.reshape(-1, len(names)), b["labels"].reshape(-1))
                        + a.row_weight * role_loss(rl.reshape(-1, len(ROLES)),
                                                   b["role_labels"].reshape(-1)))
                if a.struct:
                    loss = (loss
                            + a.col_weight * col_loss(out[2].reshape(-1, MAX_COLS + 1),
                                                      b["col_labels"].reshape(-1))
                            + a.cell_weight * cell_loss(out[3].reshape(-1, 2),
                                                        b["cell_labels"].reshape(-1)))
            scaler.scale(loss / a.accum).backward()

            if (i + 1) % a.accum:
                continue
            scaler.unscale_(opt)
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            for g, lr in zip(opt.param_groups, base):
                g["lr"] = lr * schedule(step, total, warmup)
            scaler.step(opt)
            scaler.update()
            opt.zero_grad(set_to_none=True)
            step += 1

            if step % 25 == 0:
                print(f"step {step}/{total}  loss {loss.item():.4f}  "
                      f"{(time.perf_counter()-t0)/step:.2f} s/step", flush=True)
                log.append({"step": step, "loss": loss.item()})
            if a.max_steps and step >= a.max_steps:
                break
        if a.max_steps and step >= a.max_steps:
            break

        m = evaluate(model, vdl, device)
        show(m, f"epoch {epoch}")
        log.append({"epoch": epoch, "val_macro_f1": m["macro"], "o_f1": m["o_f1"],
                    "role_macro_f1": m["role_macro"], "per_class": m["per_class"],
                    "role_per_class": m["role_per_class"],
                    **({"col_acc": m["col_acc"], "cell_f1": m["cell_f1"], "struct": m["struct"],
                        "col_per_index": m["col_per_index"]} if "struct" in m else {})})
        score = m["struct"] if "struct" in m else m["macro"]
        if score > best:
            best = score
            torch.save(model.state_dict(), a.out / "model.pt")

    (a.out / "log.json").write_text(json.dumps(
        {"args": {k: str(v) for k, v in vars(a).items()}, "best": best, "log": log}, indent=2))
    print(f"best val macro-F1 {best:.4f}")

    last = [e for e in log if "per_class" in e]
    if last:
        show_per_class({"per_class": last[-1]["per_class"],
                        "role_per_class": last[-1]["role_per_class"]})


if __name__ == "__main__":
    main()
