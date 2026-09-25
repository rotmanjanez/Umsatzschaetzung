# Kalkulation

Die Kalkulation beantwortet eine Frage: Welchen Umsatz hätte der Betrieb mit
dem, was er eingekauft hat, machen müssen? Sie rechnet dafür vom Einkauf zum
Verkauf, Zutat für Zutat.

## Der Rechenweg

<ol class="us-flow">
<li><b>Einkauf</b> Die zugeordneten Rechnungspositionen des Zeitraums, umgerechnet in die Einheit der Rezepte: 116 Fässer zu 50 l sind 5.800 l Fassbier. Mit <a href="../pruefung/#bestand">Bestand</a> zählt nur, was verbraucht wurde.</li>
<li><b>Verkaufsfähige Menge</b> Davon ab gehen Schankverlust, Eigenverbrauch, Personalverpflegung und Freirunden, so wie es die gewählte Ertragsregel vorsieht.</li>
<li><b>Portionen</b> Die verkaufsfähige Menge wird auf die Produkte des Sortiments verteilt, nach deren Rezepten.</li>
<li><b>Umsatz</b> Portionen mal Preis, netto gerechnet. Aus diesen Portionen folgt der Rohgewinnaufschlagsatz.</li>
<li><b>Geschätzter Umsatz</b> Was keine Portion mit Preis geworden ist (Produkte ohne Preis, nicht zugeteilte Ware, Zutaten ohne Rezeptur), geht mit seinem Einsatz zuzüglich dieses Satzes in den Umsatz ein.</li>
</ol>

Jede Änderung an Rechnungen, Zuordnung, Sortiment, Rezepten oder
Ertragsregeln rechnet die Kalkulation sofort neu.

Der Reiter hat vier Seiten: **Portionen**, **Ertragsregeln**, **Übrige
Einkäufe** und zuletzt das **Ergebnis**.

## Sortiment { #sortiment }

