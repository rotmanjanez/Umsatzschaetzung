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
pixel coordinates and its label — one of the twelve value classes, one of the six
label classes (`numberLabel`, `dateLabel`, `netLabel`, `grossLabel`, `vatLabel`,
`otherLabel`) or `O` — plus region boxes tagged `line-item`, `column-header`,
`continuation`, `group`, `total`, `carry`, `footer`. The boxes are transported through the geometric part of the degradation, so
they stay correct under rotation and skew.

Template ids are stable hashes and each is assigned to `train` or `val`, so the
validation split is by layout, not by page: it measures generalisation to unseen
layouts.

## What varies

**Columns.** Presence and order of Pos, Artikel-Nr., EAN, Bezeichnung, Größe/Variante,
Menge, Einheit, Preisbasis, Einzelpreis, Rabatt, MwSt, Betrag and the three unlabelled
collision columns Währung, Steuerschlüssel and Warengruppe, from twenty base orderings. Quantity
before or after the name; unit as its own column or glued to the quantity ("12 Kt");
VAT per line or only in the totals block; discount column only when the invoice
actually carries discounts.

**Seitenformat.** Sieben Formate, gewichtet in `layout.PAGE_MIX`: A4, US Letter, Legal,
Folio, B5, A5 und A4 quer. Das ist keine Kosmetik. `quantise_box` normiert x an der
Seitenbreite und y an der Seitenhöhe *getrennt*, also fällt das Seitenverhältnis gerade
nicht heraus — dieselbe Rechnung auf Letter (1,294) landet in anderen Bins als auf A4
(1,414). Die echten Scans in `fixtures/dataset/2025` sind ~88 % A4 und ~12 % Letter, und
die Letter-Seiten hängen alle an einem Lieferanten, also fällt der Fehler dort geballt an
und nicht als gleichmäßiges Rauschen. Die Auflösung dagegen ist egal: das Modell sieht
nie ein Pixel, und `tools/rapidocr/run.py --max-side` deckelt sie ohnehin vor der OCR.

`--page-mix "letter=38,a5=14,..."` überschreibt die Gewichte, um einen bestehenden,
reinen A4-Korpus mit `--start` um die anderen Formate zu ergänzen, statt ihn neu zu bauen.

Schmale Blätter (unter 180 mm: A5 mit 148, B5 mit 176) brauchten drei Anläufe, und die
Reihenfolge lohnt sich zu kennen, weil die ersten beiden je für erledigt gehalten wurden.

Zuerst fiel auf, dass A5 bei ~7 % fehlerhafter Variationen lag gegen ~1,2 % sonst, immer
fehlende `lineNet`-Wörter. Ursache: eine neunspaltige Tabelle quetscht auf 148 mm die
Betragsspalte so weit, dass ihre Wörter seitlich aus der Seite laufen. `overflow:hidden`
schneidet sie ab, die DOM-Sonde meldet sie trotzdem — sie stehen dann in der Wahrheit,
aber nicht im Bild. Die Überlauf-Schleife in `render.build` fing das nicht, weil sie nur
die Höhe maß. Jetzt meldet die Sonde auch `hoverflow`, und `build()` verkleinert Schrift
und Zellenabstand, bis die Zeile passt.

Danach blieben Restfälle, in denen die Meta-Tabelle über den *linken* Rand lief: sie sitzt
auf `justify-content:flex-end`, läuft also nach links aus und hat negative x-Werte, die
eine Prüfung nur gegen die rechte Kante nicht sieht. `hoverflow` misst seither beides.

Die letzten 3,5 % waren `meta_style:"row"` auf A5 — bei 5,6 pt, dem Schriftboden, immer
noch zu breit. Die Überlauflogik arbeitete korrekt und verkleinerte bis zum Anschlag; das
Layout ist auf 148 mm schlicht nicht darstellbar. `template()` schließt `"row"` auf
schmalen Seiten deshalb aus.

`NARROW_COLUMNS` streicht heute nur noch, was auch gedruckt keinen Sinn ergibt (GTIN neben
der Artikelnummer, Bemessungsgrundlage). Artikelnummer und MwSt-Spalte bleiben. Sie waren
anfangs mitgestrichen — als grober erster Fix, bevor `build()` die Breite messen konnte —
und das kostete A5/B5 *jede* `articleId`-Supervision: 0,00 statt 2,06 Wörter je Seite auf
10,9 % des Korpus. Das ist kein Fehler in der Wahrheit, die Seiten sind in sich stimmig;
es bringt dem Tagger aber bei, dass eine schmale Seite keine Artikelnummer trägt, und eine
echte A5-Rechnung mit Artikelspalte fällt ihm dann um. Gestrichene Spalten sind billig zu
übersehen, weil nichts auffällt: `validate.py` ist zufrieden, erst eine Feldabdeckung je
Format zeigt die Null.

Mit allen drei Korrekturen: 1400 Rechnungen, 14 000 Variationen über alle sieben Formate,
0 Beanstandungen.

**Alignment and spacing.** Per-column right/left/centre alignment, varying cell padding
and leading, and a narrow-table mode that wraps the article name onto a second row.
Numeric columns never wrap; the name column absorbs the slack.

**Header vocabulary.** Menge / Anz. / Anzahl / Stk / Mng., Einzelpreis / E-Preis /
Preis/Einh. / EP / à Preis, Gesamt / Betrag / Summe / Netto / Wert, and so on — with
12 % of pages carrying no header row at all. Upper-casing, bold, inverted, underlined
and boxed header styles.

**Furniture.** Procedural logos (mark, wordmark, colour band, none), address block in
either corner or centred, eight metadata block styles at five places, seven totals
styles, footers with bank details in one to three columns, multi-page invoices with
`Übertrag` carried totals and page numbers, and delivery notes that carry no prices at
all. Der ganze Raum steht unter „Breite des Template-Raums (v9)" weiter unten.

**Gebindegrößen.** `sizes.py` builds the size that goes *into the article name* —
"0,7 l", "Kt 6 x 0,7 l", "Btl. 20 Stk" — instead of drawing from the short hand-written
list per category (which it still uses for about one size in eight). It varies the
measure (l, ml, cl, kg, g, Stk, cm, m, %), the spelling (`l` / `L` / `ltr` / `Ltr.` /
`Liter`), whether a space separates number from unit, the container, and the multiplier
form. Containers are split into liquid and dry pools so no "Sack 0,33 l" comes out.

