"""The LiLT layout stream with GottBERT as its text side."""
import torch
from torch import nn
from transformers import AutoModel, AutoTokenizer, LiltModel

from schema import LABELS, ROLES

LILT = "SCUT-DLVCLab/lilt-roberta-en-base"
GOTTBERT = "TUM/GottBERT_base_last"

BOX_BINS = 1024


def backbone(lilt=LILT, text=GOTTBERT):
    """LiLT with its English RoBERTa text stream replaced by a German one.

    Both halves are 12x768 RoBERTa; only the vocabulary differs, so the swap is
    a straight state-dict copy over the 199 text keys.
    """
    body = LiltModel.from_pretrained(lilt)
    german = AutoModel.from_pretrained(text)
    body.resize_token_embeddings(german.config.vocab_size)
    missing, unexpected = body.load_state_dict(german.state_dict(), strict=False)
    if unexpected:
        raise RuntimeError(f"text encoder has keys LiLT does not: {sorted(unexpected)[:5]}")
    if any("layout" not in k for k in missing):
        raise RuntimeError("text stream not fully transplanted")
    body.config.vocab_size = german.config.vocab_size
    return body


def tokenizer(text=GOTTBERT):
    return AutoTokenizer.from_pretrained(text)


class Tagger(nn.Module):
    """Word classes on the first subword of each word, row roles on a mean pool."""

    def __init__(self, body=None, dropout=0.1, n_labels=None, layers=None, n_cols=None):
        super().__init__()
        self.body = body if body is not None else backbone()
        # v13 structure heads: column index (0 = none, 1..n_cols) and cell start (0/1).
        # Only built when asked for, so v11/v12 checkpoints keep loading strictly.
        self.n_cols = n_cols
        if layers is not None and layers < len(self.body.encoder.layer):
            # A shallower tagger keeps the bottom `layers` LiLT blocks (each block
            # holds the text and the layout stream of one depth) and drops the rest.
            self.body.encoder.layer = self.body.encoder.layer[:layers]
            self.body.config.num_hidden_layers = layers
        h = self.body.config.hidden_size
        self.dropout = nn.Dropout(dropout)
        self.word_head = nn.Linear(h, n_labels or len(LABELS))
        self.role_head = nn.Linear(h, len(ROLES))
        if n_cols:
            self.col_head = nn.Linear(h, n_cols + 1)
            self.cell_head = nn.Linear(h, 2)

    def forward(self, input_ids, bbox, attention_mask, row_pool=None):
        states = self.dropout(self.body(input_ids=input_ids, bbox=bbox,
                                        attention_mask=attention_mask).last_hidden_state)
        word_logits = self.word_head(states)
        if row_pool is None:
            if self.n_cols:
                return word_logits, self.col_head(states), self.cell_head(states)
            return word_logits
        # row_pool: (B, R, T) row membership, rows normalised to sum 1 over their words
        role_logits = self.role_head(torch.bmm(row_pool, states))
        if self.n_cols:
            return word_logits, role_logits, self.col_head(states), self.cell_head(states)
        return word_logits, role_logits


def n_layers(state):
    """How deep the tagger in a state dict is (12 for the full LiLT/GottBERT stack)."""
    depths = [int(k.split("encoder.layer.")[1].split(".")[0])
              for k in state if "encoder.layer." in k]
    return max(depths) + 1


def n_cols_of(state):
    """Columns of a v13 checkpoint's column head (None for a v11/v12 checkpoint)."""
    w = state.get("col_head.weight")
    return None if w is None else w.shape[0] - 1


def truncate_state(state, layers):
    """Drop the encoder blocks a shallower tagger has no slot for."""
    return {k: v for k, v in state.items()
            if "encoder.layer." not in k or int(k.split("encoder.layer.")[1].split(".")[0]) < layers}


def quantise_box(box, w, h, bins=BOX_BINS):
    """A pixel box to LiLT's (x0, y0, x1, y1) bin space, clamped inside the page."""
    x, y, bw, bh = box
    top = bins - 1
    q = lambda v, size: min(top, max(0, int(v * top / max(size, 1))))
    x0, y0 = q(x, w), q(y, h)
    return [x0, y0, max(x0, q(x + bw, w)), max(y0, q(y + bh, h))]