Das Sortiment sind die Produkte, die der Betrieb führt, mit Bruttopreis und
Steuersatz, wie sie auf der Karte stehen. Die Produkte und ihre Rezepte kommen
aus den [Regeln](regeln.md#produkte), nur der Preis gehört zur Prüfung. Ein
Rezept lässt sich zusätzlich für die Prüfung anpassen, siehe
[Rezeptur anpassen](#rezeptur); das Produkt steht dann mit
**Rezept angepasst** in der Liste.

Nur Produkte im Sortiment bekommen Portionen. Führt das Gasthaus kein Bier
0,3 l, fließt das ganze Fassbier in die 0,5 l. Ein Produkt ohne Preis bekommt
Portionen, aber keinen Umsatz; es steht mit **Preis fehlt** in der Liste, bis
der Preis nachgetragen ist.

Die Speisekarte lässt sich auch als Tabelle übernehmen, siehe
[Sortiment importieren und exportieren](import-export.md#sortiment).

## Portionen

Dieselbe Zutat steckt oft in mehreren Produkten: Fassbier in 0,3 l und 0,5 l,
Schweinefleisch in Schnitzel und Cordon Bleu. Wie der Betrieb sie tatsächlich
aufgeteilt hat, weiß niemand. Das Programm setzt dafür keine Quote, sondern
rechnet die Aufteilung aus, die den Einkauf am vollständigsten aufbraucht, also
am wenigsten Ware übrig lässt. Die Verkaufspreise spielen dabei keine Rolle.

Unter **Portionen** steht je Produkt, wie viele Portionen es bekommen hat und
welcher Umsatz daraus folgt. Ein Klick auf ein Produkt zeigt rechts, was eine
Portion im Einkauf kostet, und seine Rezeptur; für die Prüfung angepasste Produkte
tragen **angepasst**.

Die Aufteilung ist eine Rechnung, keine Feststellung. Sie zeigt, was der
Einkauf hergibt, nicht, was tatsächlich über die Theke ging.

## Ergebnis

Die Seite zeigt:

- **Umsätze vor und nach Betriebsprüfung**: erklärter und kalkulierter Umsatz
  je Steuersatz, mit Differenz, einschließlich des geschätzten Umsatzes. Der
  Steuersatz folgt dem Produkt, nicht der Einkaufsrechnung; geschätzter Umsatz
  verteilt sich wie der kalkulierte Umsatz seiner Sparte.
- **Rohgewinnaufschlag**: um wie viel der Verkaufspreis über dem Einsatz liegt,
  getrennt nach Sparte und nur an Portionen mit Preis ermittelt. Getränke und Speisen tragen sehr verschiedene
  Aufschläge und sind nur getrennt zu beurteilen. Mit
  [Gewerbekennzahl](pruefung.md#eckdaten) vergleicht der Bericht den Satz mit
  der Richtsatzsammlung.

## Ertragsregeln { #ertragsregeln }

Auf der Seite **Ertragsregeln** stehen die Zutaten und Kategorien der Prüfung, für
die es mehr als eine [Ertragsregel](regeln.md#ertragsregeln) gibt. Ohne Wahl
gilt die Standardregel. Hat der Betrieb etwa eine alte Schankanlage mit
belegbar höherem Verlust, wird hier die passende Regel gewählt.

Freie Prozentsätze lassen sich in der Prüfung nicht eintragen. Jeder Abzug hat
einen Namen, und der Bericht nennt ihn. Fehlt eine passende Regel, wird sie
unter [Regeln](regeln.md#ertragsregeln) angelegt.

## Rezeptur anpassen { #rezeptur }

Rechts neben den Portionen steht unter **Rezeptur je Portion** das Rezept des
gewählten Produkts, gekennzeichnet mit **Katalog** oder **Nur diese Prüfung**.

- **Im Katalog bearbeiten** öffnet das Produkt unter
  [Regeln](regeln.md#produkte). Die Änderung gilt für alle Prüfungen.
- **Für diese Prüfung anpassen** legt eine Abschrift des Rezepts in der
  Prüfung an. Der Katalog bleibt unverändert.

Die Abschrift ist das ganze Rezept, nicht nur die Abweichung. Ihre Zeilen
lassen sich bearbeiten (Zutat, Menge, Einheit, das Plus für **Zutat hinzufügen**);
abweichende Zeilen zeigen grau den Katalogwert, etwa **Katalog: 200 g**. Jede
Zutat bleibt in der Einheit, in der sie in allen Rezepten gemessen wird. Wird
das Produkt aus dem Sortiment genommen, entfällt die Anpassung mit.

**Auf Katalog zurücksetzen** verwirft die Anpassung. **In den Katalog
übernehmen** öffnet die Regeln mit dem angepassten Rezept; ist es dort
gespeichert, entfällt die Anpassung, und das Produkt steht wieder auf
**Katalog**.

Die Prüfung merkt sich, von welchem Stand des Katalogrezepts sie abgeschrieben
hat. Ändert sich das Katalogrezept danach, wird nichts übernommen; beim
Produkt steht **Katalogrezept seit der Anpassung geändert**, und
die Prüferin entscheidet, ob die Anpassung bleibt oder zurückgesetzt wird.

Anders als bei den Ertragsregeln wird hier ein freier Wert in die Prüfung
eingetragen. Nachprüfbar bleibt er durch den Bericht, der das Rezept so
führt, wie es gerechnet ist. Ist ein Rezept für jeden Betrieb falsch, gehört die Korrektur in den Katalog; weicht
nur dieser Betrieb belegbar ab, etwa mit größeren Portionen oder einem anderen
Salat, in die Prüfung.

## Übrige Einkäufe { #nicht-berucksichtigt }

Diese Seite zeigt, was eingekauft wurde, aber nicht über Rezeptur und Preis in
den Umsatz eingeht, in zwei Listen mit Betrag und Grund.

**Nicht Teil der Ermittlung des Aufschlagsatzes**: Der Umsatz dieser Positionen wird über den
Rohgewinnaufschlagsatz ihrer Sparte geschätzt (Bericht, Anhang E), sie verändern den
Satz selbst aber nicht. Entweder kommt die Zutat in keinem Produkt des
Sortiments vor, oder der Zuordnung fehlt der Faktor, sodass sich die Menge nicht
in Portionen umrechnen lässt. Fracht und Verpackung gehören besser auf eine
Zutat ohne Sparte oder aus der Zuordnung heraus. Echte Ware steht hier meist zu
Recht: Kleinigkeiten, für die sich kein eigenes Produkt lohnt, oder Zutaten, die
ein Rezept nicht aufs Gramm genau abbildet. Erst ein großer Betrag verdient
einen zweiten Blick; dann ist oft die Zuordnung falsch, oder im Sortiment fehlt
ein Produkt. Welche Produkte die Zutat verwenden, steht unter
[Regeln → Produkte](regeln.md#produkte).

**Nicht in der Umsatzschätzung**: Diese Positionen fehlen im Umsatz nach BP
ganz: Positionen, die unter [Zuordnung](zuordnung.md) noch offen sind, und
Zutaten, die ohne Umsatz festgelegt sind.

Pfand und Leergut stehen nicht in der Liste. Über die Zeit gleichen sie sich
aus, darum zählen sie auch nicht zu den erfassten Einkäufen. Unter der Liste
steht grau, was an Pfand berechnet und an Leergut gutgeschrieben wurde.

Ein Klick auf eine Zeile öffnet rechts dieselbe Ansicht wie unter
[Zuordnung](zuordnung.md): die Position aus der Rechnung, ihre Zuordnung mit
Vorschlägen und die Belege. Eine falsche Zuordnung lässt sich dort direkt
korrigieren; die Kalkulation und die Vorschau rechnen danach neu.

Ob eine Ware Umsatz bringt, hängt am Betrieb, nicht am Katalog. Das × am Ende
einer Zeile unter **Nicht Teil der Ermittlung des Aufschlagsatzes** nimmt ihre
Zutat aus der Schätzung, etwa bei Reinigungsmitteln oder Müllbeuteln. Das gilt
für alle Positionen dieser Zutat in dieser Prüfung, auch für später eingelesene
Rechnungen; sie stehen dann unter **Nicht in der Umsatzschätzung**. Das ↺ am
Ende der Zeile nimmt die Festlegung zurück.

Neben **Nicht in der Umsatzschätzung** steht ihr Anteil an allen Einkäufen. So viel vom Umsatz
kann die Kalkulation gar nicht erklären; vor dem Speichern sollte er klein und
begründet sein.

## Grenzen

Die Kalkulation ist so gut wie ihre Annahmen:

- Rezepte sind Durchschnittswerte. Wer großzügig einschenkt, verkauft weniger
  Gläser aus einem Fass. Was vor Ort festgestellt ist, gehört als
  [angepasstes Rezept](#rezeptur) in die Prüfung.
- Ein Preis je Produkt kennt keine Happy Hour. Aktionen und Preisänderungen im
  Zeitraum gehören als Mischpreis in das Sortiment.
- Was nicht als Rechnung vorliegt, fehlt im Wareneinsatz.

Das Ergebnis ist Anlass für das Gespräch mit dem Steuerpflichtigen, nicht schon
die Hinzuschätzung. Seine Einwände gehören als Preis, Sortiment, Bestand,
angepasstes Rezept oder gewählte Ertragsregel in die Prüfung, wo sie die Rechnung sichtbar verändern.
