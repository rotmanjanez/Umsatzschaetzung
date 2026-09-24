# Bericht

Der Bericht ist das Ergebnis der Prüfung, so wie es in die Akte kommt: der
erklärte Umsatz neben dem kalkulierten, und dahinter alles, was nötig ist, um
jede Zahl bis zur einzelnen Rechnungszeile zurückzuverfolgen.

## Nicht berücksichtigte Einkäufe { #nicht-berucksichtigt }

Oben auf dem Reiter, nicht im PDF: was eingekauft wurde, aber nicht in die
Kalkulation eingeht, mit Betrag und Grund.

- **ohne Zuordnung**: Die Position ist unter [Zuordnung](zuordnung.md) noch
  offen.
- **Faktor fehlt**: Die Zutat ist klar, aber nicht, wie viel in einem Gebinde
  steckt.
- **in keiner Rezeptur**: Die Zutat kommt in keinem Produkt des Sortiments vor.
  Fracht und Verpackung gehören hierher. Eine echte Ware hier heißt: Die
  Zuordnung ist falsch, oder im Sortiment fehlt ein Produkt. Welche Produkte
  die Zutat verwenden, steht unter [Regeln → Produkte](regeln.md#produkte);
  gehört eines davon zum Betrieb, kommt es ins Sortiment.

Pfand und Leergut stehen nicht in der Liste. Über die Zeit gleichen sie sich
aus, darum zählen sie auch nicht zu den erfassten Einkäufen. Unter der Liste
steht grau, was an Pfand berechnet und an Leergut gutgeschrieben wurde.

Ein Klick auf eine Zeile öffnet rechts dieselbe Ansicht wie unter
[Zuordnung](zuordnung.md): die Position aus der Rechnung, ihre Zuordnung mit
Vorschlägen und die Belege. Eine falsche Zuordnung lässt sich dort direkt
korrigieren; die Kalkulation und die Vorschau rechnen danach neu.

Die Zeile darüber nennt den Anteil an allen Einkäufen. So viel vom Umsatz kann
die Kalkulation gar nicht erklären; vor dem Speichern sollte er klein und
begründet sein.

## Aufbau

**Hinweise** stehen vorneweg, wenn etwas noch nicht stimmt: ein Produkt ohne
Preis, eine Zutat, die nur über ein geschätztes Stückgewicht umgerechnet wurde,
eine näherungsweise Verteilung. Ein Bericht ohne Hinweise hat keine offenen
Punkte.

1. **Umsätze vor und nach Betriebsprüfung**: erklärter und kalkulierter Umsatz
   netto je Steuersatz, mit Differenz. Das ist das Ergebnis.
2. **Rohgewinnaufschlag**: Einsatz, Umsatz, Rohgewinn und Aufschlagsatz je
   Sparte. Ist eine Gewerbekennzahl eingetragen, steht darunter der Rahmen der
   Richtsatzsammlung und ob der kalkulierte Satz darin liegt.

Der Anhang belegt beides:

- **A: Eingangspositionen**: jede Rechnungszeile, die eingegangen ist.
- **B: Rezepturen und Produkte**: je Produkt Rezept, Portionen, Preis und
  Aufschlagsatz.
- **C: Warenfluss und Ausbeute**: je Zutat vom Einkauf über die Abzüge bis zu
  dem, was verkauft oder übrig geblieben ist, dazu die angewandte Ertragsregel.
  In der Prüfung gewählte Regeln sind als solche vermerkt.
- **D: Kennzahlen**: die Brücke vom Wareneinsatz zum Umsatz. Wie viel ging
  durch Schwund und Abzüge verloren, wie viel wurde nicht zugeteilt, was blieb
  als Einsatz der verkauften Portionen.

## Als PDF speichern

**Als PDF speichern** legt den Bericht ab, wo man will. Die Vorschau zeigt
immer den aktuellen Stand; ein gespeichertes PDF ändert sich nicht mit. Wer
danach eine Rechnung, Zuordnung oder einen Preis ändert, speichert neu.
