"""Export the tagger to ONNX opset 17 and quantise it to int8.

The quantisation is not free and is measured, not assumed: `predict.py --onnx`
tags through the same windowing and summed logits as `Tagging/Tagger.cs`, so
scoring that dump with `tools/eval` is the number the app will see. On
`v11-19-tag-ft-e2` over the 109 real 2025 scans:

    v11-19-tag-ft-e2                            clean   supp    vat    cost
      fp32 onnx                                 0.330   0.688   0.923   3.7
      signed per-tensor        (v9/v10 default) 0.239   0.688   0.735   5.6
      signed per-channel, full range            0.000   0.000   0.000  59.1
      unsigned per-channel, heads fp32          0.330   0.688   0.925   3.7

    v11-19-u-lay-e8-s0 (the v11 winner)
      fp32 onnx                                 0.321   0.688   1.000   3.0
      signed per-channel + reduce-range         0.312   0.688   0.998   3.0
      unsigned per-channel, heads fp32          0.330   0.688   1.000   2.9

The last line of each block is the default here: **unsigned per-channel weights,
full range, with the two classifier heads left in fp32**. Same 133 MB and the
same per-window latency as the old export. Three things make it up:

* **per-channel is the whole accuracy story.** One scale for a 768x3072 matrix
  is set by that matrix's largest outlier; a scale per output column is not.
  Per-tensor loses 0.09 of clean rate, almost all of it in `vat`.
* **the weights are unsigned on purpose.** Signed per-channel weights at full
  range go through ORT's u8s8 CPU kernel, which accumulates in int16 and
  saturates on pre-VNNI x86: the model comes out not merely worse but *dead*,
  every word `O` at 0.744 confidence and every row `footer` (measured on an AMD
  EPYC 7452, AVX2, no VNNI). `--reduce-range` is the documented escape and does
  work, but it spends a bit of weight range and cost 0.018 of clean rate on the
  winner. Unsigned weights avoid that kernel altogether and keep the full range.
  If you do pass `--weight-type qint8 --per-channel`, pass `--reduce-range` with
  it; the code warns if you do not.
* **the two classifier heads stay fp32.** 21 k of 132 M parameters, no size or
  latency cost, and it is where the decision is actually made.

Leaving the 52009x768 embedding table in fp32 as well (`--matmul-only`) is no
better than this and doubles the file to 256 MB.

The C# side reads `word_logits` and `role_logits` as `float` tensors
(`Tagging/Tagger.cs`, `AsTensor<float>()`) and passes no session options beyond
the thread counts, so any op the CPU execution provider supports is allowed but
the graph outputs must stay float32 -- a wholesale fp16 conversion would not
load there.
"""
import argparse
from pathlib import Path

import numpy as np
import torch

from model import BOX_BINS, Tagger

# The two classifier heads, as the exported graph names them.
HEAD_NODES = ["/word_head/MatMul", "/role_head/MatMul"]


def dummy_boxes(batch, seq):
    """Valid (x0, y0, x1, y1) boxes: LiLT embeds x1-x0 and y1-y0 as widths."""
    lo = np.random.randint(0, BOX_BINS - 32, (batch, seq, 2))
    return np.concatenate([lo, lo + np.random.randint(1, 32, (batch, seq, 2))], axis=2)


class WordOnly(torch.nn.Module):
    """The export graph: the row head is a matmul C# does on the pooled states."""

    def __init__(self, tagger):
        super().__init__()
        self.tagger = tagger

    def forward(self, input_ids, bbox, attention_mask):
        states = self.tagger.body(input_ids=input_ids, bbox=bbox,
                                  attention_mask=attention_mask).last_hidden_state
        return self.tagger.word_head(states), self.tagger.role_head(states)


def graph_fp32(out, seq, run):
    """Write tagger.onnx and return (path, params, embedding params)."""
    out.mkdir(parents=True, exist_ok=True)
    state = None if run is None else torch.load(run / "model.pt", map_location="cpu", weights_only=True)
    tagger = Tagger(n_labels=None if state is None else state["word_head.weight"].shape[0])
    if state is not None:
        tagger.load_state_dict(state)
    tagger = tagger.eval()
    params = sum(p.numel() for p in tagger.parameters())
    embed = tagger.body.embeddings.word_embeddings.weight.numel()

    ids = torch.randint(4, 52000, (1, seq))
    bbox = torch.tensor(dummy_boxes(1, seq))
    mask = torch.ones(1, seq, dtype=torch.long)

    fp32 = out / "tagger.onnx"
    torch.onnx.export(
        WordOnly(tagger), (ids, bbox, mask), str(fp32),
        input_names=["input_ids", "bbox", "attention_mask"],
        output_names=["word_logits", "role_logits"],
        dynamic_axes={n: {0: "batch", 1: "seq"} for n in
                      ("input_ids", "bbox", "attention_mask", "word_logits", "role_logits")},
        opset_version=17, dynamo=False)
    return fp32, params, embed


