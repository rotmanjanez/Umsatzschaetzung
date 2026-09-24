# Bericht

Der Bericht ist das Ergebnis der Prüfung, so wie es in die Akte kommt: der
erklärte Umsatz neben dem kalkulierten, und dahinter alles, was nötig ist, um
jede Zahl bis zur einzelnen Rechnungszeile zurückzuverfolgen.

## Einkäufe außerhalb der Rezepturen { #nicht-berucksichtigt }

Oben auf dem Reiter, nicht im PDF: was eingekauft wurde, aber nicht über
Rezeptur und Preis in den Umsatz eingeht, in zwei Listen mit Betrag und Grund.

**Nicht in der Umsatzschätzung**: Diese Positionen fehlen im Umsatz nach BP
ganz.

- **ohne Zuordnung**: Die Position ist unter [Zuordnung](zuordnung.md) noch
  offen.
- **Faktor fehlt**: Die Zutat ist klar, aber nicht, wie viel in einem Gebinde
  steckt.

**Nicht Teil der RGAS-Ermittlung**: Die Zutat kommt in keinem Produkt des
Sortiments vor. Ihr Umsatz wird über den Rohgewinnaufschlagsatz ihrer Sparte
geschätzt (Anhang D), sie verändert den Satz selbst aber nicht. Fracht und
Verpackung gehören hierher und gehören besser auf eine Zutat ohne Sparte oder
aus der Zuordnung heraus. Eine echte Ware hier heißt: Die Zuordnung ist falsch,
oder im Sortiment fehlt ein Produkt. Welche Produkte die Zutat verwenden, steht
unter [Regeln → Produkte](regeln.md#produkte); gehört eines davon zum Betrieb,
kommt es ins Sortiment.

Pfand und Leergut stehen nicht in der Liste. Über die Zeit gleichen sie sich
aus, darum zählen sie auch nicht zu den erfassten Einkäufen. Unter der Liste
steht grau, was an Pfand berechnet und an Leergut gutgeschrieben wurde.

Ein Klick auf eine Zeile öffnet rechts dieselbe Ansicht wie unter
[Zuordnung](zuordnung.md): die Position aus der Rechnung, ihre Zuordnung mit
Vorschlägen und die Belege. Eine falsche Zuordnung lässt sich dort direkt
korrigieren; die Kalkulation und die Vorschau rechnen danach neu.

Über der ersten Liste steht ihr Anteil an allen Einkäufen. So viel vom Umsatz
kann die Kalkulation gar nicht erklären; vor dem Speichern sollte er klein und
begründet sein.

## Aufbau

**Hinweise** stehen vorneweg, wenn etwas noch nicht stimmt: ein Produkt ohne
Preis, eine Zutat, die nur über ein geschätztes Stückgewicht umgerechnet wurde,
eine näherungsweise Verteilung. Ein Bericht ohne Hinweise hat keine offenen
Punkte. Ins PDF gehen die Hinweise nicht mit.

1. **Umsätze vor und nach Betriebsprüfung**: erklärter und kalkulierter Umsatz
   netto je Steuersatz, mit Differenz. Das ist das Ergebnis. Der Umsatz nach BP
   umfasst den ganzen verkaufsfähigen Wareneinsatz: die Portionen mit Preis und
   dazu, was über den Aufschlagsatz geschätzt ist.
2. **Rohgewinnaufschlag**: Einsatz, Umsatz, Rohgewinn und Aufschlagsatz je
   Sparte, ermittelt nur an Portionen mit Preis. Ist eine Gewerbekennzahl
   eingetragen, steht darunter der Rahmen der Richtsatzsammlung und ob der
   kalkulierte Satz darin liegt.

Der Anhang belegt beides:

- **A: Eingangspositionen**: jede Rechnungszeile, die eingegangen ist.
- **B: Rezepturen und Produkte**: je Produkt Rezept, Portionen, Preis und
  Aufschlagsatz. Für die Prüfung angepasste Rezepte sind als **abweichend vom
  Katalog** gekennzeichnet, mit dem Stand des Katalogrezepts; das
  Katalogrezept steht grau darunter.
- **C: Warenfluss und Ausbeute**: je Zutat vom Einkauf über die Abzüge bis zu
  dem, was verkauft oder übrig geblieben ist, dazu die angewandte Ertragsregel.
  In der Prüfung gewählte Regeln sind als solche vermerkt.
- **D: Umsatz über den Rohgewinnaufschlagsatz**: Wareneinsatz ohne Portion und
  Preis, dessen Umsatz als Einsatz zuzüglich Aufschlagsatz geschätzt ist:
  Produkte ohne Preis, nicht zugeteilte Ware je Zutat und Eingangspositionen
  ohne Rezeptur. Es gilt der Satz der Sparte; hat die Sparte keinen, der
  Gesamtsatz. Auf die Steuersätze verteilt sich der geschätzte Umsatz wie der
  kalkulierte Umsatz derselben Sparte.
- **E: Kennzahlen**: die Brücke vom Wareneinsatz zum Umsatz. Wie viel ging
  durch Schwund und Abzüge verloren, wie viel wurde nicht zugeteilt, was blieb
  als Einsatz der verkauften Portionen und was über den Aufschlagsatz
  geschätzt ist.

## Als PDF speichern

**Als PDF speichern** legt den Bericht ab, wo man will. Die Vorschau zeigt
immer den aktuellen Stand; ein gespeichertes PDF ändert sich nicht mit. Wer
danach eine Rechnung, Zuordnung oder einen Preis ändert, speichert neu.
