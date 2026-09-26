# Berichtsvorlagen

Diese Seite richtet sich an die IT und an alle, die den Bericht an eine eigene Form anpassen. Für die Prüfung selbst ist sie nicht nötig.

Der [Bericht](https://docs.umsatzschaetzung.amtstools.de/dev/bericht/index.md) ist eine HTML-Seite, die aus einer Vorlage entsteht. Vorlagen gehören zu den [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/index.md) und liegen mit ihnen in der Regel-Datenbank, eine gemeinsame Regel-Datenbank teilt also auch die Vorlagen. Gepflegt werden sie unter **Regeln → Vorlagen**.

- Genau eine Vorlage ist der **Standard**. Eine Prüfung nimmt ihn, solange sie im Bericht keine andere wählt. Ist die gewählte Vorlage gelöscht, gilt wieder der Standard.
- Den Standard selbst kann man nicht löschen, erst muss eine andere Vorlage Standard werden.
- Das Programm bringt die Vorlage **Bericht** mit. Solange sie niemand ändert, folgt sie jedem Update. Eine geänderte Vorlage bleibt dagegen, wie sie ist. Wer anpassen will, legt am besten mit **Neu** eine Kopie an und macht diese zum Standard. Dann bleibt das Original als Vergleich erhalten.

Die Vorlage wird beim Speichern geprüft. Ein Fehler im Aufbau, etwa ein fehlendes `{% endif %}`, verhindert das Speichern. Ein Feld, das es nicht gibt, fällt erst beim Erzeugen des Berichts auf.

## Was eine Vorlage darf

Die Seite läuft in einer abgeschotteten Ansicht. Erlaubt sind HTML, Stile im Dokument (`<style>`, `style="…"`) sowie Bilder und Schriften als `data:`-URL. Skripte laufen nicht, externe Dateien werden nicht geladen und Formulare nicht abgeschickt. Eine Vorlage kann also nichts nachladen und nichts versenden.

Kopf- und Fußzeilen des PDF (Prüfung, Zeitraum, Steuernummer, Seitenzahl) setzt das Programm selbst. Sie stehen nicht in der Vorlage.

## Aufbau

Eine Vorlage ist HTML mit drei Arten von Platzhaltern:

```
{{ case.label }}                                   Wert einsetzen
{{ report.totals.revenueNet | cents }}             Wert mit Filter
{{ l.quantity | quantity:l.unitCode }}             Filter mit Argument

{% for inv in included %} … {% endfor %}           Liste oder Objekt durchlaufen
{% if rahmen %} … {% else %} … {% endif %}         wenn vorhanden und nicht leer
{% if not case.inventory %} … {% endif %}          wenn fehlend oder leer
{% if e.basis == "unbestimmt" %} … {% endif %}     Vergleich mit Text
```

- Ein Pfad geht mit `.` in ein Objekt und mit `[…]` über einen anderen Wert: `rules.ingredients[l.ingredientId].name`. Eine Zahl in `[…]` wählt einen Eintrag einer Liste, gezählt ab 0: `revenue[0].vat`.
- Eingesetzte Werte werden für HTML maskiert. HTML aus den Daten ist deshalb nicht möglich.
- Steht ein `{% … %}` allein auf seiner Zeile, verschwindet die Zeile im Ergebnis.
- Leer, `0`, `false`, eine leere Liste und ein fehlender Wert gelten in `if` als nicht vorhanden.

Beträge sind ganze **Cent**, Sätze **Basispunkte** (1900 = 19 %), Rezeptmengen ganze g, ml oder Stück. Rechnungsmengen stehen in Tausendsteln, Einzelpreise in Millionstel Euro. Ohne Filter erscheint die rohe Zahl.

## Daten

Die folgenden Abschnitte werden aus dem Programmcode erzeugt und stimmen daher mit der installierten Version überein. Als JSON Schema: [vorlagen-schema.json](https://docs.umsatzschaetzung.amtstools.de/dev/vorlagen-schema.json).

## Filter

| Filter                           | Wirkung                                                         |
| -------------------------------- | --------------------------------------------------------------- |
| `bp`                             | Basispunkte als Prozent                                         |
| `cents`                          | Cent als Euro mit zwei Nachkommastellen                         |
| `date`                           | Datum JJJJ-MM-TT als TT.MM.JJJJ                                 |
| `day`                            | Zeitpunkt als Tag TT.MM.JJJJ                                    |
| `group`                          | Ganzzahl mit Tausenderpunkten                                   |
| `micro`                          | Millionstel als Dezimalzahl                                     |
| `milli`                          | Tausendstel als Dezimalzahl                                     |
| `portions`                       | Anzahl mit „Portion“ oder „Portionen“                           |
| `price:preisbasis:einheitencode` | Einzelpreis je Preisbasis und Einheit                           |
| `qty:einheit`                    | Menge in der Rezepteinheit (g, ml, Stück), ab 1000 in kg oder l |
| `quantity:einheitencode`         | Rechnungsmenge mit Einheitencode (UN/ECE Rec 20)                |
| `sparte`                         | Sparte als Wort: Getränke, Speisen, Handelsware, Übrige         |
| `unitname`                       | Einheitencode als Bezeichnung                                   |

### Wurzel

| Feld           | Typ                                               |                                                                                                   |
| -------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `case`         | [Case](#m-case)                                   | Die Prüfung, wie sie gespeichert ist, mit allen Rechnungen und Zeilen.                            |
| `rules`        | [RuleSet](#m-ruleset)                             | Die Regeln, mit denen gerechnet wurde; Rezepturen so, wie diese Prüfung sie anpasst.              |
| `report`       | [Report](#m-report)                               | Das Ergebnis der Kalkulation.                                                                     |
| `included`     | Liste von [Included](#m-included)                 | Rechnungen mit den Zeilen, die in den Wareneinsatz eingehen, nach Datum und Nummer.               |
| `revenue`      | Liste von [VatRow](#m-vatrow)                     | Umsatz vor und nach Prüfung je Steuersatz; die letzte Zeile (total) ist die Summe.                |
| `invoiceCount` | Zahl                                              | Anzahl der Rechnungen in included.                                                                |
| `includedNet`  | Zahl                                              | Wareneinsatz samt Bestandsveränderung, in Cent.                                                   |
| `excluded`     | Zahl                                              | Einkauf ohne Zuordnung oder ohne Umsatz, in Cent.                                                 |
| `estimated`    | [EstimateGroups](#m-estimategroups)               | Über den Aufschlagsatz geschätzter Umsatz, getrennt nach Herkunft.                                |
| `calculation`  | Liste von [CalculationGroup](#m-calculationgroup) | Kalkulation je Produkt: eine Gruppe je Sparte, außerhalb der Gastronomie eine Gruppe ohne Sparte. |
| `rahmen`       | [Rahmen](#m-rahmen)?                              | Rahmensatz der Richtsatzsammlung für die Gewerbekennzahl, falls eindeutig.                        |
| `lage`         | `"unter"` \| `"im"` \| `"über"`?                  | Lage des kalkulierten Aufschlagsatzes zum Rahmen.                                                 |
| `anyYields`    | ja/nein                                           | Ob irgendeine Zutat eine Ertragsregel trägt.                                                      |
| `gewerbe`      | [Gewerbezweig](#m-gewerbezweig)?                  | Die Gewerbekennzahl der Prüfung aus den Regeln.                                                   |
| `richtsatz`    | [Richtsatzbasis](#m-richtsatzbasis)?              | Die verwendete Richtsatzsammlung mit der ganzen Gewerbeklasse der Prüfung und den Pauschbeträgen. |
| `suppliers`    | Liste von [SupplierSum](#m-suppliersum)           | Je Lieferant Anzahl und Summe aller Rechnungen der Prüfung, in Cent.                              |
| `marks`        | [PageMarks](#m-pagemarks)                         | Kopf- und Fußzeilen, die ins PDF gestempelt werden.                                               |
| `template`     | [TemplateInfo](#m-templateinfo)                   | Die Vorlage, aus der dieser Bericht entsteht.                                                     |
| `periodDays`   | Zahl                                              | Tage im Prüfungszeitraum, beide Enden eingeschlossen.                                             |
| `generatedAt`  | Zeitpunkt                                         | Zeitpunkt, zu dem der Bericht erzeugt wurde.                                                      |
| `appVersion`   | Text                                              | Version des Programms.                                                                            |

### Case

| Feld          | Typ                                             |
| ------------- | ----------------------------------------------- |
| `id`          | Text                                            |
| `label`       | Text                                            |
| `periodFrom`  | Datum                                           |
| `periodTo`    | Datum                                           |
| `taxpayer`    | [Taxpayer](#m-taxpayer)                         |
| `declared`    | Liste von [DeclaredRevenue](#m-declaredrevenue) |
| `inventory`   | Liste von [InventoryEntry](#m-inventoryentry)   |
| `invoices`    | Liste von [Invoice](#m-invoice)                 |
| `products`    | Liste von [CaseProduct](#m-caseproduct)         |
| `yields`      | Liste von [YieldChoice](#m-yieldchoice)         |
| `pinned`      | Liste von [PinnedPortions](#m-pinnedportions)   |
| `noRevenue`   | Liste von Text                                  |
| `mappings`    | ID → [ArticleMapping](#m-articlemapping)        |
| `templateId`  | Text?                                           |
| `mappedStore` | Text?                                           |
| `mappedAt`    | Zahl                                            |
| `createdAt`   | Zeitpunkt                                       |
| `updatedAt`   | Zeitpunkt                                       |

### RuleSet

| Feld            | Typ                                      |
| --------------- | ---------------------------------------- |
| `store`         | Text?                                    |
| `version`       | Zahl                                     |
| `categories`    | ID → [Category](#m-category)             |
| `ingredients`   | ID → [Ingredient](#m-ingredient)         |
| `mappings`      | ID → [ArticleMapping](#m-articlemapping) |
| `products`      | ID → [Product](#m-product)               |
| `yieldRules`    | ID → [YieldRule](#m-yieldrule)           |
| `gewerbezweige` | ID → [Gewerbezweig](#m-gewerbezweig)     |
| `templates`     | ID → [ReportTemplate](#m-reporttemplate) |

### Report

| Feld          | Typ                                         |
| ------------- | ------------------------------------------- |
| `caseId`      | Text                                        |
| `computedAt`  | Zeitpunkt                                   |
| `totals`      | [Totals](#m-totals)                         |
| `ingredients` | Liste von [IngredientRow](#m-ingredientrow) |
| `products`    | Liste von [ProductRow](#m-productrow)       |
| `markups`     | Liste von [MarkupRow](#m-markuprow)         |
| `unmapped`    | Liste von [UnmappedLine](#m-unmappedline)   |
| `unused`      | Liste von [UnusedLine](#m-unusedline)       |
| `deposits`    | Liste von [UnusedLine](#m-unusedline)       |
| `noRevenue`   | Liste von [UnusedLine](#m-unusedline)       |
| `estimated`   | Liste von [EstimateRow](#m-estimaterow)     |
| `allocations` | Liste von [Allocation](#m-allocation)       |
| `warnings`    | Liste von [Flag](#m-flag)                   |

### Included

| Feld       | Typ                                     |
| ---------- | --------------------------------------- |
| `number`   | Text                                    |
| `fileName` | Text                                    |
| `date`     | Datum?                                  |
| `lines`    | Liste von [InvoiceLine](#m-invoiceline) |

### VatRow

| Feld         | Typ     |
| ------------ | ------- |
| `vat`        | Zahl    |
| `declared`   | Zahl    |
| `calculated` | Zahl    |
| `total`      | ja/nein |
| `difference` | Zahl    |

### EstimateGroups

| Feld       | Typ                               |
| ---------- | --------------------------------- |
| `products` | [EstimateGroup](#m-estimategroup) |
| `leftover` | [EstimateGroup](#m-estimategroup) |
| `lines`    | [EstimateGroup](#m-estimategroup) |

### CalculationGroup

| Feld     | Typ                                                                |
| -------- | ------------------------------------------------------------------ |
| `sparte` | `"unbestimmt"` \| `"getraenke"` \| `"speisen"` \| `"handelsware"`? |
| `rows`   | Liste von [ProductRow](#m-productrow)                              |
| `cost`   | Zahl                                                               |
| `markup` | Zahl                                                               |

### Rahmen

| Feld        | Typ             |
| ----------- | --------------- |
| `jahr`      | Zahl            |
| `klasse`    | Text            |
| `stufe`     | Text?           |
| `aufschlag` | [Satz](#m-satz) |
| `von`       | Zahl            |
| `bis`       | Zahl            |

### Gewerbezweig

| Feld       | Typ             |
| ---------- | --------------- |
| `id`       | Text            |
| `kennzahl` | Text            |
| `name`     | Text            |
| `meta`     | [Meta](#m-meta) |

### Richtsatzbasis

| Feld            | Typ                                       |
| --------------- | ----------------------------------------- |
| `jahr`          | Zahl                                      |
| `quelle`        | Text                                      |
| `importedAt`    | Zeitpunkt                                 |
| `klasse`        | [Klasse](#m-klasse)?                      |
| `pauschbeträge` | Liste von [Pauschbetrag](#m-pauschbetrag) |

### SupplierSum

| Feld       | Typ  |
| ---------- | ---- |
| `name`     | Text |
| `invoices` | Zahl |
| `net`      | Zahl |
| `gross`    | Zahl |

### PageMarks

| Feld       | Typ            |
| ---------- | -------------- |
| `topLeft`  | Text           |
| `topRight` | Text           |
| `footer`   | Liste von Text |

### TemplateInfo

| Feld   | Typ  |
| ------ | ---- |
| `id`   | Text |
| `name` | Text |

### Taxpayer

| Feld        | Typ  |
| ----------- | ---- |
| `name`      | Text |
| `taxNumber` | Text |
| `pabNumber` | Text |
| `gewerbe`   | Text |

### DeclaredRevenue

| Feld  | Typ  |
| ----- | ---- |
| `vat` | Zahl |
| `net` | Zahl |

### InventoryEntry

| Feld           | Typ  |
| -------------- | ---- |
| `ingredientId` | Text |
| `opening`      | Zahl |
| `closing`      | Zahl |
| `unit`         | Text |

### Invoice

| Feld           | Typ                                           |
| -------------- | --------------------------------------------- |
| `id`           | Text                                          |
| `source`       | `"ubl"` \| `"cii"` \| `"zugferd"` \| `"scan"` |
| `fileName`     | Text                                          |
| `supplierName` | Text                                          |
| `number`       | Text                                          |
| `date`         | Datum?                                        |
| `currency`     | Text                                          |
| `netTotal`     | Zahl                                          |
| `grossTotal`   | Zahl                                          |
| `statedNet`    | Zahl?                                         |
| `statedGross`  | Zahl?                                         |
| `lines`        | Liste von [InvoiceLine](#m-invoiceline)       |
| `verification` | [Verification](#m-verification)?              |

### CaseProduct

| Feld          | Typ                                    |
| ------------- | -------------------------------------- |
| `productId`   | Text                                   |
| `grossPrice`  | Zahl                                   |
| `vat`         | Zahl                                   |
| `recipe`      | Liste von [RecipeLine](#m-recipeline)? |
| `recipeBasis` | Zahl                                   |

### YieldChoice

| Feld           | Typ   |
| -------------- | ----- |
| `ingredientId` | Text? |
| `categoryId`   | Text? |
| `yieldRuleId`  | Text? |

### PinnedPortions

| Feld        | Typ  |
| ----------- | ---- |
| `productId` | Text |
| `portions`  | Zahl |
| `reason`    | Text |

### ArticleMapping

| Feld                | Typ             |
| ------------------- | --------------- |
| `id`                | Text            |
| `supplierName`      | Text?           |
| `supplierArticleId` | Text?           |
| `gtin`              | Text?           |
| `name`              | Text?           |
| `observed`          | Text?           |
| `unitCode`          | Text?           |
| `ingredientId`      | Text            |
| `factor`            | Zahl?           |
| `confirmed`         | ja/nein         |
| `meta`              | [Meta](#m-meta) |

### Category

| Feld      | Typ                                                               |
| --------- | ----------------------------------------------------------------- |
| `id`      | Text                                                              |
| `name`    | Text                                                              |
| `gewerbe` | Liste von Text                                                    |
| `gebinde` | Liste von Text                                                    |
| `sparte`  | `"unbestimmt"` \| `"getraenke"` \| `"speisen"` \| `"handelsware"` |
| `meta`    | [Meta](#m-meta)                                                   |

### Ingredient

| Feld         | Typ                |
| ------------ | ------------------ |
| `id`         | Text               |
| `name`       | Text               |
| `categoryId` | Text               |
| `aliases`    | Liste von Text     |
| `piece`      | [Piece](#m-piece)? |
| `meta`       | [Meta](#m-meta)    |

### Product

| Feld     | Typ                                   |
| -------- | ------------------------------------- |
| `id`     | Text                                  |
| `name`   | Text                                  |
| `recipe` | Liste von [RecipeLine](#m-recipeline) |
| `meta`   | [Meta](#m-meta)                       |

### YieldRule

| Feld           | Typ             |
| -------------- | --------------- |
| `id`           | Text            |
| `name`         | Text            |
| `categoryId`   | Text?           |
| `ingredientId` | Text?           |
| `deduction`    | Zahl            |
| `default`      | ja/nein         |
| `meta`         | [Meta](#m-meta) |

### ReportTemplate

| Feld      | Typ             |
| --------- | --------------- |
| `id`      | Text            |
| `name`    | Text            |
| `source`  | Text            |
| `default` | ja/nein         |
| `meta`    | [Meta](#m-meta) |

### Totals

| Feld                   | Typ  |
| ---------------------- | ---- |
| `calculatedRevenueNet` | Zahl |
| `purchases`            | Zahl |
| `costOfGoods`          | Zahl |
| `stockChange`          | Zahl |
| `sellableCost`         | Zahl |
| `allocatedCost`        | Zahl |
| `shrinkageCost`        | Zahl |
| `unallocatedCost`      | Zahl |
| `pricedCost`           | Zahl |
| `pricedPortions`       | Zahl |
| `grossProfit`          | Zahl |
| `markup`               | Zahl |
| `estimatedCost`        | Zahl |
| `estimatedRevenueNet`  | Zahl |
| `revenueNet`           | Zahl |
| `portions`             | Zahl |
| `unmappedCost`         | Zahl |
| `unusedCost`           | Zahl |
| `noRevenueCost`        | Zahl |
| `depositCharged`       | Zahl |
| `depositRefunded`      | Zahl |
| `excludedShare`        | Zahl |

### IngredientRow

| Feld           | Typ                                                 |
| -------------- | --------------------------------------------------- |
| `ingredientId` | Text                                                |
| `name`         | Text                                                |
| `unit`         | `"ml"` \| `"g"` \| `"piece"`                        |
| `purchases`    | Liste von [Purchase](#m-purchase)                   |
| `opening`      | Zahl                                                |
| `closing`      | Zahl                                                |
| `bought`       | Zahl                                                |
| `cost`         | Zahl                                                |
| `used`         | Zahl                                                |
| `usedCost`     | Zahl                                                |
| `yield`        | [YieldRule](#m-yieldrule)?                          |
| `yieldRate`    | Zahl                                                |
| `sellable`     | Zahl                                                |
| `products`     | Liste von [IngredientProduct](#m-ingredientproduct) |
| `leftover`     | Zahl                                                |
| `binding`      | ja/nein                                             |

### ProductRow

| Feld             | Typ                                                               |
| ---------------- | ----------------------------------------------------------------- |
| `productId`      | Text                                                              |
| `name`           | Text                                                              |
| `binding`        | Liste von Text                                                    |
| `pinReason`      | Text                                                              |
| `sparte`         | `"unbestimmt"` \| `"getraenke"` \| `"speisen"` \| `"handelsware"` |
| `portions`       | Zahl                                                              |
| `costPerPortion` | Zahl                                                              |
| `costOfGoods`    | Zahl                                                              |
| `pinned`         | ja/nein                                                           |
| `grossPrice`     | Zahl                                                              |
| `vat`            | Zahl                                                              |
| `unitNet`        | Zahl                                                              |
| `revenueNet`     | Zahl                                                              |
| `markup`         | Zahl                                                              |
| `priceMissing`   | ja/nein                                                           |

### MarkupRow

| Feld          | Typ                                                               |
| ------------- | ----------------------------------------------------------------- |
| `sparte`      | `"unbestimmt"` \| `"getraenke"` \| `"speisen"` \| `"handelsware"` |
| `portions`    | Zahl                                                              |
| `costOfGoods` | Zahl                                                              |
| `revenueNet`  | Zahl                                                              |
| `grossProfit` | Zahl                                                              |
| `markup`      | Zahl                                                              |

### UnmappedLine

| Feld        | Typ  |
| ----------- | ---- |
| `invoiceId` | Text |
| `lineNo`    | Zahl |
| `name`      | Text |
| `lineNet`   | Zahl |

### UnusedLine

| Feld           | Typ  |
| -------------- | ---- |
| `invoiceId`    | Text |
| `lineNo`       | Zahl |
| `name`         | Text |
| `lineNet`      | Zahl |
| `ingredientId` | Text |

### EstimateRow

| Feld         | Typ                                                               |
| ------------ | ----------------------------------------------------------------- |
| `source`     | `"preis"` \| `"rest"` \| `"rezeptur"`                             |
| `name`       | Text                                                              |
| `invoice`    | Text                                                              |
| `date`       | Datum?                                                            |
| `unit`       | `"ml"` \| `"g"` \| `"piece"`                                      |
| `qty`        | Zahl                                                              |
| `basis`      | `"unbestimmt"` \| `"getraenke"` \| `"speisen"` \| `"handelsware"` |
| `cost`       | Zahl                                                              |
| `markup`     | Zahl                                                              |
| `revenueNet` | Zahl                                                              |

### Allocation

| Feld          | Typ                                             |
| ------------- | ----------------------------------------------- |
| `component`   | Zahl                                            |
| `products`    | Liste von [ProductPortions](#m-productportions) |
| `binding`     | Liste von Text                                  |
| `leftover`    | Liste von [Leftover](#m-leftover)               |
| `grid`        | Zahl                                            |
| `states`      | Zahl                                            |
| `approximate` | ja/nein                                         |

### Flag

| Feld      | Typ                                                                                                                                                                                                                                                                                                             |
| --------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `code`    | Text                                                                                                                                                                                                                                                                                                            |
| `message` | Text                                                                                                                                                                                                                                                                                                            |
| `lineNo`  | Zahl                                                                                                                                                                                                                                                                                                            |
| `field`   | `"quantity"` \| `"unit"` \| `"name"` \| `"articleId"` \| `"unitPrice"` \| `"lineNet"` \| `"vat"` \| `"invoiceNumber"` \| `"invoiceDate"` \| `"supplier"` \| `"netTotal"` \| `"grossTotal"` \| `"numberLabel"` \| `"dateLabel"` \| `"netLabel"` \| `"grossLabel"` \| `"vatLabel"` \| `"otherLabel"` \| `"cell"`? |

### InvoiceLine

| Feld              | Typ   |
| ----------------- | ----- |
| `no`              | Zahl  |
| `name`            | Text  |
| `sellerArticleId` | Text? |
| `gtin`            | Text? |
| `quantity`        | Zahl  |
| `unitCode`        | Text  |
| `unitPrice`       | Zahl  |
| `priceBaseQty`    | Zahl  |
| `lineNet`         | Zahl  |
| `vat`             | Zahl  |
| `mappingId`       | Text? |

### EstimateGroup

| Feld         | Typ                                     |
| ------------ | --------------------------------------- |
| `rows`       | Liste von [EstimateRow](#m-estimaterow) |
| `cost`       | Zahl                                    |
| `revenueNet` | Zahl                                    |

### Satz

| Feld     | Typ   |
| -------- | ----- |
| `von`    | Zahl? |
| `bis`    | Zahl? |
| `mittel` | Zahl  |

### Meta

| Feld        | Typ       |
| ----------- | --------- |
| `validFrom` | Datum?    |
| `validTo`   | Datum?    |
| `changedAt` | Zeitpunkt |
| `changedBy` | Text?     |
| `rev`       | Zahl      |

### Klasse

| Feld         | Typ                             |
| ------------ | ------------------------------- |
| `name`       | Text                            |
| `zusatz`     | Text?                           |
| `kennzahlen` | Liste von Text                  |
| `staffeln`   | Liste von [Staffel](#m-staffel) |
| `bemerkung`  | Text?                           |
| `seite`      | Zahl                            |

### Pauschbetrag

| Feld           | Typ   |
| -------------- | ----- |
| `von`          | Datum |
| `bis`          | Datum |
| `gewerbezweig` | Text  |
| `ermäßigt`     | Zahl  |
| `voll`         | Zahl  |
| `gesamt`       | Zahl  |

### Verification

| Feld   | Typ       |
| ------ | --------- |
| `at`   | Zeitpunkt |
| `auto` | ja/nein   |

### RecipeLine

| Feld           | Typ   |
| -------------- | ----- |
| `ingredientId` | Text  |
| `productId`    | Text? |
| `amount`       | Zahl  |
| `unit`         | Text  |

### Piece

| Feld     | Typ                          |
| -------- | ---------------------------- |
| `amount` | Zahl                         |
| `unit`   | `"ml"` \| `"g"` \| `"piece"` |

### Purchase

| Feld        | Typ                                              |
| ----------- | ------------------------------------------------ |
| `invoiceId` | Text                                             |
| `invoice`   | Text                                             |
| `lineNo`    | Zahl                                             |
| `name`      | Text                                             |
| `quantity`  | Zahl                                             |
| `unitCode`  | Text                                             |
| `unit`      | `"ml"` \| `"g"` \| `"piece"`                     |
| `factor`    | Zahl                                             |
| `per`       | Zahl                                             |
| `source`    | `"table"` \| `"manual"` \| `"pack"` \| `"piece"` |
| `qty`       | Zahl                                             |
| `net`       | Zahl                                             |

### IngredientProduct

| Feld         | Typ  |
| ------------ | ---- |
| `productId`  | Text |
| `name`       | Text |
| `portions`   | Zahl |
| `perPortion` | Zahl |
| `qty`        | Zahl |

### ProductPortions

| Feld        | Typ     |
| ----------- | ------- |
| `productId` | Text    |
| `portions`  | Zahl    |
| `pinned`    | ja/nein |

### Leftover

| Feld           | Typ  |
| -------------- | ---- |
| `ingredientId` | Text |
| `qty`          | Zahl |

### Staffel

| Feld    | Typ                    |
| ------- | ---------------------- |
| `stufe` | Text?                  |
| `von`   | Zahl?                  |
| `bis`   | Zahl?                  |
| `sätze` | [Sätze](#m-s%C3%A4tze) |

### Sätze

| Feld             | Typ              |
| ---------------- | ---------------- |
| `aufschlag`      | [Satz](#m-satz)? |
| `rohgewinnI`     | [Satz](#m-satz)? |
| `rohgewinnII`    | [Satz](#m-satz)? |
| `halbreingewinn` | [Satz](#m-satz)? |
| `reingewinn`     | [Satz](#m-satz)? |