def quantise(fp32, int8, per_channel=True, reduce_range=False, weight_type="quint8",
             exclude_heads=True, matmul_only=False, exclude=()):
    """Dynamic quantisation of `fp32` into `int8`, and the settings used.

    `matmul_only` leaves `Gather` alone, which keeps the 52k x 768 embedding
    table in fp32 -- bigger file, and on this model no better than per-channel.
    """
    import sys
    from onnxruntime.quantization import QuantType, quantize_dynamic
    if weight_type == "qint8" and per_channel and not reduce_range:
        print("WARNING: signed per-channel weights at full range go through ORT's u8s8 "
              "kernel, which saturates on pre-VNNI x86 and returns a DEAD model (every "
              "word O at 0.744). Add --reduce-range, or stay on --weight-type quint8.",
              file=sys.stderr)
    skip = list(exclude) + (HEAD_NODES if exclude_heads else [])
    kw = {}
    if matmul_only:
        kw["op_types_to_quantize"] = ["MatMul"]
    quantize_dynamic(str(fp32), str(int8),
                     weight_type=QuantType.QInt8 if weight_type == "qint8" else QuantType.QUInt8,
                     per_channel=per_channel, reduce_range=reduce_range,
                     nodes_to_exclude=skip, **kw)
    return {"per_channel": per_channel, "reduce_range": reduce_range,
            "weight_type": weight_type, "matmul_only": matmul_only,
            "nodes_to_exclude": skip}


def export(out, seq=512, run=None, **quant):
    fp32, params, embed = graph_fp32(out, seq, run)
    int8 = out / quant.pop("name", "tagger.int8.onnx")
    quantise(fp32, int8, **quant)
    return params, embed, fp32, int8


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=Path("runs/gate"))
    ap.add_argument("--seq", type=int, default=512)
    ap.add_argument("--run", type=Path, help="checkpoint to export; without it the graph is untrained")
    ap.add_argument("--name", default="tagger.int8.onnx", help="file name of the quantised graph")
    ap.add_argument("--from-fp32", type=Path,
                    help="quantise this existing tagger.onnx instead of re-exporting one")
    ap.add_argument("--per-channel", dest="per_channel", action="store_true", default=True,
                    help="a weight scale per output column instead of one per tensor (default)")
    ap.add_argument("--no-per-channel", dest="per_channel", action="store_false")
    ap.add_argument("--reduce-range", dest="reduce_range", action="store_true", default=False,
                    help="7-bit weights; required with --weight-type qint8 --per-channel, "
                         "which is otherwise dead on pre-VNNI x86, and worth -0.018 clean "
                         "where it is not needed")
    ap.add_argument("--no-reduce-range", dest="reduce_range", action="store_false")
    ap.add_argument("--weight-type", choices=("qint8", "quint8"), default="quint8",
                    help="unsigned weights (default) keep the full 8-bit range out of the "
                         "saturating u8s8 kernel")
    ap.add_argument("--exclude-heads", dest="exclude_heads", action="store_true", default=True,
                    help="keep word_head and role_head in fp32 (default)")
    ap.add_argument("--quantise-heads", dest="exclude_heads", action="store_false")
    ap.add_argument("--matmul-only", action="store_true",
                    help="quantise MatMul only, so the embedding table stays fp32")
    ap.add_argument("--exclude", nargs="*", default=(), help="further node names to leave alone")
    a = ap.parse_args()

    quant = dict(per_channel=a.per_channel, reduce_range=a.reduce_range,
                 weight_type=a.weight_type, exclude_heads=a.exclude_heads,
                 matmul_only=a.matmul_only, exclude=a.exclude)
    mb = lambda p: p.stat().st_size / 1e6

    if a.from_fp32:
        int8 = a.out / a.name
        a.out.mkdir(parents=True, exist_ok=True)
        used = quantise(a.from_fp32, int8, **quant)
        print(f"fp32 onnx     {mb(a.from_fp32):.1f} MB  {a.from_fp32}")
        print(f"int8 onnx     {mb(int8):.1f} MB  {int8}")
        print(f"settings      {used}")
        return

    params, embed, fp32, int8 = export(a.out, a.seq, a.run, name=a.name, **quant)
    print(f"params        {params/1e6:.1f} M  ({(params-embed)/1e6:.1f} M outside the embedding table)")
    print(f"fp32 onnx     {mb(fp32):.1f} MB")
    print(f"int8 onnx     {mb(int8):.1f} MB")


if __name__ == "__main__":
    main()
