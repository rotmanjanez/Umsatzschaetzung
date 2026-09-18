# The tagger — LiLT layout stream + GottBERT

`docs/models.md` §2. The pretrained LiLT layout transformer with its English
RoBERTa text side replaced by German GottBERT, two heads (`schema.LABELS` word
classes, 9 row roles), trained on `page.jsonl`.

    model.py             the backbone swap and the two heads
    export_onnx.py       ONNX opset 17 + int8 dynamic quantisation
    data.py              page.jsonl -> 512-subword windows, boxes, row pooling
    train.py             the training loop
    predict.py           windows -> stitched predictions -> a page.jsonl dump,
                         through PyTorch or through the int8 ONNX
    tokenizer_parity.py  artefacts and cases for the C# byte-level BPE, plus the
                         reference BPE built from those artefacts alone
    align.py             truth.json + page-N.ocr.json -> page.jsonl, including
                         the row grouping that mirrors Rows.GroupRows in the app
    schema.py            the label and role vocabularies, jsonl IO

Scoring is `tools/eval`, in C#, against the same `Assemble` the app ships.
`predict.py` only tags and dumps; the dump is scored here, not on the box:

    python3 predict.py --rows page.jsonl --run runs/b --dump pred.jsonl
    dotnet run --project tools/eval -- --rows pred.jsonl --split val

`align.py` builds `page.jsonl` from the corpus; scoring is not owned here.

## The word head is sized from the checkpoint

`LABELS` grew from 13 to 19 when the header label classes were appended, and the
value classes kept their ids, so `LABELS[:n]` still decodes a narrower head.
`Tagger(n_labels=...)` takes the width; `predict.head_size` reads it off
`word_head.weight` in the state dict, and `predict.py` and `export_onnx.py --run`
both size the model that way. Only `train.py` uses `len(LABELS)`, because a new
run is what grows the head.

## Confidence

`tag_page` puts `conf` on every word: the winning class's softmax probability
over the stitched logits, to three decimals. `onnx_page` does the same over the
summed window logits. Assembly ranks competing header runs by it and treats a
missing `conf` as 1.0, which is what makes truth words score exactly as before.

## v9 — label-keyed header selection, 2101 val variations

`b-v8/v8-body5e5-lay-s0` (a 13-way head, so no label class is ever predicted and
only the fallback path runs), scored against `expected.json`. Old and new
assembly on byte-identical predictions:

                      old     new
    clean rate       0.024   0.024
    number           0.932   0.932    +1 invoice  -0
    date             0.930   0.932    +5          -0
    supplierName     0.865   0.865    +0          -0
    netTotal         0.956   0.956    +1          -1
    grossTotal       0.984   0.985    +1          -0

The one netTotal loss is the confidence tie-break doing its job on a page where
the model is wrong: two netTotal runs in the totals block, and it is more sure of
the subtotal (0.975) than of the real net (0.899). A `netLabel` settles it once
the corpus emits one.

The label path itself cannot be measured on a v8 model. Injecting the label
classes onto 201 val variations synthetically, then planting one false positive
of each header field ahead of the true value — which is what a real scan looks
like — separates the two rules completely:

                      old     new
    number           0.204   0.940    +148 invoices  -0
    grossTotal       0.353   0.990    +128           -0
    netTotal         0.955   0.980    +5             -0

With the labels injected but no false positives planted, old and new agree on
every field, so the label path costs nothing where "first" was already right.
That selection now lives in `Extract.Assemble`, the one the app ships; the
Python reference it was ported from is gone. `tools/eval/README.md` has the
order.

## The latency gate (2026-09-15, M1 Pro, 4 threads, untrained, measured once)

    params        132.1 M   (92.2 M outside the embedding table)
    fp32 onnx     525 MB
    int8 onnx     132 MB

    int8, ms per window        128    256    384    512
      4 threads                 33     71    114    166
      8 threads                 69    138    244    390

The corpus median page is 256 subwords, so a 2-page invoice is two windows:
~140 ms, ~330 ms worst case at a full 512. The budget is a second. Passes.

Eight threads is slower than four on this chip — the scheduler puts work on the
efficiency cores. Pin the ONNX session to four.

## Running

    python3 -m venv ~/venvs/b && ~/venvs/b/bin/pip install -r requirements.txt

    python3 align.py --corpus ../../fixtures/dataset/gen --out page.jsonl
    python3 export_onnx.py --out runs/gate
    python3 tokenizer_parity.py dump --out tokenizer
    python3 tokenizer_parity.py selfcheck
    python3 train.py --rows page.jsonl --out runs/b-001
    python3 train.py --rows page.jsonl --out runs/b-002 --init ../../checkpoints/b-v9/v9-...
    python3 predict.py --rows page.jsonl --run runs/b-001

`--init PATH` warm-starts training from `PATH/model.pt` instead of the plain
pretrained backbone, and stops with a message when either head in that
checkpoint is a different width from this run's.

