# CSV-Dateien

Eine CSV-Datei ist eine Tabelle als reiner Text. Jede Zeile der Datei ist eine
Zeile der Tabelle, ein Trennzeichen teilt sie in Spalten. Die erste Zeile
nennt meist die Spalten. Mehr steckt nicht darin: keine Formeln, keine Farben,
keine Spaltenbreiten.

## So sieht sie aus

Die Beispieltabelle für das Sortiment, in einem Texteditor geöffnet:

```
Produkt;Bruttopreis;USt
Radler, 0,5 l;4,40 €;19 %
Weißbier, 0,5 l Flasche;4,90 €;19 %
Alkoholfreies Bier 0,33 l Flasche;3,90 €;19 %
```

Und so, wie ein Tabellenprogramm sie zeigt:

| Produkt | Bruttopreis | USt |
|---|---|---|
| Radler, 0,5 l | 4,40 € | 19 % |
| Weißbier, 0,5 l Flasche | 4,90 € | 19 % |
| Alkoholfreies Bier 0,33 l Flasche | 3,90 € | 19 % |

CSV steht für *comma-separated values*, durch Komma getrennte Werte. Im
Deutschen ist das Komma aber schon das Dezimalzeichen, darum trennt hier
meist das Semikolon. Das Programm schreibt Semikolon und liest Semikolon,
Komma und Tabulator.

## Womit öffnen und speichern

| Programm | Öffnen | Als CSV speichern |
|---|---|---|
| Microsoft Excel | Doppelklick auf die Datei | **Datei → Speichern unter**, Dateityp **CSV UTF-8 (durch Trennzeichen getrennt)** |
| LibreOffice Calc | Doppelklick, Trennzeichen **Semikolon** bestätigen | **Datei → Speichern unter**, Dateityp **Text CSV** |
| Apple Numbers | Datei auf Numbers ziehen | **Ablage → Exportieren → CSV …** |
| Google Tabellen | **Datei → Importieren** | **Datei → Herunterladen → CSV** |
| Texteditor | Editor, TextEdit | direkt speichern, Endung `.csv` |

Excel fragt beim Speichern, ob Funktionen verloren gehen dürfen: Ja, eine
CSV-Datei hat keine. Wer die Tabelle mit Formeln weiter pflegen will, speichert
zusätzlich eine Excel-Datei.

Was im Programm als CSV hinein- und herausgeht, steht unter
[Import und Export](../import-export.md).