This exists because of a specific confusion: OCR reads the litre `l` after a decimal as
a `1`, and the tagger then calls that stray token `quantity` instead of `name`. About
18 % of sizes (`ADVERSARIAL_SHARE`) are therefore printed with an OCR-shaped misspelling
already baked in — "0,71", "0,7 I", "0,7l", "400 9". That is safe supervision, not a
poisoned label: the mutated string is what gets printed *and* what lands in
`expected.json`, so ground truth stays self-consistent and `validate.py` passes. Only
the size inside the name is ever mutated; quantity, price and total cells are never
touched, because there the printed text has to keep matching the number.

No U+00A0 between number and unit, tempting as it looks: `blocks.words` splits on
`str.split()`, which treats U+00A0 as whitespace, so the spans would be identical to the
plain-space case while the non-breaking space stayed behind in the expected name and
tripped `validate.py`.

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

**Degradation.** Ten profiles — `crisp`, `scan_clean`, `scan_worn`, `photocopy`, `photo`,
`fax`, `faded`, `dark`, `washed`, `low_ink` — combining rotation up to ~2°, shear,
gaussian blur, sensor noise, JPEG requantisation, gamma and contrast shifts, paper grain,
vignetting, desk shadows, speckle, dark photocopier edges and bilevel thresholding, plus
occasional rubber stamps and handwritten scribbles. Output is PNG, JPEG or an image-only
PDF, which is what a real scan is.

`low_ink` is the odd one out: every other profile models the *acquisition*, this one
models a page that was already badly printed before anyone scanned it — an empty
cartridge, a dry roller. `degrade.starve()` runs before the scan blur and only touches
the ink, never the paper: `arr + (255 - arr) * (1 - coverage)` is a no-op on white, so
the background stays put and only the glyphs go pale. That is what separates it from
`faded`, which squeezes the whole tonal range with `black`/`white`. Three knobs:
`ink_erode` thins strokes with a maximum filter, `ink_bands` lays in the horizontal
streaks a tired roller leaves, and `ink_blotch` varies coverage over two octaves — coarse
for empty patches, fine so individual strokes break up instead of merely greying out.
`ink_dropout` punches the last few holes. It is photometric only, so word boxes are
untouched.

## Kopfblock: Beschriftungen, Formen, Ablenker (v9)

Der Tagger las die Positionstabelle gut und verlor fast jeden Fehler im Kopf. Vier
Ursachen, alle im Korpus und keine im Modell:

1. Die Zusammensetzung nimmt je Feld den *ersten* getaggten Lauf der Seite. Ein früh
   falsch getaggtes Wort gewinnt damit gegen das richtige weiter unten — Telefonnummer,
   Kundennummer, Lieferschein-Nr. oder Steuernummer statt der Rechnungsnummer, ein
   MwSt-Betrag oder ein Währungszeichen statt der Summe. Genau diese Ablenker standen
   im Korpus so gut wie nie.
2. Kopfwerte wurden nur in der Form erkannt, in der der Korpus sie druckte: der
   Lieferantenname im Fließsatz auf einer eigenen Zeile, das Datum direkt hinter seiner
   Beschriftung in derselben Zeile. Eine Wortmarke, Versalien oder eine Beschriftung in
   einer anderen Zeile als ihr Wert bekamen keinen Tag — dafür bekamen ihn die
   Ähnlichkeiten daneben ("Inh."-Zeile, Kundenname).
3. Die Beschriftungen selbst trugen keine Klasse. Damit hatte das Modell keinen Anker,
   der "Rechnungsnummer" an das Wort daneben bindet, und keinen Weg, sie von
   "Kundennummer" zu unterscheiden.
4. Unbeschriftete Spalten kollidierten mit unseren: Pos/Artikelnummer neben der Menge,
   Prozent- und Steuerschlüssel neben dem Preis, Währungsspalte neben dem Betrag.

**Beschriftungsklassen.** Jedes Schlüsselwort im Kopf- und Summenblock trägt jetzt eine
eigene Klasse: `numberLabel`, `dateLabel`, `netLabel`, `grossLabel`, `vatLabel` — und
`otherLabel` für jeden Schlüssel, der *nicht* unserer ist (Kundennummer, Bestellnummer,
Lieferdatum, Fällig am, UID, USt-IdNr., Steuernummer, Tel, Fax, Mail, Web, IBAN, BIC,
Sachbearbeiter, Ihr Zeichen, Lieferschein-Nr., Auftrags-Nr. …). Ein getrennt gesetzter
Doppelpunkt gehört zur Beschriftung und bekommt deren Klasse. Die *Werte* der fremden
Schlüssel bleiben `O` — sie sind der Ablenker, nicht die Supervision. Die Spaltenköpfe
der Positionstabelle bleiben ebenfalls `O`: die tragen schon die Region `column-header`,
und eine zweite Beschriftungsklasse darüber würde nur die Kopfzeile verwässern.
`otherLabel` ist mit Abstand die häufigste der sechs, und das ist beabsichtigt: es ist
die Klasse, die "das hier ist eine Beschriftung, aber nicht unsere" trägt.

Der Doppelpunkt hat dabei eine eigene Falle. Gedruckt klebt er am Schlüssel, die OCR
liest also ein Wort "Rechnungsnummer:". Steht er in der Wahrheit als eigenes Token
daneben, sieht ihn niemand: seine IoU mit dem OCR-Wort liegt bei einer langen
Beschriftung unter der Schwelle von `align.py`, und er zählt als ungesehen. Gemessen auf
v9: einzeln gesetzte ":" sind zu 84–94 % ungesehen, alle anderen Wörter derselben Klassen
zu 5,0 % (`numberLabel`), 5,0 % (`dateLabel`) und 4,9 % (`otherLabel`) — also genau im
Bereich der Wertklassen (quantity 5,0 %, vat 5,0 %, unit 3,9 %). Nur weil der
Doppelpunkt in 35 % der Vorlagen an seinem Schlüssel klebt (ein Token) und lediglich in
20 % daneben steht, bleibt die Gesamtquote der Beschriftungsklassen bei rund 12 % statt
bei 25 %. Das ist kein Schaden an der Supervision — ein ungesehenes Wahrheitswort
erzeugt einfach keine Trainingszeile —, aber es verzerrt die Deckenrechnung, und wer die
Zahl ohne diese Aufschlüsselung liest, hält die Beschriftungsklassen für unlesbar.

**Kopfformen.** `meta_style` kennt neben `pairs`, `stack`, `boxed` und `row` vier neue:

* `stacked` — Beschriftung auf eigener Zeile, Wert darunter (kleiner gesetzter Schlüssel),
  höchstens vier Angaben, sonst wird der Block zu hoch.
* `grid` — eine Zeile Beschriftungen, eine Zeile Werte darunter, 3–5 Spalten
  (Rechnungsnummer | Datum | Kundennummer | …). Auf echten Rechnungen die häufigste
  Kopfform überhaupt und im Korpus bisher gar nicht vorhanden.
