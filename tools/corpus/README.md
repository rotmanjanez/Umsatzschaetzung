# Synthetic invoice corpus

Generates scanned-invoice pages with exact word-level ground truth, for training the
ONNX word classifier described in `docs/extraction-eval.md`.

None of the layouts here are derived from the real scans in `fixtures/dataset/2025`.
Those stay test-only. What is shared is vocabulary — German trade article names, unit
texts, VAT rates — not a single column order or stylesheet.

## Running

From the repository root:

    python3 tools/corpus/generate.py --out fixtures/dataset/gen \
        --count 400 --variations 8 --workers 8 --seed umsatz-1

Needs Chromium (or Chrome) on the path, plus Pillow and numpy. Set `CORPUS_CHROME` to
point at a specific binary.

    --count        invoices (each gets its own directory)
    --variations   renderings per invoice, each with a fresh layout template
    --workers      parallel processes; one Chromium per worker (default: all cores)
    --scale        2, 3 or 4 device pixels per CSS pixel = 192, 288 or 384 dpi
    --formats      subset of png,jpg,pdf (default all three)
    --val-share    fraction of templates assigned to the validation split
    --start        first invoice index, for extending a corpus in place
    --verbose      one line per invoice instead of the progress bar

A progress bar reports pages done, throughput, bytes written and an ETA.

Everything is seeded from `--seed`, so the same flags reproduce the same corpus byte
for byte, and `--start` extends an existing one without redoing the earlier invoices.
The output is not checked in; regenerate it.

Throughput is about 1.2 s per page per worker, roughly 4–5 pages/s on eight cores,
and about 0.45 MB per page. Chromium is driven over the DevTools protocol
(`cdp.py`) with one long-lived browser per worker, because process startup used to
cost more than the rendering did.

## Layout

One directory per invoice, with the ground truth at the top and the renderings below:

    inv-000042/
      expected.json                      the invoice, in the shape of *.expected.json
      source.json                        category, supplier, injected defect
      v00-crisp-7a4ef82c5844/
        truth.json                       layout spec, degradation, word boxes + labels
        page-1.png
      v03-scan_worn-2ce4897604d1/
        truth.json
        page-1.jpg
        page-2.jpg

`expected.json` is layout-independent: every variation of an invoice renders the same
numbers, so the same ground truth grades all of them. Each variation draws a fresh
template, so one invoice appears in many unrelated layouts.

`truth.json` carries, per page, every rendered word with its box in *that image's*
pixel coordinates and its label — one of the twelve `Field` values or `O` — plus region
boxes tagged `line-item`, `column-header`, `continuation`, `group`, `total`, `carry`,
`footer`. The boxes are transported through the geometric part of the degradation, so
they stay correct under rotation and skew.

Template ids are stable hashes and each is assigned to `train` or `val`, so the
validation split is by layout, not by page: it measures generalisation to unseen
layouts.

## What varies

**Columns.** Presence and order of Pos, Artikel-Nr., EAN, Bezeichnung, Menge, Einheit,
Preisbasis, Einzelpreis, Rabatt, MwSt and Betrag, from ten base orderings. Quantity
before or after the name; unit as its own column or glued to the quantity ("12 Kt");
VAT per line or only in the totals block; discount column only when the invoice
actually carries discounts.

**Alignment and spacing.** Per-column right/left/centre alignment, varying cell padding
and leading, and a narrow-table mode that wraps the article name onto a second row.
Numeric columns never wrap; the name column absorbs the slack.

**Header vocabulary.** Menge / Anz. / Anzahl / Stk / Mng., Einzelpreis / E-Preis /
Preis/Einh. / EP / à Preis, Gesamt / Betrag / Summe / Netto / Wert, and so on — with
12 % of pages carrying no header row at all. Upper-casing, bold, inverted, underlined
and boxed header styles.

**Furniture.** Procedural logos (mark, wordmark, colour band, none), address block in
either corner, four metadata block styles, three totals styles, footers with bank
details in one to three columns, multi-page invoices with `Übertrag` carried totals and
page numbers, and delivery notes that carry no prices at all.

**Content.** Ten trade categories, Austrian and German suppliers with matching VAT
rates (20/13/10 vs 19/7), one or two rates per invoice, 1–60 lines, per-100-g price
bases, group subheadings and per-line detail rows. Article names are mostly real German
trade names, but a fifth of invoices lean on opaque ones — numeric-only codes,
consonant soup, abbreviated all-caps — and 8 % are almost entirely opaque, so the model
cannot learn to find the name column by recognising food words.

**Arithmetic.** Totals are consistent by construction. 15 % of invoices carry an
injected defect — rounding drift, a cent of slack, an extra charge outside the line
sum, a line missing from the total — so the model never learns that the numbers always
add up.

**Degradation.** Six profiles (`crisp`, `scan_clean`, `scan_worn`, `photocopy`, `photo`,
`fax`) combining rotation up to ~2°, shear, gaussian blur, sensor noise, JPEG
requantisation, gamma and contrast shifts, paper grain, vignetting, desk shadows,
speckle, dark photocopier edges and bilevel thresholding, plus occasional rubber stamps
and handwritten scribbles. Output is PNG, JPEG or an image-only PDF, which is what a
real scan is.

## Checking the labels

    python3 overlay.py ../../fixtures/dataset/gen/inv-000042/v03-scan_worn-2ce4897604d1 --out /tmp/ov

Draws every labelled word box onto the degraded page in a per-field colour. Use
`--all-words` to include the `O` words. Look at a handful after any change to the
renderer — a silent box/label drift is the one failure this corpus cannot survive.

## Real OCR

The render-time boxes are not the model's input. The model sees what the OCR engine
saw, errors and all, so every page is read back with the same `Windows.Media.Ocr` the
app itself runs — `tools/ocr`, which writes the words it found next to the image:

    dotnet run --project ../ocr -- ../../fixtures/dataset/gen

    inv-000042/
      v03-scan_worn-2ce4897604d1/
        truth.json
        page-1.jpg
        page-1.ocr.json

`page-1.ocr.json` carries the page size and every OCR word with its box, in the same
pixel coordinates as `truth.json` — PDF pages are rasterised back to exactly the size
they were written at, so the two line up without a transform. Pages that already have
a dump are skipped, so a run resumes where it stopped; `--force` redoes them.

Windows-only, and it needs the German OCR language pack (the „Optische
Zeichenerkennung“ feature under the Deutsch language options) — without it the tool
exits and lists what is installed. The same tool dumps the real scans in
`fixtures/dataset/2025`, which is what lets the eval run off Windows.

## Still to come

Transferring the labels from `truth.json` onto the OCR words by IoU plus text
similarity. That is the step that turns the two files above into training rows, and
it is where the genuine OCR errors become part of the supervision.
