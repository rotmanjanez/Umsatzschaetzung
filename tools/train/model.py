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

    def __init__(self, body=None, dropout=0.1, n_labels=None):
        super().__init__()
        self.body = body if body is not None else backbone()
        h = self.body.config.hidden_size
        self.dropout = nn.Dropout(dropout)
        self.word_head = nn.Linear(h, n_labels or len(LABELS))
        self.role_head = nn.Linear(h, len(ROLES))

    def forward(self, input_ids, bbox, attention_mask, row_pool=None):
        states = self.dropout(self.body(input_ids=input_ids, bbox=bbox,
                                        attention_mask=attention_mask).last_hidden_state)
        word_logits = self.word_head(states)
        if row_pool is None:
            return word_logits
        # row_pool: (B, R, T) row membership, rows normalised to sum 1 over their words
        return word_logits, self.role_head(torch.bmm(row_pool, states))


def quantise_box(box, w, h, bins=BOX_BINS):
    """A pixel box to LiLT's (x0, y0, x1, y1) bin space, clamped inside the page."""
    x, y, bw, bh = box
    top = bins - 1
    q = lambda v, size: min(top, max(0, int(v * top / max(size, 1))))
    x0, y0 = q(x, w), q(y, h)
    return [x0, y0, max(x0, q(x + bw, w)), max(y0, q(y + bh, h))]