* `title` — Nummer und Datum stehen in der Überschrift: "Rechnung Nr. 2025/0123 vom
  12.03.2025". Die Überschrift ist dann selbst die Beschriftung (`numberLabel`), "vom"
  bzw. "Datum" ist `dateLabel`, und eine Meta-Zeile für Nummer und Datum gibt es nicht
  mehr — andere Schlüssel dürfen weiter in einer kleinen Tabelle stehen.
* `dateline` — "Wien, 12.03.2025" rechtsbündig über der Überschrift, *ohne*
  Datumsbeschriftung; der Ort ist `O`. Die Nummer steht dann in der Überschrift oder in
  einer Tabelle.

`row` bleibt auf schmalen Blättern ausgeschlossen (siehe oben) und ist seit v9 auf fünf
Angaben gedeckelt: mit allen Ablenkern darin schrumpfte `render.build` bis an den
Schriftboden und schnitt die Rechnungsnummer trotzdem ab.

Dazu `meta_place`: der Block sitzt unter der Überschrift, rechts oben neben der
Anschrift, in einem randlosen Panel über die volle Breite, direkt über der Tabelle, oder
geteilt — Nummer und Datum oben rechts, der Rest unten.

**Briefkopf.** Die Wortmarke druckte bisher nur das *erste* Wort des Lieferantennamens
und ließ es unbeschriftet. Sie brachte dem Modell damit aktiv bei, dass die größte
Schrift am Seitenkopf gerade *nicht* der Lieferant ist — und daran scheiterte es auf
echten Briefköpfen. Jetzt trägt die Wortmarke den vollen Namen und ist `supplier`, groß
gesetzt, ein- oder zweizeilig, mit optionaler Tagline (`O`). Versalien gibt es in zwei
Varianten: `text-transform:uppercase` ändert den DOM-Text *nicht*, die Sonde liest also
weiter gemischte Schreibung — für die Ausrichtung ist das folgenlos, weil `align.py`
casefold vergleicht, aber die Tokens unterscheiden sich dann eben nicht. Deshalb gibt es
daneben die echte Versalform, bei der der gedruckte Name wirklich aus anderen Tokens
besteht. In etwa 30 % der Wortmarken-Templates entfällt der Absenderblock, dann ist die
Wortmarke die einzige beschriftete Nennung. Fehlen Absender *und* Wortmarke, trägt die
Fußzeile den Namen beschriftet — `validate.py` besteht seit v9 darauf, dass jede
Variation irgendwo einen `supplier` hat, genau weil dieser Fall sonst still durchrutscht.

Neu daneben: eine "Inh. Vorname Nachname"-Zeile unter dem Absender (`O`, sieht aus wie
ein Lieferantenname und ist keiner), 0–4 Kontaktzeilen (Tel/Fax/E-Mail/Web/UID/Steuernr.,
Schlüssel `otherLabel`, Werte `O`) und eine optionale Überschrift
"Lieferadresse"/"Rechnungsadresse" über der Kundenanschrift. Der Kundenname kommt in
40 % der Fälle aus demselben Generator wie der Lieferantenname: eine Rechnung geht selten
an "Gasthaus Zur Alten Post", sondern meist an eine GmbH, die genauso klingt wie der
Absender. `expected.json` führt weiter den vollen gedruckten und beschrifteten Namen.

**Ablenker.** Im Kopf optional Lieferschein-Nr. (eine *Nummer*, kein Datum), Auftrags-Nr.,
Steuernummer/UID, Sachbearbeiter (ein Personenname), Ihr Zeichen, Kundennummer. Im
Summenblock optional "Skonto 2 % bis 20.03.2025: 124,60 €", "Bereits bezahlt 3.000,00 €"
und "Zahlbar bis 03.03.2025" — Schlüssel `otherLabel`, Beträge `O`. Der MwSt-*Betrag*
bleibt `O`, der *Satz* `vat`, die Beschriftung `vatLabel`.

**Kollisionsspalten.** Drei neue, alle unbeschriftet (`O`): `waehrung` (EUR/€ direkt
neben Betrag oder Preis, Kopf "Währ."/"EUR"/"Whg"), `steuercode` (A/B/1/2/N/E neben
Preis oder Betrag, Kopf "St"/"StC"/"Code"/"MwSt-Kz") und `wg` (Warengruppe, Kopf
"WG"/"WGr"/"Gruppe"). Sie stehen in eigenen Spaltenreihenfolgen und werden sonst an
ihrem natürlichen Platz eingeschoben (`insert_column`), damit sie in *jeder* Reihenfolge
vorkommen können und nicht nur in den sechs, die sie zufällig führen. Auf A5/B5 sind sie
nachrangig (Faktor 0,35), also dort selten bis gar nicht — das ist der bewusst
akzeptierte Rest der `NARROW_COLUMNS`-Abwägung.

Dazu in ~40 % der Templates ein Währungszeichen in der Preis- oder Betragszelle selbst
("12,50 €", "€ 12,50", "12,50 EUR", "EUR 12,50"). Das Zeichen ist ein eigenes Token und
bleibt `O`, die Zahl behält ihre Klasse; geklebt ginge es nicht, weil das Token dann
"12,50€" hieße und `validate.py` es gegen `expected.json` prüft. `tools/eval/parse.py`
entfernt €/EUR ohnehin vor dem Parsen. Und `pos` wird gemischt als "1", "1.", "001" oder
"0010" gesetzt — eine Zahl links neben der Menge, die keine Menge ist.

## Breite des Template-Raums (v9)

Die vier Fehlerbilder oben waren der Anlass, der kombinatorische Raum von
`layout.template()` ist das eigentliche Produkt: jede Achse wird je Template gezogen, und
zwei Variationen derselben Rechnung haben nichts miteinander gemein außer den Zahlen.
Mit v9 dazugekommen oder verbreitert:

* **Briefkopf** — Logo links, rechts, mittig oder keins; Absender unter, neben oder über
  dem Logo, rechtsbündig, oder gar nicht (dann trägt ihn die Fußzeile); Wortmarke ein-
  oder zweizeilig, mit Tagline, in Versalien (CSS oder echt).
* **Anschrift** — links, rechts oder mittig (Fensterposition), mit oder ohne Rücksendezeile,
  mit optionaler Überschrift.
* **Kopfdaten** — acht Formen (`meta_style`) an fünf Plätzen (`meta_place`), Reihenfolge
  der Angaben gemischt, Datum auch vor der Nummer, Doppelpunkt an oder aus.
* **Überschrift** — normal, gesperrt, Kapitälchen, unterstrichen, gerahmt, als Farbband,
  linksbündig/zentriert/rechtsbündig, und in ~8 % gar keine.
