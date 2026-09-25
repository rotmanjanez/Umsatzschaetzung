# Kalkulation

<p class="us-steps"><a href="../start/">Prüfung</a><a href="../rechnungen/">1. Rechnungen</a><a href="../zuordnung/">2. Zuordnung</a><a href="../sortiment/">3. Sortiment</a><span class="here">4. Kalkulation</span><a href="../bericht/">5. Bericht</a></p>

## Übrige Einkäufe

Auf **4. Kalkulation** wechseln und die Seite **Übrige Einkäufe**
öffnen. Bevor das Ergebnis zählt, wird hier aufgeräumt.

Die Seite zeigt die Einkäufe, die nicht über Rezeptur und Preis in den
Umsatz eingehen, in zwei Listen mit dem Grund je Position:

- **Nicht Teil der Ermittlung des Aufschlagsatzes**: Die Zutat kommt in keinem Produkt des
  Sortiments vor, oder der Zuordnung fehlt der Faktor. Ihr Umsatz wird über den
  Rohgewinnaufschlagsatz geschätzt. Meist ist das in Ordnung: Kleinigkeiten,
  für die sich kein eigenes Produkt lohnt, oder Zutaten, die ein Rezept nicht
  aufs Gramm genau abbildet.
- **Nicht in der Umsatzschätzung**: Einkäufe, die keinen Umsatz bringen, etwa
  Putzmittel oder Servietten. Auf sie wird kein Gewinn ermittelt.

Ein Klick auf eine Zeile öffnet rechts die Zuordnung der Position; eine falsche
Zuordnung lässt sich dort direkt korrigieren. Pfand und Leergut stehen nicht in
den Listen, sie gleichen sich über die Zeit aus.

### Das Schnitzel

In **Nicht Teil der Ermittlung des Aufschlagsatzes** steht ganz oben
**Schweineschnitzel natur, ausgelöst**, mit fast 3.500 € netto. Das Programm
hat es als **Schnitzel, paniert, je Stück** zugeordnet, als fertig paniertes
Schnitzel. Das Gasthaus kauft das Fleisch aber roh, 427 kg im Jahr, und
paniert selbst; im Sortiment steht **Schnitzel mit Pommes**, und dessen Rezept
rechnet mit Schweinefleisch.

Die Zeile anklicken, **Manuell zuordnen**, als Zutat `Schweinefleisch`{.copy}
wählen und **Zuordnen**:

![Das Schnitzel wird Schweinefleisch zugeordnet](img/kalkulation-schnitzel.png)

Die Position verschwindet aus der Liste und zählt ab jetzt zum Schnitzel.

Die automatische Zuordnung liegt meistens richtig, aber nicht immer. Darum am
Ende immer prüfen, ob alles zusammenpasst: Ein großer Betrag in dieser Liste
verdient einen zweiten Blick.

### Was keinen Umsatz bringt

In derselben Liste stehen Einkäufe, die der Betrieb braucht, aber nicht
verkauft. Bleiben sie dort, schätzt das Programm auch auf sie einen Umsatz
über den Rohgewinnaufschlagsatz. Bei diesen Positionen auf das Kreuz am
Zeilenende klicken (**… bringt in diesem Betrieb keinen Umsatz**):

1. **Servietten 1/4 Falz 33 × 33 cm weiß 1000 St.**
2. **CO2-Flasche 10 kg Füllung**
3. **Handspülmittel Konzentrat 5 l**
4. **Versandkostenpauschale**

Die Festlegung gilt für die Zutat, nicht nur für die angeklickte Position.
Alle Positionen derselben Zutat wandern mit: mit den Servietten auch
Handtuchrollen, Müllbeutel und Handschuhe, mit dem Handspülmittel der
Spülmaschinen-Reiniger und der Klarspüler, mit der Versandkostenpauschale die
Leihgebühr der Thermobox. Sie stehen danach unter **Nicht in der
Umsatzschätzung**, und auf sie wird kein Gewinn ermittelt. Das Symbol dort
nimmt die Festlegung wieder zurück.