`predict.py --truth-only` is the assembly ceiling: it scores the ground-truth
labels through the same path and no model can beat that number.

## The tokenizer parity test

`tokenizer_parity.py dump` writes what a C# byte-level BPE needs — `vocab.json`,
`merges.txt`, `byte_to_unicode.json`, the GPT-2 pre-tokenizer regex in
`spec.json` — plus `cases.jsonl`, every distinct OCR word in the corpus with its
expected ids. `selfcheck` reimplements the tokenizer from those files
alone and imports nothing from transformers; it matches all 50 049 cases, which
is what says the artefacts are sufficient. The C# writes the same file shape and
`tokenizer_parity.py check` diffs it.

## Training throughput (2026-09-15, one Quadro RTX 6000, real page.jsonl, measured once)

    6711 pages, 825 449 OCR words, 123 words/page
    6792 windows total (5804 train / 988 val) — 1.01 windows per page

    batch    ms/batch   windows/s   peak GiB   dataloader wait
       8          139        57.5       7.09              1.9%
      16          261        61.2      12.49              1.1%
      24          408        58.8      17.89              0.7%
      32          528        60.6      23.30              0.7%

Throughput is flat from batch 8 to 32, so the GPU is saturated at batch 8 and a
larger batch buys nothing but memory. 0.72 GiB per window; batch 32 is the
ceiling on a 24 GB card.

    startup       32 s   (4 read + 2 tokenizer + 24 index build + 2 model)
    epoch        123 s   (104 s train + ~19 s val)
    6 epochs   12m 49s   measured, end to end

Six cards running one config each is still 13 minutes, since the runs are
independent and nothing is shared but the NFS read at startup.

## OCR v1 vs v2 (AutoLevels), same config, one card

Two identical 6-epoch baseline runs, the only difference being the OCR behind
`page.jsonl`. v2 adds an AutoLevels histogram stretch; its alignment recovers
1.12 pp more truth words (unseen 14.91% -> 13.65%).

    val macro-F1        0.9698 -> 0.9711
    clean rate           0.403 -> 0.435
    line precision       0.990 -> 0.992
    line recall          0.992 -> 0.993
    correction cost  6.4 / p90 21 -> 5.6 / p90 18
    supplierName         0.703 -> 0.724   (fuzzy 0.749 -> 0.763)
    wall clock         12m 49s -> 12m 43s
    windows per page     1.012 -> 1.014

The word-level metric barely moves and the invoice-level one moves a lot. That
is the expected shape, not a surprise: word recall was already 0.99+ on every
field before the change, so better OCR cannot show up as higher F1 on the words
the model already saw. It shows up as more words reaching the model at all --
per-class support in val rose 0.3-2.0% -- and those extra words are whole cells
that were previously unrecoverable.

Read per-class F1 next to its support count, never alone. `vat` support rose
0.3% and its F1 moved 0.0007, which agrees with its unseen rate barely moving
(40.2 -> 39.4); it is not the model underperforming.

## v3 — the vat relabel, same OCR as v2

3176 totals-block VAT words that were taught as `O` are now labelled `vat`.
OCR input byte-identical to v2, so the difference is labels alone; the window
counts (5815/989) come out the same, which confirms it.

    val macro-F1     0.9711 -> 0.9711     unchanged to four places
    O           F1   0.9828 -> 0.9823     n 58750 -> 58321   -429
    vat         F1   0.9652 -> 0.9642     n  2506 ->  2935   +429

`O` loses exactly the words `vat` gains. Both F1s move by a thousandth, so the
model learned the newly labelled words about as well as the ones it already
had — the relabel cost nothing and bought a class that was previously
unlearnable in 57% of layouts.

Tier 1, 452 val variations, canonical `score.vat_exempt`, repair off both sides:

                      v2      v3
    clean rate       0.551   0.555
    line precision   0.992   0.992
    line recall      0.994   0.994
    correction cost  3.4/10  4.0/13
    vat (line)       0.986   0.964
    netTotal         0.962   0.967
    grossTotal       0.960   0.967

`vat` falling and correction cost rising are both the target getting harder,
not the model getting worse. Under v2 labels a line with no printed rate scored
vat 0 against 0 — free. Under v3 the totals rate is inherited onto both sides,
so vat is a real cell on every line, and invoices that still fail now fail by
more cells.

### Group by template, not by invoice

`page.jsonl` has both, and they are not the same thing: 452 val variations sit
on 283 invoices, so keying on `invoice` silently merges several renderings of
one invoice into a single document with duplicated lines. The assembly ceiling
stays 1.000 either way — both sides merge identically — so the mistake does not
announce itself. `template` is the variation id and is the right key.

Every Tier 1 number here is on `template`. The v1 and v2 tables recorded
earlier in this session were on `invoice` and read about 10 points low.

### What parse.repair is worth is not measurable here

Repairing the reference changes the target, so a plain on/off A/B is
confounded. Holding one side fixed separates them:

    repair    both    want    got    none
    clean    0.546   0.511  0.509  0.555