* **Schrift** — 28 Familien, Kopf- und Fließschrift getrennt gezogen (Serifen im Satz,
  Grotesk im Kopf und umgekehrt), Grade 6,9–11,4 pt, Durchschuss 1,06–1,72,
  Zellenabstände 0,5–4,0 mm × 0,5–5,2 mm, Seitenränder 9–26 mm.
* **Tabelle** — elf Bilder: Gitter, Linien, Zebra, linienlos, doppelte Kopflinie, nur
  eine dicke Kopflinie, nur senkrechte Striche, gepunktet, getönte Spalten.
* **Spaltenköpfe** — acht Stile inklusive Kapitälchen, Akzentfarbe und gesperrt.
* **Summen** — Block, gerahmt, Tabelle, Panel mit dicker Oberlinie, und einzeilig inline
  ("Netto 100,00 MwSt 20,00 Brutto 120,00"); rechts, links oder über die volle Breite.
* **Fußzeile** — Bankblock, zwei oder drei Spalten, einzeilig, Adresszeile, keine;
  Seitenzahl oben rechts, unten mittig oder unten links, als "Seite 1 von 2",
  "Seite 1/2", "- 1 -" oder "1 / 2".
* **Farbe und Kontrast** — 16 Akzentfarben, sieben Grauwerte für den Fließsatz, getrennt
  gezogene Linienfarben und -stärken.
* **Wortlose Flächen** — in ~10 % ein QR-ähnlicher Block, in ~8 % ein schräg gesetzter
  Stempelrahmen. Beide tragen kein `span.w`, stehen also in keiner Wahrheit; die OCR
  sieht sie trotzdem und liefert Rauschwörter. Genau das tun echte Scans auch.

Jede Kombination muss ausrichtbar bleiben und `validate.py` bestehen. Was dabei nicht
auffällt, zeigt `coverage.py`: Wörter je Seite, je Klasse und je Seitenformat. Eine Null
darin heißt, dass das Modell für dieses Format "gibt es hier nicht" lernt — der gleiche
Fehler wie damals bei der gestrichenen Artikelspalte auf A5.

    python3 coverage.py /var/tmp/rotman/corpus-v10

## Checking the labels

    python3 overlay.py ../../fixtures/dataset/gen/inv-000042/v03-scan_worn-2ce4897604d1 --out /tmp/ov

Draws every labelled word box onto the degraded page in a per-field colour. Use
`--all-words` to include the `O` words. Look at a handful after any change to the
renderer — a silent box/label drift is the one failure this corpus cannot survive.

## Real OCR

The render-time boxes are not the model's input. The model sees what the OCR engine
saw, errors and all, so every page is read back through the app's own page pipeline —
`tools/ocr`, which renders, cleans, straightens and reads with RapidOCR exactly as
`Service/` does and writes the words it found next to the image:

    dotnet run -c Release --project ../ocr -- ../../fixtures/dataset/gen --dpi 288

    inv-000042/
      v03-scan_worn-2ce4897604d1/
        truth.json
        page-1.jpg
        page-1.ocr.json

`page-1.ocr.json` carries the page size and every OCR word with its box, in the same
pixel coordinates as `truth.json` — PDF pages are rasterised back to exactly the size
they were written at (`--dpi` is 96 times `--scale`), so the two line up without a
transform. Pages that already have
a dump are skipped, so a run resumes where it stopped; `--force` redoes them.

The same tool dumps the real scans in `fixtures/dataset/2025`, which is what the eval scores.

## Von hier zu den Trainingszeilen

`tools/train/align.py` trägt die Klassen aus `truth.json` per IoU (plus Textähnlichkeit
in den Zweifelsfällen) auf die OCR-Wörter und schreibt `page.jsonl`. Das ist der Schritt,
der aus den beiden Dateien oben Trainingszeilen macht, und dort werden die echten
OCR-Fehler Teil der Supervision. Er meldet auch die Decke — ungesehen, verlesen,
erreichbar —, je Feld und je Degradationsprofil.

    python3 ../train/align.py --corpus /var/tmp/rotman/corpus-v9 --out page-v9.jsonl --workers 48
    python3 coverage.py /var/tmp/rotman/corpus-v9 --jsonl page-v9.jsonl

## Einheiten

Die Codes stehen nur in `data/units.json`. `vocab.py` führt je Warengruppe den
gedruckten Einheitentext und die Stückzahl je Gebinde; den Code holt `content.py`
über `tools/units.py` aus derselben Datei, die `Umsatzschätzung.Core/Model/Units.cs`
einbettet. UN/ECE Rec 20: Packmittel aus Rec 21 tragen dort das Präfix X — `XCT`,
nicht `CT`.

## Korpus v10: Breite

v9 machte den Kopfblock lesbar. Danach gemessen — 1500 Variationen des Korpus gegen die
echten Belege des Nutzers — blieb der Befund, dass der Korpus zu *schmal* ist: er druckt
jede Rechnung nach derselben Grundidee und variiert nur die Oberfläche. Echte Rechnungen
kommen aus einer Handvoll Programme, und jedes druckt Dinge, die der Korpus gar nicht
kannte. v10 fügt sie hinzu.

### Familien statt Würfel

Der rein zufällige Wurf über alle Achsen erzeugt Vorlagen, die es so nie gibt — eine
Garamond mit Farbband und Kassenbon-Summen. `layout.FAMILIES` legt deshalb zehn benannte
Vorlagenfamilien an, jede ein zusammenhängender Satz von Achsen, wie ihn ein bestimmtes
Programm druckt. Gezogen wird zuerst die Familie, dann wie bisher jede Achse einzeln; die
Familie überschreibt anschließend nur die Achsen, die sie ausmachen. Alles andere bleibt
gewürfelt, der Raum wird also nicht enger, nur plausibler.

| Familie | Anteil | Was sie festlegt |
|---|---|---|
| `free` | 42 % | nichts — die freie Ziehung wie in v9 |
| `form` | 8 % | Formularsatz: gerahmter Kopfblock rechts mit Doppelpunktspalte, gesperrte Überschrift mit der Nummer, Bildwortmarke statt Absender, Summen ganz unten, Barcode, winzige Spaltenfußzeile |
| `word` | 8 % | klassischer Briefsatz aus der Textverarbeitung, Serifen, schlichte Tabelle, Summen auch als Satz |
| `lexware` | 7 % | Lexware/sevDesk: gerahmter Kopf rechts oben, Gittertabelle, Grotesk |
| `shop` | 7 % | WooCommerce/Shopify: Farbband, Webschrift, Summen als Schlüssel-/Wertzeile |
| `datev` | 6 % | nüchtern, Linien, gerahmter Kopf |
| `sap` | 6 % | ERP-Ausdruck: dicht, schmal oder dicktengleich, alle drei Codespalten, Einheiten im Spaltenkopf |
| `ninja` | 6 % | InvoiceNinja: Absender mittig oben, Kunde neben den Kopfdaten, Einheit vor dem Preis, Währung in jeder Zelle |
| `amazon` | 5 % | schmale Grotesk, Kopfraster, winzige Fußzeile |
| `receipt` | 5 % | Kassenbon auf 80 mm |

