# Zuordnung prüfen

Eine Rechnung nennt die Ware so, wie der Lieferant sie nennt: „Frankenbräu Pils Fass 50 l KEG“. Die Kalkulation rechnet mit Zutaten: Fassbier. Die Zuordnung ist die Verbindung dazwischen. Beim Import hat das Programm sie für fast alle Positionen schon hergestellt. Auf diesem Reiter wird nachgeholt, was es nicht wusste.

## Die Liste

Auf **2. Zuordnung** wechseln. Links stehen alle Artikel der Prüfung, jeder einmal, auch wenn er auf 25 Rechnungen vorkommt. Darüber steht die Bilanz: `5 offen, 117 automatisch, 0 manuell`.

Die erste Spalte ist der Status. So wie die Liste zu Beginn sortiert ist, stehen die offenen oben:

- **Offen**: Das Programm war sich bei keiner Zutat sicher genug. Hier ist eine Entscheidung nötig.
- **Automatisch**: Das Programm hat zugeordnet, niemand hat es bestätigt.
- **Manuell**: Ein Mensch hat die Zuordnung gewählt oder bestätigt.

## Ein Vorschlag, der nicht passt

Die Zeile **2022 Domina trocken 0,75 l** anklicken. Rechts erscheint alles, was zu diesem Artikel bekannt ist:

- **Aus der Rechnung**: Name, Lieferant, Artikelnummer und die Gesamtmenge über alle Rechnungen, hier 158 Flaschen in 6 Positionen.
- **Aus den Regeln**: die Vorschläge des Programms mit einer Sicherheit in Prozent.
- **Faktor**: wie viel in einem Gebinde steckt.
- **Belege**: die Rechnungszeilen, aus denen der Artikel stammt, als Ausschnitt des Scans.

Das Programm hält den Wein für Schaumwein, mit 55 % ist es sich aber nicht sicher, und Rotwein steht mit 52 % gleich dahinter. Genau deshalb ist die Position offen geblieben.

Ein Domina ist ein fränkischer Rotwein: den Vorschlag **Rotwein × 750 ml** anklicken.

Der **Faktor** passt schon: Das Rezept für ein Glas Rotwein rechnet in Millilitern, die Rechnung in Flaschen, und das Programm hat 750 ml je Flasche aus dem Artikeltext gelesen. Darunter rechnet es vor: 158 Flaschen × 750 ml = 118,5 l.

Dann **Zuordnen**. Rechts oben bestätigt ein grüner Hinweis die Zuordnung, die Zeile wandert in der Liste nach unten zu den manuellen Zuordnungen.

Das Programm merkt sich die Zuordnung in der [Regel-Datenbank](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank): Kommt der Domina in einer späteren Prüfung wieder vor, ist er gleich Rotwein. Dort sammeln sich auch Rezepte und Ertragsregeln, siehe [3. Sortiment](https://docs.umsatzschaetzung.amtstools.de/dev/guide/sortiment/index.md) und [5. Bericht](https://docs.umsatzschaetzung.amtstools.de/dev/guide/bericht/index.md).

## Wenn eine Menge fehlt

Nach dem Zuordnen wählt das Programm die nächste offene Position, hier **Kaffeesahne Portionen 10 × 7,5 g 240er**. Die Zutat ist klar, **Sahne** ist schon gewählt. Aber der Faktor fehlt: Aus „Karton“ lässt sich nicht lesen, wie viel drin ist. Das Feld **Faktor** ist dann mit einem Stern markiert, und darunter steht die Frage, die zu beantworten ist.

Die Antwort steht meist im Artikeltext: 240 Portionen zu 7,5 g sind rund 1,8 l je Karton, das Rezept rechnet Sahne in Millilitern. `1800` eintragen, die Zeile darunter rechnet vor. Dann **Zuordnen**.

## Wenn alles passt

Danach ist **Frühkartoffeln festkochend 12,5 kg** dran. **Kartoffeln × 12,5 kg** ist schon gewählt, und der Faktor `12.500` stimmt. Das Programm war sich nur nicht sicher genug, um es allein zu entscheiden. Einfach **Zuordnen**.

## Was offen bleiben darf

Bleiben **Leberkäse am Stück, ungebacken** und **Petersilie glatt, Bund**. Beim Leberkäse hat der Scan die Einheit als „K8“ gelesen; ohne verlässliche Einheit gibt es keinen verlässlichen Faktor. Bei der Petersilie lässt sich aus „Bund“ nicht lesen, wie viel drin ist, und der Einkauf fällt kaum ins Gewicht.

Nicht jede Position muss zugeordnet sein: Was offen bleibt, zählt nicht zum Wareneinsatz, und die Kalkulation führt es unter **Nicht in der Umsatzschätzung** auf, damit es nicht unbemerkt verloren geht. Wo der Faktor erst geschätzt werden müsste und der Einkauf kaum ins Gewicht fällt, ist offen lassen die ehrlichere Antwort.

Am Ende steht über der Liste `2 offen`.

**Geschafft, wenn …**

- über der Liste `2 offen, 117 automatisch, 3 manuell` steht
- nur noch Leberkäse und Petersilie **Offen** sind

**Zum Nachlesen**

Woher die Vorschläge kommen, was die Sicherheit bedeutet und wie mit Pfand, Fracht und anderem umgegangen wird, das kein Wareneinsatz ist, steht unter [Zuordnung](https://docs.umsatzschaetzung.amtstools.de/dev/zuordnung/index.md).

[Weiter: 3. Sortiment Was das Gasthaus verkauft, und zu welchem Preis](https://docs.umsatzschaetzung.amtstools.de/dev/guide/sortiment/index.md)
