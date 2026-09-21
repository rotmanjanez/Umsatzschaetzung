# Eval

Tier 1 extraction metrics, in C#, through `Core`'s own `Extract.Assemble`: the number is
the app's number, not a second implementation's.

    dotnet run -c Release --project tools/eval                       # the real scans
    dotnet run -c Release --project tools/eval -- --full             # plus date and supplier
    dotnet run -c Release --project tools/eval -- --rows pred.jsonl  # a Python dump

    --corpus    fixtures/dataset/2025 unless given; holds the expected.json files and,
                without --rows, the <name>.ocr.json dumps to score
    --rows      page.jsonl: labelled or predicted words, one page per line, scored as is
    --split     with --rows; "" scores every split
    --parity    with --rows: re-tag the dump's words in C# and report word-for-word agreement
    --full      score date and supplier name too, not only what carries the estimate
    --out       the report
    --detail    per-variation TSV, for diffing two runs
    --show X    columns, cells and assembled-against-expected lines of invoices matching X

Without `--rows` the eval reads every `*.ocr.json` under the corpus, groups the words into
rows the way the app does, tags them through the app's ONNX path and assembles them with
`Core`. The dumps are what `tools/ocr` writes off the app's own rendering, cleaning,
straightening and OCR; they are gitignored, so refresh them with
`dotnet run -c Release --project tools/ocr -- fixtures/dataset/2025 --force` whenever
`Service/` changes. An invoice's `.pdf.scan` variation shares its `expected.json`; where the
PDF and the e-invoice XML both carry one, the PDF's wins.

A `--rows` word is `{t, box, row, pred|field, role, col, cell_start}`. `pred` wins over
`field`, so the same reader scores a prediction or verifies a ground truth: **run it on
ground truth first**, since whatever that loses is assembly loss and the ceiling for every
model. `tools/train/README.md` has the dump step.

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

## The item table

Nothing inside the table is typed by the model since v13; `Extract/Table.cs` reads it
from the structure heads, deterministically and in this order:

1. **Cells.** Per table row (`column-header`, `line-item`, `line-wrap`,
   `continuation`), words sorted by x, a new cell at every `cell_start` word.
2. **Columns.** Header and line-item cells cluster by x-overlap: a cell joins the
   column overlapping it most, among near-equal overlaps the one whose model column
   index matches, else the narrower one; no overlap opens a column. A header-only
   column merges into the body-only neighbour with the same index (a left-aligned
   header over right-aligned amounts). Wrap rows are placed afterwards without
   widening anything. A page without a header row starts from the previous page's
   columns.
3. **Meaning.** The header text first, longest dictionary key wins on a prefix
   (`Artikelnummer` before `Artikel`); then the body can contradict it — `Artikel`
   over numeric codes is the article id, and of two columns claiming one meaning
   only the one whose body fits keeps it. Undecided columns are typed by content:
   `%` is the rate, a running 1, 2, 3 is the position, unit words are the unit,
   two-decimal amounts and small counts stay candidates, the widest alphabetic
   column is the name, a code column left of it the article id. Among the
   candidates quantity × unit price = line net picks the triple that holds on most
   rows; with no row adding up the amounts read left to right as price then net.
4. **Rows.** One item per line-item row. Name text ends where a key opens (GTIN,
   Artikelnummer, Charge ...). A wrap row appends its name-column text and fills a
   cell the item lacks. A code-shaped first cell in the name column is the article
   id. `15 Stk` as one cell yields quantity and unit whichever column it landed in.

All 174 variations of the 100 invoices under `fixtures/dataset/2025` score 0.690 clean,
cost 0.4, through the app path on 2026-09-21 (0.483 / 0.6 with `--full`); line precision
and recall are both 1.000. What is left is the supplier name — the model tags a wordmark
where the sender line prints the legal name, and tags an XRechnung viewer's form
inconsistently — and one-cell OCR misreads on scans (`KI.` for `Kl.`, `Speisezwiebein`,
`Bt)` for `Btl`).