Die Familie steht in `truth.json` unter `layout.family`, `coverage.py` berichtet ihre
Verteilung.

### Neue Achsen

* **Nackte Mengen.** 25 % der Rechnungen drucken bei Stückware **gar keine Einheit** —
  weder als Spalte noch an der Menge. Auf den echten Belegen sind das 14,5 % der Zeilen,
  im Korpus 20,8 %.
* **Wortlose Flächen hinter dem Satz.** Große blasse Form (Kreis, Ringe, Dreieck,
  Streifen, Klecks, Raster) mit Deckkraft 0,06–0,18 hinter der Tabelle oder über die
  ganze Seite, farbiges Kopfband, getöntes Panel hinter den Kopfdaten, grauer Kasten
  hinter den Summen. **Nie Text**: eine verblasste Wortmarke aus Buchstaben läse die OCR
  mit, und die Wahrheit könnte sie nicht sauber beschriften. Nur Formen, nur SVG.
* **Freizeilen.** 12 % der Rechnungen tragen 1–3 Beigaben ("Herzlichen Dank", "Flyer",
  "Paketbeilage") mit Menge 1 und **leerer** Preis- und Betragszelle.
* **Zweizeilige Spaltenköpfe** ("Einzelpreis / netto", "Menge / Einheit") und Köpfe mit
  der Einheit in Klammern ("Menge (Stk)", "Betrag (EUR)").
* **Summenzeilen, die den Nettobetrag verändern.** Versandkosten, Fracht, Verpackung,
  Pfand/Leergut (plus oder minus), Rabatt, Rundung, Mindermengenzuschlag. Dazu die
  Summen als Schlüsselzeile über Wertzeile (Warenwert | Versand | Steuerpflichtiger
  Betrag | MwSt % | MwSt.-Betrag | Summe), links statt rechts, und als Satz
  ("Rechnungsbetrag: 70,81 EUR").
* **Größen-/Variantenspalte** zwischen Artikelnummer und Bezeichnung (S/M/L/XL, 42,
  0,5 l, Farbe), unbeschriftet `O`.
* **Positionen über mehrere Zeilen.** Bezeichnung auf zwei Zeilen mit eingerücktem
  Rest, oder Mengen- und Preisangaben in einer Zeile für sich unter dem Namen, dazu die
  Detailzeile mit Charge/MHD/Seriennummer/Farbe.
* **Gutschriften und negative Beträge.** 8 % der Belege sind Gutschriften
  ("Gutschrift", "Rechnungskorrektur", "Stornorechnung") mit durchgehend negativen
  Beträgen; dazu Pfand- und Leergutzeilen mit negativem Positionsbetrag auf normalen
  Rechnungen.
* **Zahl- und Datumsformate.** Tausendertrennung als Punkt, als Leerzeichen ("1 234,56"
  — zwei Tokens!) oder gar nicht; Minus vorn oder hinten ("70,81-"); Preise mit 2, 3
  oder 4 Nachkommastellen; Mengen getrimmt, zwei- oder dreistellig ("2,5" / "2,50" /
  "2,500"); Datum als 14.07.2026, 14.7.26, 14. Juli 2026, 2026-07-14, 14/07/2026.
* **Kassenbon.** Eigenes Seitenformat `receipt` (80 × 240 mm) mit eigenem Zeilenbudget,
  dicktengleicher oder schmaler Schrift, zentriertem Kopf, ohne Linien, "2 Stk x 1,50"
  auf einer Zeile und dem Betrag daneben oder darunter. Mehrseitig wie jede andere
  Rechnung, `render.build` deckelt den Überlauf genauso.
* **Fotorealismus.** Das Profil `photo` legt jetzt eine Unterlage rings um das Blatt
  (Schreibtisch, Kuvert, dunkle Platte), einen Schlagschatten unter der Blattkante, ein
  bis zwei waagrechte Knickfalten und gelegentlich eine perspektivische Verzerrung an.
  Bild **und Wahrheitsboxen** laufen durch dieselbe 3x3-Abbildung (`degrade.geometry`);
  `warp_boxes` teilt seit v10 durch die dritte Zeile, rechnet also auch projektiv
  richtig.
* **Ablenkerziffern.** Barcode mit seiner Nummer darunter (die Nummer ist gedruckter
  Text und bleibt `O`), Druckcode "R1 31550133" in der Fußzeile, Kundennummer im
  Anschriftenfenster, und die Kopfzeile als *eine* Zeile
  ("Kunden-Nr. K31550133   Rechnung R185518416   14.07.2026   Seite 1/1").
* **Schriften und Schnitte.** Die Familienliste führt nur Schriften, die auf dem
  Rechner wirklich installiert sind — eine Wunschliste fällt still auf dieselbe
  Ersatzschrift zurück und bringt keine Varianz. 20 Grotesk, 8 schmale, 21 Serifen,
  10 dicktengleiche, dazu zehn Display-Schriften für Wortmarken. Fließsatz in vier
  Schnitten (300–600), leicht gesperrt oder leicht geschlossen, Fußzeilen 0,55–0,82 em,
  Überschriften 1,3–2,9 em.
* **Kopfzeile der Tabelle.** Zusätzlich ein Stil `bodyrow` (die Kopfzeile sieht aus wie
  eine Datenzeile), eine Überschriftzeile über der Tabelle statt einer Kopfzeile, und
  die Kopfzeile steht wie bisher auf jeder Seite.
* **Nordfoto-Merkmale.** Doppelpunkt als eigene Spalte ("Datum      :  17.03.2025"),
  ein Formularfeld mit leerem Wert ("Kd-UStIdNr. :"), gesperrte Überschrift mit der
  Nummer ("R E C H N U N G   N R.   2503561" — der ganze Schlüssellauf ist
  `numberLabel`), zweiteilige Bildwortmarke mit Schwung und Werbesatz statt Absender,
  Informationszeilen mitten in der Tabelle ("Shopbestellung: 116573 / 17.03.2025 / 2"),
  Seriennummernzeilen, gekaufter Zahlungsweg in einem Kasten zwischen Tabelle und
  Summen, Summen ganz unten auf der Seite, Lieferanschrift als Fließsatz,
  vier- bis fünfspaltige Fußzeile in 5–6 pt mit Kapitälchen-Überschriften.

