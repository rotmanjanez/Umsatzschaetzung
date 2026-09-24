# Zuordnung prüfen

<p class="us-steps"><a href="../start/">Prüfung</a><a href="../rechnungen/">1. Rechnungen</a><span class="here">2. Zuordnung</span><a href="../sortiment/">3. Sortiment</a><a href="../kalkulation/">4. Kalkulation</a><a href="../bericht/">5. Bericht</a></p>

Eine Rechnung nennt die Ware so, wie der Lieferant sie nennt: „Frankenbräu
Pils Fass 50 l KEG“. Die Kalkulation rechnet mit Zutaten: Fassbier. Die
Zuordnung ist die Verbindung dazwischen. Beim Import hat das Programm sie für
fast alle Positionen schon hergestellt. Auf diesem Reiter wird nachgeholt, was
es nicht wusste.

## Die Liste

Auf **2. Zuordnung** wechseln. Links stehen alle Artikel der Prüfung, jeder
einmal, auch wenn er auf 25 Rechnungen vorkommt. Die erste Spalte ist der
Status:

- **Offen**: Das Programm war sich bei keiner Zutat sicher genug. Hier ist
  eine Entscheidung nötig.
- **Automatisch**: Das Programm hat zugeordnet, niemand hat es bestätigt.
- **Manuell**: Ein Mensch hat die Zuordnung gewählt oder bestätigt.

![Der Reiter „2. Zuordnung“ nach dem Import, fünf Positionen offen](img/zuordnung-liste.png)

## Eine Position ansehen

Die Zeile **2022 Domina trocken 0,75 l** anklicken. Rechts erscheint alles,
was zu diesem Artikel bekannt ist:

![Die Position 2022 Domina mit den Vorschlägen des Programms](img/zuordnung-position.png)

- **Aus der Rechnung**: Name, Lieferant, Artikelnummer und die Gesamtmenge
  über alle Rechnungen, hier 158 Flaschen in 6 Positionen.
- **Aus den Regeln**: die Vorschläge des Programms mit einer Sicherheit in
  Prozent. Das Programm hält den Wein für Schaumwein, mit 55 % ist es sich
  aber nicht sicher, und Rotwein steht mit 51 % gleich dahinter. Genau
  deshalb ist die Position offen geblieben.
- **Faktor**: Das Rezept für ein Glas Rotwein rechnet in Millilitern, die
  Rechnung in Flaschen. Der Faktor sagt, wie viel in einem Gebinde steckt,
  hier 750 ml je Flasche. Das Programm hat ihn aus dem Artikeltext gelesen und
  rechnet darunter vor: 158 Flaschen × 750 ml = 118,5 l.
- **Belege**: die Rechnungszeilen, aus denen der Artikel stammt, als
  Ausschnitt des Scans.

Ein Domina ist ein fränkischer Rotwein. Den Vorschlag **Rotwein × 750 ml**
anklicken und **Zuordnen**. Rechts oben bestätigt ein grüner Hinweis die
Zuordnung, die Zeile wandert in der Liste nach unten zu den manuellen
Zuordnungen, und die Bilanz zeigt `4 offen`.

![Die Bestätigung nach dem Zuordnen](img/zuordnung-zugeordnet.png)

## Die übrigen offenen Positionen

Nach demselben Muster die anderen vier. Bei zweien ist die Zutat klar, das
Programm zeigt sie als **Exakter Treffer**, aber der Faktor fehlt: Aus
„Karton“ oder „Bund“ lässt sich nicht lesen, wie viel drin ist. Das Feld
**Faktor** ist dann mit einem Stern markiert, und darunter steht die Frage,
die zu beantworten ist:

![Die Kaffeesahne: Zutat klar, Faktor fehlt](img/zuordnung-faktor-fehlt.png)

Die Antwort steht meist im Artikeltext: 240 Portionen zu 7,5 g sind rund
1,8 l je Karton, das Rezept rechnet Sahne in Millilitern. `1800` eintragen,
die Zeile darunter rechnet vor:

![Der Faktor ist eingetragen](img/zuordnung-faktor.png)

| Position | Vorschlag wählen | Faktor | Dann |
|---|---|---|---|
| Frühkartoffeln festkochend 12,5 kg | **Kartoffeln × 12,5 kg** (schon gewählt) | bleibt `12.500` | **Zuordnen** |
| Kaffeesahne Portionen 10 × 7,5 g 240er | **Sahne** (schon gewählt) | `1800` eintragen | **Zuordnen** |
| Petersilie glatt, Bund | | | offen lassen |
| Pommes frites 7/7 blanchiert, 4 x | **Pommes frites** (schon gewählt) | `10000` eintragen | **Zuordnen** |

Bei den Pommes hat der Scan die Packungsangabe abgeschnitten, aus „4 × 2,5 kg“
wurde „4 x“. Was im Karton ist, zeigt der Beleg-Ausschnitt unten oder die
gleichlautende Position des anderen Tiefkühllieferanten: 4 × 2,5 kg, also
10.000 g.

Die Petersilie bleibt offen. Nicht jede Position muss zugeordnet sein: Was
offen bleibt, zählt nicht zum Wareneinsatz, und die Kalkulation führt es unter
**Nicht in der Umsatzschätzung** auf, damit es nicht unbemerkt verloren geht.
Wo der Faktor erst geschätzt werden müsste und der Einkauf kaum ins Gewicht
fällt, ist offen lassen die ehrlichere Antwort.

Mit jeder Zuordnung zählt die Bilanz herunter, bis dort `1 offen` steht.

![Nur die Petersilie ist noch offen](img/zuordnung-fertig.png)

!!! geschafft "Geschafft, wenn …"
    - über der Liste `1 offen, 149 automatisch, 4 manuell` steht
    - nur noch die Petersilie **Offen** ist

!!! nachlesen "Zum Nachlesen"
    Woher die Vorschläge kommen, was die Sicherheit bedeutet und wie mit
    Pfand, Fracht und anderem umgegangen wird, das kein Wareneinsatz ist,
    steht unter [Zuordnung](../zuordnung.md).

[Weiter: 3. Sortiment <span>Was das Gasthaus verkauft, und zu welchem Preis</span>](sortiment.md){ .us-next }
