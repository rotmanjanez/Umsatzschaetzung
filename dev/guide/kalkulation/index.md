# Kalkulation

Die Kalkulation rechnet aus dem Einkauf, den Rezepten und den Preisen den Umsatz. Was dabei nicht über Rezept und Preis hineinfindet, steht gesondert auf der Seite **Übrige Einkäufe**. Bevor das Ergebnis zählt, wird dort aufgeräumt.

[Übung 5: Die Kalkulation Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/einkaeufe/)

## Übrige Einkäufe

Auf **4. Kalkulation** wechseln und die Seite **Übrige Einkäufe** öffnen. Sie zeigt die Einkäufe, die nicht über Rezeptur und Preis in den Umsatz eingehen, in zwei Listen mit dem Grund je Position:

- **Nicht Teil der Ermittlung des Aufschlagsatzes**: Kein Produkt des Sortiments braucht die Ware, oder der Zuordnung fehlt der Faktor. Ihr Umsatz wird über den Rohgewinnaufschlagsatz geschätzt. Meist ist das in Ordnung: Kleinigkeiten, für die sich kein eigenes Produkt lohnt, oder Waren, die ein Rezept nicht aufs Gramm genau abbildet.
- **Nicht in der Umsatzschätzung**: Einkäufe, die keinen Umsatz bringen, etwa Putzmittel oder Servietten. Auf sie wird kein Gewinn ermittelt.

Ein Klick auf eine Zeile öffnet rechts die Zuordnung der Position; eine falsche Zuordnung lässt sich dort direkt korrigieren. Pfand und Leergut stehen nicht in den Listen, sie gleichen sich über die Zeit aus.

## Das Schnitzel

In **Nicht Teil der Ermittlung des Aufschlagsatzes** steht ganz oben **Schweineschnitzel natur, ausgelöst**, mit fast 3.500 € netto. Das Programm hat es als **Schnitzel, paniert, je Stück** zugeordnet, als fertig paniertes Schnitzel. Das Gasthaus kauft das Fleisch aber roh, 427 kg im Jahr, und paniert selbst; im Sortiment steht **Schnitzel mit Pommes**, und dessen Rezept rechnet mit Schweinefleisch.

Rechts steht die Zuordnung, darunter die Vorschläge. **Schweinefleisch** trägt das Zeichen **In Rezeptur**: Ein Produkt des Sortiments braucht die Ware, hier **Schnitzel mit Pommes**. Den Vorschlag wählen und **Zuordnen**.

Die Position verschwindet aus der Liste und zählt ab jetzt zum Schnitzel.