### Konventionen der Wahrheit

Drei Regeln, die aus den echten `expected.json` des Nutzers kommen und die der Korpus
seit v10 einhält:

1. **Keine gedruckte Einheit heißt `unitText: null` und `unitCode: "H87"`.** Nicht
   "Stk" raten, nicht die Einheit aus dem Gebinde im Namen ziehen. `validate.py` prüft
   beides: der Code muss H87 sein, und für so eine Zeile darf **kein** `unit`-Wort in
   der Wahrheit stehen.
2. **Versand und Zuschläge stecken im Nettobetrag, nicht in den Positionen.**
   `netTotal` = Summe der Positionen + Summe der gedruckten Zuschlagszeilen. Die Zeile
   "Warenwert" darüber ist damit **nicht** der Nettobetrag und bleibt `O`; die
   Zuschlagsbeschriftung ist `otherLabel`, ihr Betrag `O`. `validate.py` rechnet das
   nach (die Zuschläge stehen in `source.json`, weil `expected.json` die Form der App
   behält).
3. **Freizeilen drucken leere Zellen, keine Null.** In `expected.json` stehen sie mit
   `unitPrice` 0 und `lineNet` 0; gedruckt ist die Zelle leer. Eine gedruckte "0,00"
   wäre eine andere Rechnung. `validate.py` besteht darauf, dass für so eine Zeile kein
   `unitPrice`- und kein `lineNet`-Wort in der Wahrheit steht.

`validate.py` vergleicht Beträge seit v10 nicht mehr gegen *eine* Schreibweise, sondern
gegen alle, die die Vorlagen drucken können (`money.forms`): Punkt-, Leerzeichen- oder
keine Tausendertrennung, Minus vorn oder hinten.

### Regionen bei mehrzeiligen Positionen

Eine Position kann über zwei oder drei gedruckte Zeilen gehen. Die Region `line-item`
muss sie alle umfassen und es darf **genau eine** Region je Position geben:
`align.py` nummeriert die Positionen über die Reihenfolge der `line-item`-Regionen, und
zwei Regionen für eine Position brächten die Zusammensetzung aus dem Tritt. Deshalb
liegt die Region seit v10 auf einem eigenen `tbody` je Position statt auf der
einzelnen `tr`; die Folgezeilen bekommen in `align.wrap_rows` von selbst die Rolle
`line-wrap`. Die Detailzeile (Charge, MHD, Seriennummer) steht weiter in einem eigenen
`tbody` mit der Rolle `continuation` und gehört damit wie bisher zu keiner Position —
das war schon in v9 so und bleibt es.

### Verwaiste Chrome-Prozesse

v9 hinterließ nach jeder Generierung Hunderte Chrome-Prozesse an init. Die Ursache ist
nicht das fehlende Aufräumen, sondern dass `chromium` auf diesen Rechnern ein AppImage
ist: unser Kindprozess ist nur dessen Starter, und ein SIGTERM an ihn lässt Browser,
Zygoten und Renderer stehen. `cdp.Browser` startet den Browser jetzt in einer eigenen
Prozessgruppe (`start_new_session`) und schießt beim Schließen die ganze Gruppe ab;
`render.reset()` schließt Seite *und* Browser, und `generate.one()` ruft es am Ende
jeder Rechnung — ein multiprocessing-Worker verlässt den Prozess mit `os._exit()` und
führt weder `atexit` noch einen anderen Haken aus. Nachgemessen: 0 statt 126 verwaiste
Prozesse.

### Was v10 gekostet hat: die Decke

`align.py` über den fertigen Korpus (20 000 Variationen, 34 150 Seiten, 4 464 139
OCR-Wörter, 44,2 % beschriftet):

    ungesehen 6,10 %   verlesen 4,74 %   erreichbar 89,16 %

Das ist mehr als die 3,24 % von v9, und die Aufschlüsselung sagt, warum — jedes Stück
davon ist eine Achse, die v10 absichtlich dazugenommen hat:

| Klasse | ungesehen | davon |
|---|---|---|
| `numberLabel` | 16,4 % | einzeln gesetzter Doppelpunkt 62,3 % (337 Wörter), Einzelbuchstaben der gesperrten Überschrift 15,4 % (1512), **normaler Text 4,6 %** |
| `otherLabel` | 14,8 % | einzeln gesetzter Doppelpunkt 85,0 % (1515), **normaler Text 5,4 %** |
| `dateLabel` | 20,5 % | Doppelpunkt 86,4 %, **Text 4,5 %** |
| `quantity` | 6,6 % | Tokens bis zwei Zeichen 12,5 %, **längere 0,8 %** |
| `unit` | 5,6 % | bis zwei Zeichen 8,5 %, längere 2,5 % |
| `vat` | 5,6 % | bis zwei Zeichen 5,8 % |
| `name`, `lineNet`, `unitPrice`, `netTotal`, `grossTotal`, `articleId`, `invoiceNumber`, `invoiceDate` | 0,9–2,3 % | — |

Der Doppelpunkt in eigener Spalte (`meta_colon: "column"`, 16 % der Vorlagen) und die
gesperrte Überschrift sind beide gewollt und beide für die OCR unsichtbar: ein einzelner
Doppelpunkt in einer eigenen Tabellenzelle wird gar nicht erst als Wort erkannt, und aus
"R E C H N U N G" liest die OCR ein Wort statt acht. Beides kostet keine Supervision — ein
ungesehenes Wahrheitswort erzeugt keine Trainingszeile —, es verzerrt nur die
Deckenrechnung. Die nackten Mengen kosten echte Decke: ein einzelnes "1" in einer Spalte
ist ein Ziel, das die OCR oft nicht als eigenes Wort schneidet. Das ist der Preis dafür,
dass der Tagger diese Zeilen überhaupt zu sehen bekommt.

Je Profil: `scan_clean` 2,4 %, `crisp` 3,3 %, `scan_worn`/`faded` 4,1 %, `dark` 4,4 %,
`photo` 5,9 %, `photocopy` 6,3 %, `low_ink` 6,7 %, `washed` 7,5 %, **`fax` 17,4 %**.
Je Seitenformat: `receipt` **2,3 %** (der Bon ist dicktengleich und sauber gesetzt),
letter/legal 4,9 %, a4 5,7 %, folio 6,2 %, a5 6,8 %, b5 7,6 %, a4 quer 7,8 %.

## Korpus v11: hundert Familien statt zehn

