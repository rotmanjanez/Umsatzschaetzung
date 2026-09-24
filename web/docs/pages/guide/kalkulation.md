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

In **Nicht Teil der Ermittlung des Aufschlagsatzes** fällt **Schweineschnitzel
natur, ausgelöst** auf, mit fast 3.500 € netto. Das Programm hat es als
**Schnitzel, paniert, je Stück** zugeordnet, als fertig paniertes Schnitzel.
Das Gasthaus kauft das Fleisch aber roh und paniert selbst; im Sortiment steht
**Schnitzel mit Pommes**, und dessen Rezept rechnet mit Schweinefleisch.

Die Zeile anklicken, **Manuell zuordnen**, als Zutat `Schweinefleisch` wählen
und **Zuordnen**. Die Position verschwindet aus der Liste und zählt ab jetzt
zum Schnitzel.

Die automatische Zuordnung liegt meistens richtig, aber nicht immer. Darum am
Ende immer prüfen, ob alles zusammenpasst: Ein großer Betrag in dieser Liste
verdient einen zweiten Blick.

### Was keinen Umsatz bringt

In derselben Liste stehen Einkäufe, die der Betrieb braucht, aber nicht
verkauft. Bleiben sie dort, schätzt das Programm auch auf sie einen Umsatz
über den Rohgewinnaufschlagsatz. Bei diesen Positionen auf das Symbol am
Zeilenende klicken (**… bringt in diesem Betrieb keinen Umsatz**):

1. **Frittieröl pflanzlich 10 l**
2. **Servietten 1/4 Falz 33 x 33 cm weiß 1000 St.**
3. **CO2-Flasche 10 kg Füllung**
4. **Klarspüler 10 l**

Die Festlegung gilt für die Zutat, nicht nur für die angeklickte Position.
Alle Positionen derselben Zutat wandern mit: Mit dem Frittieröl geht alles,
was als **Pflanzenöl** zugeordnet ist, mit dem ersten Klarspüler auch der
zweite. Sie stehen danach unter **Nicht in der Umsatzschätzung**, und auf sie
wird kein Gewinn ermittelt. Das Symbol dort nimmt die Festlegung wieder
zurück.

!!! geschafft "Geschafft, wenn …"
    - **Schweineschnitzel natur** nicht mehr in den Listen steht
    - Frittieröl, Servietten, CO2 und Klarspüler unter **Nicht in der
      Umsatzschätzung** stehen

## Ertragsregeln

Auf der Seite **Ertragsregeln** stehen die Regeln der Kategorien und Zutaten,
die in der Prüfung vorkommen und für die es mehr als eine Regel gibt:
Schankverlust, Eigenverbrauch, Personalverpflegung und Freirunden. Je Zutat oder
Warengruppe lässt sich wählen, welche Regel gilt; die Kalkulation rechnet sofort
neu. Für das Beispiel bleiben die Standardregeln.

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

Der Bericht kennzeichnet das Produkt als **abweichend vom Katalog** und druckt
das Katalogrezept grau darunter.

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
