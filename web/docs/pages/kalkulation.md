# Kalkulation

Die Kalkulation beantwortet eine Frage: Welchen Umsatz hätte der Betrieb mit
dem, was er eingekauft hat, machen müssen? Sie rechnet dafür vom Einkauf zum
Verkauf, Zutat für Zutat.

## Der Rechenweg

<ol class="us-flow">
<li><b>Einkauf</b> Die zugeordneten Rechnungspositionen des Zeitraums, umgerechnet in die Einheit der Rezepte: 116 Fässer zu 50 l sind 5.800 l Fassbier. Mit <a href="../pruefung/#bestand">Bestand</a> zählt nur, was verbraucht wurde.</li>
<li><b>Verkaufsfähige Menge</b> Davon ab gehen Schankverlust, Eigenverbrauch, Personalverpflegung und Freirunden, so wie es die gewählte Ertragsregel vorsieht.</li>
<li><b>Portionen</b> Die verkaufsfähige Menge wird auf die Produkte des Sortiments verteilt, nach deren Rezepten.</li>
<li><b>Umsatz</b> Portionen mal Preis, netto gerechnet.</li>
</ol>

Jede Änderung an Rechnungen, Zuordnung, Sortiment oder Ertragsregeln rechnet
die Kalkulation sofort neu.

## Sortiment { #sortiment }

Das Sortiment sind die Produkte, die der Betrieb führt, mit Bruttopreis und
Steuersatz, wie sie auf der Karte stehen. Die Produkte und ihre Rezepte kommen
aus den [Regeln](regeln.md#produkte), nur der Preis gehört zur Prüfung.

Nur Produkte im Sortiment bekommen Portionen. Führt das Gasthaus kein Bier
0,3 l, fließt das ganze Fassbier in die 0,5 l. Ein Produkt ohne Preis bekommt
Portionen, aber keinen Umsatz; es steht mit **Preis fehlt** in der Liste, bis
der Preis nachgetragen ist.

Die Speisekarte lässt sich auch als Tabelle übernehmen, siehe
[Sortiment importieren und exportieren](import-export.md#sortiment).

## Die Verteilung

Dieselbe Zutat steckt oft in mehreren Produkten: Fassbier in 0,3 l und 0,5 l,
Schweinefleisch in Schnitzel und Cordon Bleu. Wie der Betrieb sie tatsächlich
aufgeteilt hat, weiß niemand. Das Programm setzt dafür keine Quote, sondern
rechnet die Verteilung aus, die den Einkauf am vollständigsten aufbraucht, also
am wenigsten Ware übrig lässt. Die Verkaufspreise spielen dabei keine Rolle.

Unter **Verteilung** steht je Produkt, wie viele Portionen es bekommen hat, was
eine Portion im Einkauf kostet und welcher Umsatz daraus folgt. Ein Klick auf
ein Produkt zeigt rechts seine Rezeptur.

Die Verteilung ist eine Rechnung, keine Feststellung. Sie zeigt, was der
Einkauf hergibt, nicht, was tatsächlich über die Theke ging.

## Zusammenfassung

- **Umsätze vor und nach Betriebsprüfung**: erklärter und kalkulierter Umsatz
  je Steuersatz, mit Differenz. Der Steuersatz folgt dem Produkt, nicht der
  Einkaufsrechnung.
- **Rohgewinnaufschlag**: um wie viel der Verkaufspreis über dem Einsatz liegt,
  getrennt nach Sparte. Getränke und Speisen tragen sehr verschiedene
  Aufschläge und sind nur getrennt zu beurteilen. Mit
  [Gewerbekennzahl](pruefung.md#eckdaten) vergleicht der Bericht den Satz mit
  der Richtsatzsammlung.

## Ertragsregeln { #ertragsregeln }

Unter der Zusammenfassung stehen die Zutaten und Kategorien der Prüfung, für
die es mehr als eine [Ertragsregel](regeln.md#ertragsregeln) gibt. Ohne Wahl
gilt die Standardregel. Hat der Betrieb etwa eine alte Schankanlage mit
belegbar höherem Verlust, wird hier die passende Regel gewählt.

Freie Prozentsätze lassen sich in der Prüfung nicht eintragen. Jeder Abzug hat
einen Namen, und der Bericht nennt ihn. Fehlt eine passende Regel, wird sie
unter [Regeln](regeln.md#ertragsregeln) angelegt.

## Was nicht mitzählt

Eine Position geht nur in die Kalkulation ein, wenn sie zugeordnet ist und ihre
Zutat in einem Rezept des Sortiments vorkommt. Alles andere steht im
[Bericht](bericht.md#nicht-berucksichtigt) unter **Nicht berücksichtigte
Einkäufe**. Bei Fracht ist das richtig; steht dort eine echte Ware,
fehlt im kalkulierten Umsatz ein Stück. Pfand und Leergut zählen gar nicht zu
den Einkäufen, weil sie sich über die Zeit ausgleichen.

## Grenzen

Die Kalkulation ist so gut wie ihre Annahmen:

- Rezepte sind Durchschnittswerte. Wer großzügig einschenkt, verkauft weniger
  Gläser aus einem Fass.
- Ein Preis je Produkt kennt keine Happy Hour. Aktionen und Preisänderungen im
  Zeitraum gehören als Mischpreis in das Sortiment.
- Was nicht als Rechnung vorliegt, fehlt im Wareneinsatz.

Das Ergebnis ist Anlass für das Gespräch mit dem Steuerpflichtigen, nicht schon
die Hinzuschätzung. Seine Einwände gehören als Preis, Sortiment, Bestand oder
gewählte Ertragsregel in die Prüfung, wo sie die Rechnung sichtbar verändern.