v10 hatte zehn benannte Familien und 42 % freien Wurf; das war der größte Gewinn von v1
auf v10, also wurde er vervielfacht. `layout.FAMILIES` führt jetzt **113** Familien, die
103 neuen stehen samt allen neuen Bausteinen in **`families.py`**. `free` hält weiter
26,6 % — die Familien sollen den Raum ordnen, nicht einengen. Die Tabelle aller 113 mit
Gewicht, Vorbild und prägenden Achsen steht in `../../v11/REPORT-families.md`, eine
Musterseite je Familie unter `../../v11/samples/`.

Gruppen: Rechnungsprogramme (sevDesk, Lexoffice, easybill, Billomat, FastBill, Zervant,
orgaMAX, WISO, Sage, Odoo, Xero, QuickBooks, Stripe, PayPal, SumUp, InvoiceNinja, Word-
und Excel-Eigenbau, LibreOffice, JTL-Wawi), Shops und Marktplätze (Amazon, Otto, Zalando,
eBay, Etsy, Shopify, WooCommerce, Shopware, JTL, Magento, Conrad, Reichelt, Würth,
Baumarkt, IKEA, App-Store), Lebensmittel- und Getränkehandel (Metro, Selgros,
Transgourmet, Chefs Culinar, Edeka Foodservice, Rewe-Sammelrechnung, Discounter-Bon,
Großmarkt-Thermobon, Getränkemarkt, Brauerei, Bäckerei, Metzgerei, Wochenmarkt-Quittung,
Kaffeerösterei, Obst/Gemüse, Fisch, Weinhandel, Tiefkühl), Dienstleistung und Versorger
(Telekom, Vodafone, 1&1, Stadtwerke, Gasversorger, DHL, Paketdienst, Versicherung, Miete,
Kfz-Werkstatt, Handwerker, Hotel, Bewirtungsbeleg, Taxi, Tankstelle, Bahn, Fluglinie,
Arzt, Apotheke, Dienstleister), Belegarten (Gutschrift, Kleinunternehmer, Reverse Charge,
Schweiz, Abschlag, Schlussrechnung, Proforma, Lieferschein+Rechnung, Sammelrechnung,
Österreich, englisch, niederländisch, italienisch, polnisch), E-Rechnungs-Viewer
(XRechnung, ZUGFeRD, Portal, Peppol) und reine Strukturfamilien (zweispaltig,
Schlüssel-Wert, Querformat, Seitenstreifen, Nadeldrucker, invertiert, Riesenlogo,
linienlos, Nordfoto-Formular, Doppelpunktspalte, Halbbogen).

### E-Rechnungs-Viewer

Vier der acht echten Spirituosen-Fehler sehen aus wie der Ausdruck eines
XRechnung-Viewers, und die Form kannte der Korpus gar nicht. `families.einvoice_page`
baut die Seite vollständig selbst: jeder Wert hinter einem Schlüssel, **Käuferblock vor
Verkäuferblock**, Abschnitte in Kästen, der Lieferantenname bricht im schmalen
Formularfeld über zwei Zeilen, eine Position ist ein Block aus drei bis vier Zeilen mit
`Artikelnummer:`/`Artikelkennung:`/`Schema der Artikelkennung: 0160`, eine
`Preiseinheit`-Spalte (`1 XBO` → Zahl `priceBasis`, Code `unit`) zwischen Preis und
Prozentspalte, und ein Summenblock mit `Summe aller Positionen`,
`Summe Fremdforderungen 0,00`, **zwei** `Gesamtsumme`-Zeilen und `Fälliger Betrag`.

### Belegarten, die Zahlen verändern

`expected.json` wird je *Rechnung* geschrieben, die Familie aber je *Variation* gezogen.
Eine Kleinunternehmerrechnung kann deshalb keine Familie sein: zwei Variationen derselben
Rechnung müssten sonst verschiedene Beträge drucken. Sie ist eine eigene Achse `doctype`,
die `generate.one()` je Rechnung zieht (`families.DOCTYPES`, 90/4/3/3);
`families.adapt()` rechnet die Summen neu und `layout.template()` zieht die Familie dann
aus `families.DOCTYPE_FAMILIES`. `--doctype` erzwingt sie.

* `kleinunternehmer` / `reverse_charge`: jeder Satz 0, `vatBreakdown` leer, keine MwSt-
  und keine Steuerschlüsselspalte, §19- bzw. §13b-Hinweis. **Diese beiden Familien
  tragen die Klasse `vat` nicht — Absicht, kein Loch** (`v11/famcheck.py` führt das als
  `BY_DESIGN`).
* `swiss`: 8,10 % und 2,60 %, CHF, QR-Rechnung am Blattfuß.

### Eine Achse, zwei Würfe

`layout.template()` zieht das Seitenformat **vor** `apply_family`, weil `receipt`,
`narrow_page` und die Spaltenauswahl daran hängen. Zog `apply_family` es danach ein
zweites Mal, fielen die Würfe verschieden aus, und eine Familie mit
`page_format: ["a5","a4","receipt"]` bekam `page_format "receipt"` bei `receipt False`:
den vollen A4-Satz auf 80 mm Rollenbreite. `render.build` schrumpfte bis an den
Schriftboden, die Betragsspalte lief trotzdem aus der Seite, `overflow:hidden` schnitt
sie ab, und die Wörter fehlten in der Wahrheit — 74 Beanstandungen in der ersten
v11-Probe, alle aus dieser einen Zeile. **Jede Achse, die `template()` vor
`apply_family` auswertet, gehört in `CONTROL_KEYS`**: heute `font_pool`, `force_codes`,
`order_pick` und `page_format`.

### Schräglage: die Zeile zerfällt, die Rolle bleibt

`align.group_rows` (Portierung von `Rows.GroupRows`) hängt ein Wort an die erste Zeile,
deren *Anker* senkrecht mit ihm überlappt — und der Anker wächst nie mit. Eine 180 mm
breite Positionszeile fällt bei 2° Drehung um gut zwei Zeilenhöhen ab, also zerfällt sie.
Gemessen mit `v11/skewrows.py` über 2 672 Seiten: **1,61 OCR-Zeilen je Position bei 0°,
3,00 bei 2°, 3,46 darüber**. Die *Rollen* bleiben dabei korrekt — genau eine
`line-item`-Zeile je Position in 98,6–100 % der Fälle, der Rest `line-wrap`.

Kaputt ist etwas anderes: `align.item_of` prüft Punkt-in-**Rechteck**, und das Rechteck
einer gedrehten Positionszeile ist dreimal so hoch wie die Zeile und überlappt die
Nachbarn. **19,5 % der beschrifteten Wörter landen bei 2° bei der falschen Position.**
`degrade.warp_quads()` legt deshalb seit v11 neben `box` auch `quad` (vier Ecken) in
jede Region; mit einer Punkt-in-Polygon-Prüfung fällt der Fehler auf 0,0 %. Die
Änderung in `align.item_of` steht noch aus.