Die automatische Zuordnung liegt meistens richtig. Wo nicht, fällt es hier als großer Betrag auf. Wie auf [2. Zuordnung](https://docs.umsatzschaetzung.amtstools.de/dev/guide/zuordnung/index.md) merkt sich das Programm die Zuordnung in der [Regel-Datenbank](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank).

## Was keinen Umsatz bringt

In derselben Liste stehen Einkäufe, die der Betrieb braucht, aber nicht verkauft. Bleiben sie dort, schätzt das Programm auch auf sie einen Umsatz über den Rohgewinnaufschlagsatz. Bei diesen Positionen auf das Kreuz am Zeilenende klicken (**… bringt in diesem Betrieb keinen Umsatz**):

1. **Servietten 1/4 Falz 33 × 33 cm weiß 1000 St.**
1. **CO2-Flasche 10 kg Füllung**
1. **Handspülmittel Konzentrat 5 l**
1. **Versandkostenpauschale**

Die Festlegung gilt für das Produkt, nicht nur für die angeklickte Position. Alle Positionen desselben Produkts wandern mit: mit den Servietten (**Verpackung und Einweg**) auch Müllbeutel und Filterpapier, mit dem Handspülmittel (**Reinigung und Hygiene**) Spülmaschinen-Reiniger, Klarspüler, Handtuchrollen und Handschuhe, mit der Versandkostenpauschale (**Dienstleistung und Fracht**) die Leihgebühr der Thermobox. Sie stehen danach unter **Nicht in der Umsatzschätzung**, und auf sie wird kein Gewinn ermittelt. Das Symbol dort nimmt die Festlegung wieder zurück.

Übrig bleiben in der oberen Liste gut 4.300 €, 6,52 % der Einkäufe: Kleinigkeiten, für die sich kein eigenes Produkt lohnt.

**Geschafft, wenn …**

- **Schweineschnitzel natur** nicht mehr in den Listen steht
- Servietten, CO2, Handspülmittel und Versandkostenpauschale unter **Nicht in der Umsatzschätzung** stehen
- neben **Nicht Teil der Ermittlung des Aufschlagsatzes** `4.350,66 € (6,52 % der Einkäufe)` steht

## Vom Einkauf zum Umsatz

Wie das Programm rechnet, zeigt ein Produkt am besten. Auf **3. Sortiment** steht **Schnitzel mit Pommes** mit dem Preis von der Karte, 15,90 € brutto.

Auf **4. Kalkulation** die Seite **Portionen** öffnen und **Schnitzel mit Pommes** anklicken. Rechts steht, wie es gerechnet ist:

- **Rezeptur je Portion**: ein **Schnitzel, ohne Beilage** und eine Portion **Pommes, Beilage**, jedes ein eigenes Produkt mit eigenem Rezept. Im Schnitzel stecken 180 g Schweinefleisch, in der Beilage Pommes frites und Pflanzenöl. Beim Schnitzel steht **begrenzt die Portionen**: Sein Schweinefleisch ist die Ware, von der am wenigsten da ist.
- **Portionen**: Das Schweinefleisch könnte auch in den Braten des Mittagstischs gehen. Die Kalkulation verteilt eine Ware, die sich mehrere Produkte teilen, so, dass möglichst viel vom Einkauf aufgeht; das Schnitzel verbraucht mit Pommes und Öl mehr davon. So entfallen alle rund 547 kg auf das Schnitzel, bei 180 g je Stück 3.039 Portionen. Weil sich viele Gerichte dieselben Waren teilen, steht darunter **Näherungsweise verteilt**: Das Programm hat die Aufteilung nicht bis aufs letzte Stück durchgerechnet.
- **Umsatz (netto)**: 3.039 Portionen zu 15,90 € brutto, ohne 19 % Umsatzsteuer, ergeben 40.601,04 €.

So rechnet die Kalkulation jedes Produkt des Sortiments.

## Ertragsregeln

Auf der Seite **Ertragsregeln** stehen die Kategorien und Produkte der Prüfung, für die es Ertragsregeln gibt, etwa für Schankverlust oder Bruch. Je Produkt oder Warengruppe lässt sich wählen, welche Regel gilt; die Kalkulation rechnet sofort neu. Für das Beispiel bleiben die Voreinstellungen.

## Rezeptur anpassen

Ein Klick auf ein Produkt unter **Portionen** öffnet rechts seine Details. Unter **Rezeptur je Portion** steht, was in eine Portion geht, und daneben, woher das Rezept kommt: **Katalog** oder **Nur diese Prüfung**.

Passt ein Rezept nicht, gibt es zwei Wege:

- **Im Katalog bearbeiten** öffnet die Regeln auf diesem Produkt. Die Änderung gilt für alle Prüfungen.
- **Für diese Prüfung anpassen** kopiert das Rezept in die Prüfung. Der Katalog bleibt, wie er ist.

Angepasst lassen sich die Zeilen bearbeiten: Produkt, Menge und Einheit, dazu fügt das Plus (**Produkt hinzufügen**) eine Zeile an. Weicht eine Zeile vom Katalog ab, steht grau daneben, was er vorsieht, etwa **Katalog: 200 g**. Ein Produkt wird in jedem Rezept in seiner Einheit gemessen, auch im angepassten. Die Kalkulation rechnet bei jeder Änderung sofort neu; unter **Portionen** steht beim Produkt **angepasst**, auf **3. Sortiment** **Rezept angepasst**, und ein Klick darauf führt hierher.

Der Bericht druckt das angepasste Rezept so, wie es gerechnet ist.

Unten stehen zwei Schaltflächen:

- **Auf Katalog zurücksetzen** verwirft die Anpassung.
- **In den Katalog übernehmen** öffnet die Regeln mit dem angepassten Rezept. Ist es dort gespeichert, entfällt die Anpassung, und das Produkt steht wieder auf **Katalog**.

Ändert sich das Katalogrezept später, übernimmt die Prüfung das nicht von selbst. Beim Produkt steht dann **Katalogrezept seit der Anpassung geändert**; ob die Anpassung bleibt oder zurückgesetzt wird, entscheidet der Prüfer.

**Faustregel**

Ist das Rezept für jeden Betrieb falsch, den Katalog berichtigen. Weicht nur dieser Betrieb belegbar ab, etwa mit größeren Portionen oder einem anderen Salat, für die Prüfung anpassen.

Für das Beispiel bleiben die Katalogrezepte.

## Ergebnis

Zum Schluss die letzte Seite, **Ergebnis**, öffnen: Umsätze vor und nach Betriebsprüfung, Rohgewinnaufschlag und Zusammenfassung. Jede Korrektur auf den anderen Seiten ist hier schon eingerechnet.

**Zum Nachlesen**

Den Rechenweg vom Einkauf über Ertragsregeln, Rezepte und Preise zum Umsatz und die Seite **Übrige Einkäufe** im Einzelnen beschreibt [Kalkulation](https://docs.umsatzschaetzung.amtstools.de/dev/kalkulation/#nicht-berucksichtigt).

[Weiter: 5. Bericht Das Ergebnis als PDF](https://docs.umsatzschaetzung.amtstools.de/dev/guide/bericht/index.md)