![Was keinen Umsatz bringt, steht unter „Nicht in der Umsatzschätzung“](img/kalkulation-ohne-umsatz.png)

Übrig bleiben in der oberen Liste knapp 3.900 €, 5,86 % der Einkäufe:
Kleinigkeiten, für die sich kein eigenes Produkt lohnt.

!!! geschafft "Geschafft, wenn …"
    - **Schweineschnitzel natur** nicht mehr in den Listen steht
    - Servietten, CO2, Handspülmittel und Versandkostenpauschale unter
      **Nicht in der Umsatzschätzung** stehen
    - neben **Nicht Teil der Ermittlung des Aufschlagsatzes** `3.909,85 €
      (5,86 % der Einkäufe)` steht

## Ertragsregeln

Auf der Seite **Ertragsregeln** stehen die Kategorien und Zutaten der Prüfung,
für die es Ertragsregeln gibt, etwa für Schankverlust oder Bruch. Je Zutat oder
Warengruppe lässt sich wählen, welche Regel gilt; die Kalkulation rechnet sofort
neu. Für das Beispiel bleiben die Voreinstellungen.

## Rezeptur anpassen

Ein Klick auf ein Produkt unter **Portionen** öffnet rechts seine Details. Unter
**Rezeptur je Portion** steht, was in eine Portion geht, und daneben, woher das
Rezept kommt: **Katalog** oder **Nur diese Prüfung**.

Passt ein Rezept nicht, gibt es zwei Wege:

- **Im Katalog bearbeiten** öffnet die Regeln auf diesem Produkt. Die Änderung
  gilt für alle Prüfungen.
- **Für diese Prüfung anpassen** kopiert das Rezept in die Prüfung. Der
  Katalog bleibt, wie er ist.

Angepasst lassen sich die Zeilen bearbeiten: Zutat, Menge und Einheit, dazu
fügt das Plus (**Zutat hinzufügen**) eine Zeile an. Weicht eine Zeile vom Katalog ab, steht grau daneben, was
er vorsieht, etwa **Katalog: 200 g**. Eine Zutat wird in jedem Rezept in
derselben Einheit gemessen, auch im angepassten. Die Kalkulation rechnet bei
jeder Änderung sofort neu; unter **Portionen** steht beim Produkt **angepasst**,
auf **3. Sortiment** **Rezept angepasst**, und ein Klick darauf führt hierher.

Der Bericht druckt das angepasste Rezept so, wie es gerechnet ist.

Unten stehen zwei Schaltflächen:

- **Auf Katalog zurücksetzen** verwirft die Anpassung.
- **In den Katalog übernehmen** öffnet die Regeln mit dem angepassten Rezept.
  Ist es dort gespeichert, entfällt die Anpassung, und das Produkt steht
  wieder auf **Katalog**.

Ändert sich das Katalogrezept später, übernimmt die Prüfung das nicht von
selbst. Beim Produkt steht dann **Katalogrezept seit der Anpassung geändert**;
ob die Anpassung bleibt oder zurückgesetzt wird, entscheidet der Prüfer.

!!! tip "Faustregel"
    Ist das Rezept für jeden Betrieb falsch, den Katalog berichtigen. Weicht nur
    dieser Betrieb belegbar ab, etwa mit größeren Portionen oder einem anderen
    Salat, für die Prüfung anpassen.

Für das Beispiel bleiben die Katalogrezepte.

## Ergebnis

Zum Schluss die letzte Seite, **Ergebnis**, öffnen: Umsätze vor und nach
Betriebsprüfung, Rohgewinnaufschlag und Zusammenfassung. Jede Korrektur auf den anderen Seiten ist hier schon
eingerechnet.

[Weiter: 5. Bericht <span>Das Ergebnis als PDF</span>](bericht.md){ .us-next }
