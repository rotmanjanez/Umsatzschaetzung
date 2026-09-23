# Kalkulieren

## Zuordnung prüfen

Beim Import ordnet das Programm jede Rechnungsposition einer Zutat zu, soweit es
sich sicher ist. Einige Vorschläge sind falsch oder fehlen, und genau die lohnen
den Blick. In der Prüfung auf **2. Zuordnung** wechseln, die Position wählen,
**Manuell zuordnen**, Zutat und gegebenenfalls Faktor eintragen, **Zuordnen**:

| Position | Zutat | Faktor |
|---|---|---|
| Frankenbräu Pils Fass 50 l KEG | Fassbier | 50000 |
| Frankenbräu Pils Fass 30 l KEG | Fassbier | 30000 |
| Frankenbräu Hefeweizen hell Fass 30 l | Fassbier | 30000 |
| Speisezwiebeln gelb 10 kg Sack | Zwiebeln und Lauch | 10000 |
| Schweineschnitzel natur, ausgelöst | Schweinefleisch | |
| 2022 Domina trocken 0,75 l | Rotwein | 750 |

Die Fässer hatte das Programm als Flaschenbier gelesen. Eine bestätigte
Zuordnung gilt für alle gleichlautenden Positionen; nur wo der Scan den Namen
verlesen hat („Frankenbràu …“), ist dieselbe Korrektur noch einmal nötig.

## Das erste Produkt

In der Prüfung auf **3. Sortiment** wechseln. In **Produkt hinzufügen …**
`Bier 0,5` tippen und **Bier 0,5 l vom Fass** übernehmen. Das Produkt steht
jetzt im Sortiment; als Bruttopreis `4,60` eintragen. Die Umsatzsteuer bleibt
bei 19 %.

## Der Rest per CSV

Die übrigen 20 Produkte des Gasthauses stehen mit ihren Preisen in einer
Tabelle: [sortiment-gasthaus.csv](sortiment-gasthaus.csv){ download="sortiment-gasthaus.csv" }.
Herunterladen, dann in der Kopfzeile des Sortiments auf das Importsymbol
(**CSV importieren**) klicken und die Datei auswählen.

Die Produkte werden über ihren Namen im Katalog gefunden und mit Preis und
Steuersatz ins Sortiment übernommen. Danach stehen 21 Produkte in der Liste.

Das Sortiment lässt sich auch umgekehrt als CSV exportieren, in Excel
bearbeiten und in eine andere Prüfung übernehmen, siehe
[Sortiment importieren und exportieren](../import-export.md#sortiment).

## Vorschläge

**Ins Sortiment** übernimmt einen Vorschlag, ✕ verwirft ihn.

## Ergebnis

Auf **4. Kalkulation** wechseln. Der kalkulierte Umsatz liegt bei rund
223.000 € netto, gut 27.000 € über dem erklärten.

## Ertragsregeln

Unter der Zusammenfassung stehen die Ertragsregeln der Kategorien und Zutaten,
die in der Prüfung vorkommen und für die es mehr als eine Regel gibt:
Schankverlust, Eigenverbrauch, Personalverpflegung und Freirunden. Je Zutat oder
Warengruppe lässt sich wählen, welche Regel gilt; die Kalkulation rechnet sofort
neu. Für das Beispiel bleiben die Standardregeln.