Dazu die Ursache, warum der Fall im Training kaum vorkam: `|Drehung| ≥ 1,75°` lag bei
**2,8 %** der Seiten, und fast alles davon im Profil `photo` — also im Handyfoto mit
Unterlage und Knick, nicht im Scan. v11 fügt das Profil **`scan_skew`** hinzu (rotate
3,0, sonst ein normaler Scan) und weitet die scanartigen Profile (`scan_worn` 1,8 → 2,4,
`photocopy` 1,4 → 2,0, `washed` 1,5 → 2,2, `dark` 1,3 → 1,9, `fax` 1,2 → 1,8).
Gemessen: `≥ 1,75°` **2,8 % → 13,6 %**, `≥ 2,25°` **0,3 % → 2,5 %**, während `≥ 1,0°`
nur von 29,9 % auf 34,4 % steigt.

### Prüfen

    python3 generate.py --out /var/tmp/rotman/probe --count 8 --variations 113 \
        --workers 10 --scale 2 --formats png,jpg --family "$(python3 -c '
import layout; print(",".join(layout.FAMILY_NAMES))')"
    python3 validate.py /var/tmp/rotman/probe
    python3 coverage.py /var/tmp/rotman/probe
    python3 ../../v11/famcheck.py /var/tmp/rotman/probe    # Klassendeckung je Familie
    python3 ../../v11/skewrows.py /var/tmp/rotman/probe    # Schräglage -> Zeilen, Rollen

`famcheck.py` steht neben `coverage.py`, weil es etwas anderes misst: `coverage.py` zählt
je Seitenformat, `famcheck.py` je Familie. Eine Familie, die über alle ihre Variationen
nie eine `articleId` druckt, bringt dem Modell „so ein Beleg hat keine Artikelnummer"
bei — derselbe Fehler wie damals die gestrichene Artikelspalte auf A5, nur eine Ebene
tiefer.

### Die Achse `tagline`: der Werbesatz neben dem Namen

Gemessen an den 109 echten Scans hat v11 genau eine Gruppe verschlechtert (26 Belege
eines Lieferanten). Der Briefkopf dort liest

    METZGEREI HOFMANN
    Inh. Georg Hofmann   Fleisch und Wurst aus eigener Schlachtung

und v11 taggte `Fleisch und Wurst aus eigener Schlachtung` mit 0,83–0,91 Konfidenz als
`supplier`, also hiess der zusammengesetzte Name
`METZGEREI HOFMANN Fleisch und Wurst aus eigener Schlachtung`. v10 tat das nicht.

Die Ursache liegt im Korpus, nicht im Modell: den Werbesatz kannte er nur in **einer**
Gestalt — VERSALIEN, Akzentfarbe, oben rechts (`vocab.CLAIMS`, `blocks.head_block`,
`render.py .claim`). Eine gemischt gesetzte Zeile *neben* dem Namen hat er nie als `O`
gezeigt. v11 hat daneben lange, mehrwortige Lieferantennamen gelehrt (`sender_keyed`,
4–6 Wörter, Zeilenumbruch mitten im Namen) — also war eine mehrwortige Zeile am Namen
ein starkes `supplier`-Merkmal ohne Gegenbeispiel.

Die Achse füllt die Lücke. ~30 % der Vorlagen drucken einen gemischt gesetzten
Werbesatz im Briefkopf; **jedes seiner Wörter ist `O`**:

| Achse | Werte |
|---|---|
| `tagline_axis` | 30 % der Vorlagen (mit `CORPUS_FORCE_TAGLINE=1`: alle) |
| `tagline_place` | `sender` (eigene Zeile direkt unter dem Namen), `sender_owner` (eigene Zeile unter der Inhaberzeile), `owner` (**in derselben Zeile neben der Inhaberzeile** — die Form, die v11 gekostet hat), `logo` (Subzeile unter der Wortmarke, kleiner gesetzt), `sender_caps` (die VERSALFORM im Absenderblock statt oben rechts), `receipt` (im Bonkopf unter dem Namen) |
| `tagline_style` | `plain`, `italic`, `accent` (Akzentfarbe), `accent_italic`, `smallcaps`, `light` |

Der Satzbestand steht in `vocab.SLOGANS` (28 allgemeine, mit `{trade}`- und
`{year}`-Füllung) und `vocab.SLOGAN_TRADE` (34 fachspezifische, je Warengruppe, doppelt
gewichtet — `Fleisch und Wurst aus eigener Schlachtung`, `Getränke-Fachgroßhandel seit
1924`, `Obst · Gemüse · Feinkost`), zusammen **71**. `content.slogan()` zieht daraus,
setzt in ~12 % Title Case und hängt in ~18 % einen Punkt an; Trennzeichen `·`, `|`, `—`
stehen im Satz selbst.

Zwei Regeln, an denen die Achse hängt:

1. **Der Werbesatz steht immer in einem eigenen `<span>`**, und ausser bei `owner` in
   einer eigenen Zeile. Der Lieferantenname hat damit in jeder Form seine Zeile für
   sich, und der Satz kann in der Wahrheit nie in einen `supplier`-Lauf geraten. Bei
   `owner` trennen 6 mm (`.slogan.beside`) die Inhaberzeile vom Satz — und der Name
   steht ohnehin eine Zeile höher.
2. **Die Inhaberzeile bleibt häufig, wenn ein Werbesatz danebensteht** (`show_owner`
   wird bei `owner`/`sender_owner` erzwungen, sonst in 45 % zugeschaltet): sonst lernt
   das Modell „Werbesatz *statt* Inhaberzeile" statt „beides ist `O`".

Ausgenommen sind die E-Rechnungs-Viewer (`einvoice`, ~3 % der Vorlagen): die bauen die
Seite aus Formularfeldern und haben keinen Briefkopf, in den ein Werbesatz gehörte.

`CORPUS_FORCE_TAGLINE=1` erzwingt die Achse für einen ganzen Lauf — so ist der
Ergänzungskorpus `corpus-v11s` gebaut. Eine Umgebungsvariable statt eines Schalters,
weil `generate.py` die Vorlagen in Worker-Prozessen zieht; sie erbt jeder Worker von
selbst. Hat eine Vorlage weder Absenderblock noch Wortmarke, setzt der Zwang
`sender_place` auf `under` — die einzige Achse, die er ausser der eigenen anfasst.

    CORPUS_FORCE_TAGLINE=1 nice python3 generate.py --out /var/tmp/rotman/corpus-v11s \
        --count 300 --variations 10 --workers 24 --seed umsatz-v11s
