# Eval

Tier 1 extraction metrics, in C#. Both models are scored by this same code, so
their numbers are comparable, and assembly is `Core`'s `Extract.Assemble` — the
one the app ships — so the number is not about a second implementation.

    dotnet run --project tools/eval -- --rows tools/train/page.jsonl --split val

    --rows      page.jsonl: labelled or predicted words, one page per line
    --corpus    corpus root holding <invoice>/expected.json
    --split     "" scores every split
    --no-repair assemble without the OCR digit repair, for the A/B
    --out       the report
    --detail    per-variation TSV, for diffing two runs

A word is `{t, box, row, pred|field, role}`. `pred` wins over `field`, so the
same code scores a prediction or verifies a ground truth end to end.

**Run it on ground truth first.** With `field` and no `pred` the score must come
out near perfect. Whatever it loses is assembly loss, not model error, and it is
the ceiling for every model scored afterwards. A model can never beat it.

Model B is scored by dumping its predictions in this shape and pointing `--rows`
at the dump — see `tools/trainb/README.md`.

## Scales, from Extract/Parse.cs

    money (netTotal, grossTotal, lineNet)   cents   x100
    quantity                                milli   x1000
    unitPrice                               micro   x1000000
    vat                                     bp      x100    (700 = 7.00 %)

Verified against a real fixture: `134 Stk` x `0,34` = `45,56` reads back as
quantity 134000, unitPrice 340000, lineNet 4556.

## Metrics

Header exact-match after normalisation on number, date, supplierName, netTotal,
grossTotal, plus a fuzzy supplier variant that ignores legal-form suffixes. Lines
matched greedily on name similarity plus lineNet agreement, then precision,
recall and per-field accuracy on matched lines. Invoice clean rate is the share
with every cell right. Correction cost is wrong cells per invoice, mean and p90;
unmatched expected lines count as fully wrong.

`Similarity.cs` reproduces `difflib.SequenceMatcher.ratio()` exactly, autojunk
included: the line matcher's 0.55 threshold was tuned against those numbers, so
an approximation would silently move every match.

## Status of the two datasets

**Synthetic val — runnable today.** 452 val variations, split by template.

**Real scans — blocked.** `fixtures/dataset/2025` has 117 `*.expected.json` and
**zero `*.ocr.json`**. Step 1 of the plan in `docs/extraction-eval.md` ("OCR dump
step, checked in") has not been done for the real scans, only for the synthetic
corpus. Until it is, there is no real-scan number, and the synthetic val score on
its own says nothing about the domain gap — which is the entire question.

The fix:

    dotnet run --project tools/ocr -- fixtures/dataset/2025

The 108 files in 2025 are PDFs and `tools/ocr` still rasterises those through
`Windows.Data.Pdf`, so that directory is Windows-bound until the dependency is
replaced. `tools/rapidocr/run.py` is the Python path and may already sidestep it.

### Preprocessing: none

`RapidOcr` does no preprocessing of its own. The `AutoLevels` histogram stretch
that `WindowsOcr` carried was measured against RapidOCR, came out slightly worse,
and has been removed along with the `--raw` and `--no-levels` switches.

The corpus dumps and the real scans must be produced by the same engine.
