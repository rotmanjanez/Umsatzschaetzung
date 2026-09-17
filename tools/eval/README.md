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
at the dump — see `tools/train/README.md`.

## Which run of a header field is the value

`Assemble.HeaderValue` picks one run per field. The old rule took the first run on the
first page that had one; on a real scan the first is routinely a false positive — a phone
or customer number ahead of the invoice number, a per-line "EUR" ahead of the total.

1. **Label first.** `invoiceNumber` has `numberLabel`, `invoiceDate` has `dateLabel`, the
   totals have `netLabel` and `grossLabel`. Each run of label words claims the value run
   beside it on its printed line, else the one directly beneath it, else the nearest within
   a quarter of the page. Between competing labels the higher mean `conf` wins, then
   reading order.
2. **Otherwise by role and confidence.** A run in the expected row role first (`header`,
   or `total` for the totals), then highest mean `conf`, then — for the supplier, which has
   no label — the longest run, then reading order.
3. Either way the first page with any run decides, so a letterhead on page 1 beats a
   footer on page 3.

A run is the block of consecutive rows carrying the field, so a wrapped supplier name
stays one value. Two things end it early: a row that opens a key of its own, and a gap
wide enough to be a column. A line row grouping split in two — which skew does routinely —
is rejoined and re-sorted by x, so "RG 001612" does not come back as "001612 RG".

Two exceptions:

* **The supplier name printed twice** (`Undouble`, `Printings`). A wordmark carrying the
  company name sits over or beside the sender block that prints it again, and the run joins
  the two — `'Pucher OG'` came back as `'Pucher Pucher OG'`. The run is cut into printings
  and compared case-folded (`CaseFold`, not `ToLowerInvariant`, or GROSSHANDEL never
  matches Großhandel); a printing whose words are an unbroken run inside another's is
  dropped, and of two equal printings the later is kept. A name genuinely set over two
  lines ("Bäckerei Huber" over "Feinbäckerei GmbH") has neither inside the other and joins.
* **A date cut in half by the column gap** (`UnsplitDate`). `grid` and `pairs` print the
  day in a cell of its own, so `'19.'` and `'Dezember 2025'` split and neither half is a
  date. Two neighbouring segments rejoin where the pair parses and neither does alone; a
  row carrying two real dates has both halves parsing and is left split.

`TotalsVat` retries on rows carrying a `vatLabel` when the totals block states more than
one rate; two genuinely stated rates still yield nothing.

The word head is 13 wide on a checkpoint predating the label classes and 19 with them, so
on a v8 model no label is ever predicted and only the fallback path runs. `Tagger` reads
the width off the ONNX graph.

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

`Parse.Number` reads the last separator group of exactly two digits as the cents, whatever
character precedes it: German grouping is always three digits, so `24.332.16` is 24 332,16
with the comma degraded to a dot and not 24 332. `;` and `:` are never thousands separators
in the first place, so a group behind one of those is always the decimal part (`1.462;51`,
`40;460`).

`Parse.Date` parses the raw text first and, only if that yields nothing, once more through
a confusable pass: `O`/`I`/`l` back to `0`/`1` inside a token already shaped like a date,
and a month name replaced where exactly one of the twelve is one character away from it
(`0ktober`, `Mal`). Ambiguous ones — `Ju1i`, one character from both `juli` and `juni` —
are left alone.

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