## Scales, from Extract/Parse.cs

    money (netTotal, grossTotal, lineNet)   cents   x100
    quantity                                milli   x1000
    unitPrice                               micro   x1000000
    vat                                     bp      x100    (700 = 7.00 %)

Verified against a real fixture: `134 Stk` x `0,34` = `45,56` reads back as
quantity 134000, unitPrice 340000, lineNet 4556.

## Metrics

Header exact-match after normalisation on number, date, supplierName, netTotal,
grossTotal. All of them are always measured and printed; by default only invoice number,
netTotal, grossTotal and the line items count towards the clean rate and the correction
cost, and the rest are marked `(not scored)`. `--full` counts date and supplier name too.
Lines are matched greedily on name similarity plus lineNet agreement, then precision,
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

**Synthetic val** — `tools/train/page.jsonl`, split by template; a v13 dump of it
needs the `col`/`cell_start` keys to assemble.

**Real scans** — 174 variations of the 100 invoices under `fixtures/dataset/2025`: the
digital PDF and, for 65 of them, a `.pdf.scan.<png|jpeg|pdf>` of the printed sheet. The
`*.ocr.json` dumps beside them are gitignored and come from `tools/ocr`; the eval reads
them straight through the app's row grouping and tagger. An earlier
`v13/real-v13.jsonl` covered only 109 of these — none of the 65 scans of cc, gemuese,
kaffee, metzgerei, nonfood, wein — off dumps made before straightening and cleaning
existed, and read a scanned PDF at 9920 x 14032 px; its 0.789 was not the app's number.

### Preprocessing: show-through, then straightening

`RapidOcr` cleans the reverse of the sheet off the page (`Deink`), straightens it
(`Deskew`) and does nothing else. The `AutoLevels` histogram stretch that `WindowsOcr`
carried was measured against RapidOCR, came out slightly worse, and has been removed
along with the `--raw` and `--no-levels` switches.

`Deink` works on ink depth — how far below the local paper level a pixel sits — so a grey
sheet, a shadow and a vignette cancel. Marks are split from paper by Otsu; a stroke is a
connected run of marks, and it stands if any part of it reaches **half the ink level**,
the median depth of what is clearly ink. Paper passes far less than half of what is
printed on its other side, so show-through never gets there and falls out whole, while a
word printed grey or in colour does and stays whole with its soft edges. The rule came
from the fixtures' own depth maps: a rendered PDF has no stroke under 0.55 of its ink
level, a double-sided scan piles up under 0.5. The earlier version split the marks a
second time by their own histogram, which on a rendered PDF parts dark text from darker
text — it threw away 30–55 % of the words on clean pages, among them every grey label
that anchors a header field, and a 9-cell invoice came back with 9 wrong cells.
Measured over the 65 scans and 6 clean PDFs (wrong cells, then clean invoices):

    second Otsu split (was)     70   30
    no cleaning                190   22      metzgerei show-through invents lines
    half the ink level (now)   71   34      and every clean PDF reads whole again

The lean is the angle whose horizontal projection is most peaked: straight text piles
into sharp bands and the profile spikes. The sweep runs coarse to fine over the bracket
a sheet feeder can produce — the worst real scan measured 4.9 degrees. The outer 15 % of
the page is cropped away first, because edge shadow and the sheet boundary stay axis
aligned however the sheet lay and otherwise outvote the text they frame; against the
generated corpus, which records the rotation it applied, cropping lifts the pages that
lock on from 28/40 to 39/40 and the rest land within 0.054 degrees of true.

It matters because word grouping bands by y. Once the lean carries a line further than
half a row height across the sheet, the far end of one line joins the next: names lose
their head, phantom lines appear, whole lines go missing. Measured on the three real
scans that lean that far, straightening takes 11 wrong cells to 6 and line recall from
0.778 to 1.000.

The corpus dumps and the real scans must be produced by the same engine.