One-sided repair costs ~4 points in either direction: the two sides have to
agree. Applied consistently it is roughly neutral, -0.9 points of clean rate
with correction cost unchanged at 4.0.

That residual is not damage. Under true labels every substitution this makes is
a genuine rescue (`løø`->`100`, `1.902,oo`->`1.902,00`, 345 of 25384 numeric val
words). Repairing the reference moves it toward truth, which makes previously
free cells real ones.

Deciding it properly needs a reference not derived from OCR text. The synthetic
corpus has no `expected.json` — `truth.json` carries words and regions, no
invoice-level values — so this harness cannot supply one.

## se41

Six idle Quadro RTX 6000, `~/venvs/b` on the shared NFS home, no Slurm — launch
with `CUDA_VISIBLE_DEVICES=N`. Measured once on sm_75:

    fp32 13.6 / fp16 94.7 / bf16 7.9 TFLOPS
    sdpa flash unavailable, mem_efficient ok

`torch.cuda.is_bf16_supported()` returns **True** here and is wrong — bf16 is
emulated and slower than fp32. fp16 AMP with a GradScaler, as the doc says.

Home has a hard 50 GB quota that was full; the pip cache was cleared to fit the
cu121 wheels. Keep run outputs on `/tmp` (252 GB tmpfs, but it is RAM).

## Corpus state (2026-09-15)

    401 invoices, 3200 variations, 3200 distinct templates
    2748 train / 452 val variations, split by template
    6711 pages, 988 714 labelled truth words
    225 MB of JSON; the 5.3 GB of page images are not needed once aligned

## Coordinate spaces

`truth.json` and `page-N.ocr.json` are in the **same pixel space** and both
declare the same page `width`/`height`. Verified on `inv-000042/v06`: truth word
bbox `242,197,2143,3170` against OCR `241,204,2144,3165`.

Two traps:

- `truth["pages"][i]["words"]` is **not in reading order**, and the same string
  can appear several times on a page (a wordmark and a sender line). Never align
  by text alone; align by IoU first and use text only to break ties.
- Word counts differ by design. That page has 178 truth words and 169 OCR words,
  and the OCR read `Hollerbach` as `Hürbach`. That is the supervision signal, not
  a bug.

## Formats

`truth.json`

    template  stable hash, decides the split
    split     "train" | "val"          already assigned, honour it
    profile   crisp | scan_clean | scan_worn | photocopy | photo | fax
    pages[]   image, width, height, words[], regions[]
      words[]    t (text), f (field or "O"), l (line index), box [x,y,w,h]
      regions[]  role, l, box [x,y,w,h]

`page-N.ocr.json`

    engine, language, maxImageDimension
    pages[0]  width, height, words[]  with t, box [x,y,w,h]

`page.jsonl`, one object per page, the only file the model reads:

    template, split, profile, invoice, page   provenance
    w, h                                      page size
    words[]  t, box [x,y,w,h], field, row, role, item

A line item spans as many rows as the layout gave it — a wrapped name, a detail
row, an amount pushed onto its own row. Grouping by row instead of by item cost
27 points of line precision, 0.704 against 0.969.

The boundary rides in `role`: `line-item` opens an item, `line-wrap` continues
one, and `continuation` is a detail row of the item above. Assembly needs nothing
else, so it works the same on predictions as on a truth dump.

`item`, the index of the line-item region the word falls in or -1, comes from
`truth.json` regions and so exists at training time only. It is kept for
diagnostics and as the oracle the role split is measured against: 0.989 line
precision against its 0.992.

Words are emitted in reading order, grouped into rows by the same rule as
`Rows.GroupRows` in `Umsatzschätzung.Core`, so training and inference see the
same grouping.

## Alignment rule

For each OCR word, take the truth word with the highest IoU. Accept above 0.3
IoU; between 0.1 and 0.3 require a normalised-text similarity above 0.5; below
that emit `O`. A truth word may claim several OCR words (the OCR split it) and
several truth words may map to one OCR word (the OCR merged them) — in the merge
case take the label of the truth word with the largest overlap area.

Report the share of OCR words that got a field label and the share of truth field
words with no OCR word at all. That second number is the **OCR ceiling** from
`extraction-eval.md` §5, and it caps every model that follows.

### v11: 38 classes and the over-labeling ablation

`schema.FIELDS` grew by nineteen fine classes (buyer, customerNumber, orderNumber,
deliveryNoteNumber, taxId, bankId, postcode, phone, orderDate, deliveryDate, dueDate,
gtin, lineDiscount, lineGross, priceBasis, subtotal, charge, discount, amountDue).
Assembly never reads them; they exist so the model has to tell a customer number
from the invoice number by context. `train.py --n-labels 19` folds them back into `O`
at load time (`data.LABEL_CAP`) and trains the old 19-way head on the same corpus —
the control run for whether over-labeling helps. Older heads still decode via
`LABELS[:n]`.
