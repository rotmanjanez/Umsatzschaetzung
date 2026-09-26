# Zuordnung prüfen

Eine Rechnung nennt die Ware so, wie der Lieferant sie nennt: „Frankenbräu Pils Fass 50 l KEG“. Die Kalkulation rechnet mit Zutaten: Fassbier. Die Zuordnung ist die Verbindung dazwischen. Beim Import hat das Programm sie für fast alle Positionen schon hergestellt. Auf diesem Reiter wird nachgeholt, was es nicht wusste.

## Die Liste

Auf **2. Zuordnung** wechseln. Links stehen alle Artikel der Prüfung, jeder einmal, auch wenn er auf 25 Rechnungen vorkommt. Die erste Spalte ist der Status:

- **Offen**: Das Programm war sich bei keiner Zutat sicher genug. Hier ist eine Entscheidung nötig.
- **Automatisch**: Das Programm hat zugeordnet, niemand hat es bestätigt.
- **Manuell**: Ein Mensch hat die Zuordnung gewählt oder bestätigt.

## Eine Position ansehen

Die Zeile **2022 Domina trocken 0,75 l** anklicken. Rechts erscheint alles, was zu diesem Artikel bekannt ist:

- **Aus der Rechnung**: Name, Lieferant, Artikelnummer und die Gesamtmenge über alle Rechnungen, hier 158 Flaschen in 6 Positionen.
- **Aus den Regeln**: die Vorschläge des Programms mit einer Sicherheit in Prozent. Das Programm hält den Wein für Schaumwein, mit 55 % ist es sich aber nicht sicher, und Rotwein steht mit 52 % gleich dahinter. Genau deshalb ist die Position offen geblieben.
- **Faktor**: Das Rezept für ein Glas Rotwein rechnet in Millilitern, die Rechnung in Flaschen. Der Faktor sagt, wie viel in einem Gebinde steckt, hier 750 ml je Flasche. Das Programm hat ihn aus dem Artikeltext gelesen und rechnet darunter vor: 158 Flaschen × 750 ml = 118,5 l.
- **Belege**: die Rechnungszeilen, aus denen der Artikel stammt, als Ausschnitt des Scans.

Ein Domina ist ein fränkischer Rotwein. Den Vorschlag **Rotwein × 750 ml** anklicken und **Zuordnen**. Rechts oben bestätigt ein grüner Hinweis die Zuordnung, die Zeile wandert in der Liste nach unten zu den manuellen Zuordnungen, und die Bilanz zeigt `5 offen`.

## Die übrigen offenen Positionen

Nach demselben Muster die anderen fünf. Bei zweien ist die Zutat klar, das Programm zeigt sie als **Exakter Treffer**, aber der Faktor fehlt: Aus „Karton“ oder „Bund“ lässt sich nicht lesen, wie viel drin ist. Das Feld **Faktor** ist dann mit einem Stern markiert, und darunter steht die Frage, die zu beantworten ist:

Die Antwort steht meist im Artikeltext: 240 Portionen zu 7,5 g sind rund 1,8 l je Karton, das Rezept rechnet Sahne in Millilitern. `1800` eintragen, die Zeile darunter rechnet vor:

| Position                               | Vorschlag wählen                         | Faktor           | Dann         |
| -------------------------------------- | ---------------------------------------- | ---------------- | ------------ |
| Frühkartoffeln festkochend 12,5 kg     | **Kartoffeln × 12,5 kg** (schon gewählt) | bleibt `12.500`  | **Zuordnen** |
| Kaffeesahne Portionen 10 × 7,5 g 240er | **Sahne** (schon gewählt)                | `1800` eintragen | **Zuordnen** |
| Leberkäse am Stück, ungebacken         |                                          |                  | offen lassen |
| Putenbrustfilet frisch                 | **Putenfleisch** wählen                  |                  | **Zuordnen** |
| Petersilie glatt, Bund                 |                                          |                  | offen lassen |

Beim Leberkäse hat der Scan die Einheit als „K8“ gelesen. Ohne verlässliche Einheit gibt es keinen verlässlichen Faktor, die Position bleibt offen.

Beim Putenbrustfilet schlägt das Programm als **Exakter Treffer** **Hähnchenfleisch** vor. Das ist falsch: Pute ist kein Hähnchen, die richtige Zutat **Putenfleisch** steht mit 90 % darunter. Ein exakter Treffer verdient also trotzdem einen Blick.

Leberkäse und Petersilie bleiben offen. Nicht jede Position muss zugeordnet sein: Was offen bleibt, zählt nicht zum Wareneinsatz, und die Kalkulation führt es unter **Nicht in der Umsatzschätzung** auf, damit es nicht unbemerkt verloren geht. Wo der Faktor erst geschätzt werden müsste und der Einkauf kaum ins Gewicht fällt, ist offen lassen die ehrlichere Antwort.

Mit jeder Zuordnung zählt die Bilanz herunter, bis dort `2 offen` steht.

**Geschafft, wenn …**

- über der Liste `2 offen, 116 automatisch, 4 manuell` steht
- nur noch Leberkäse und Petersilie **Offen** sind

**Zum Nachlesen**

Woher die Vorschläge kommen, was die Sicherheit bedeutet und wie mit Pfand, Fracht und anderem umgegangen wird, das kein Wareneinsatz ist, steht unter [Zuordnung](https://docs.umsatzschaetzung.amtstools.de/dev/zuordnung/index.md).

[Weiter: 3. Sortiment Was das Gasthaus verkauft, und zu welchem Preis](https://docs.umsatzschaetzung.amtstools.de/dev/guide/sortiment/index.md)
