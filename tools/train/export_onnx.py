"""Export the tagger to ONNX opset 17 and quantise it to int8."""
import argparse
from pathlib import Path

import numpy as np
import torch

from model import BOX_BINS, Tagger


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


def export(out, seq=512, run=None):
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

    from onnxruntime.quantization import QuantType, quantize_dynamic
    int8 = out / "tagger.int8.onnx"
    quantize_dynamic(str(fp32), str(int8), weight_type=QuantType.QInt8)
    return params, embed, fp32, int8


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=Path("runs/gate"))
    ap.add_argument("--seq", type=int, default=512)
    ap.add_argument("--run", type=Path, help="checkpoint to export; without it the graph is untrained")
    a = ap.parse_args()

    params, embed, fp32, int8 = export(a.out, a.seq, a.run)
    mb = lambda p: p.stat().st_size / 1e6
    print(f"params        {params/1e6:.1f} M  ({(params-embed)/1e6:.1f} M outside the embedding table)")
    print(f"fp32 onnx     {mb(fp32):.1f} MB")
    print(f"int8 onnx     {mb(int8):.1f} MB")


if __name__ == "__main__":
    main()
