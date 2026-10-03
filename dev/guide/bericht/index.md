# Bericht

Der Bericht ist das, was am Ende in die Akte kommt: der erklärte Umsatz neben dem kalkulierten, mit allem, was zur Rechnung dazugehört. Vorher gibt es auf **4. Kalkulation** noch zwei Seiten anzusehen: die **Ertragsregeln**, die festlegen, wie viel vom Einkauf als verkauft gilt, und das **Ergebnis**.

[Übung 6: Ergebnis und Bericht Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/bericht/)

## Ertragsregeln

Auf **4. Kalkulation** die Seite **Ertragsregeln** öffnen. Sie legt fest, wie viel vom Einkauf als verkauft gilt: Nicht alles, was eingekauft wird, wird verkauft. Beim Zapfen geht Bier verloren, Gemüse verdirbt, das Personal isst mit.

Die Seite führt nur die Kategorien und Produkte dieser Prüfung, für die es eine Ertragsregel gibt, **Nach Kategorie** und **Nach Produkt**. Je Zeile wird gewählt, welche Regel gilt; die Wahl für ein Produkt geht der für seine Kategorie vor. Die Kalkulation rechnet sofort neu.

Voreingestellt ist die Standardregel des Produkts oder der Kategorie, gibt es keine, **Kein Abzug**. Beim Gasthaus steht überall **Kein Abzug**. Für **Bier vom Fass** stehen drei Regeln **Schankverlust** mit 3 %, 5 % und 8 % zur Wahl, keine davon ist Standard. Hat ein Betrieb etwa eine alte Schankanlage mit belegbar höherem Verlust, wird hier die passende Regel gewählt.

Freie Prozentsätze lassen sich in der Prüfung nicht eintragen. Jeder Abzug ist eine Regel mit Namen, und der Bericht nennt ihn: nicht nur den Satz, sondern auch, warum er gilt.

Die Ertragsregeln stehen in der [Regel-Datenbank](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank), neben Produkten, Rezepten und Zuordnungen. Fehlt eine passende Regel, wird sie über das Reglersymbol (**Regeln**) unter **Ertragsregeln** angelegt, mit Namen und Abzug in Prozent, und steht dann in jeder Prüfung zur Wahl, siehe [Ertragsregeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#ertragsregeln). Die Sätze sind Erfahrungswerte, als Anhalt dient die Richtsatzsammlung des Prüfungsjahres.

Für das Beispiel bleibt es bei **Kein Abzug**.

## Das Ergebnis

Die letzte Seite, **Ergebnis**, öffnen. Jede Korrektur auf den anderen Seiten ist hier schon eingerechnet, ebenso jede spätere Änderung an Rechnungen, Zuordnung, Sortiment, Rezepten oder Ertragsregeln. Die Seite hat drei Teile:

- **Umsätze vor und nach Betriebsprüfung**: der erklärte Umsatz von 196.418,00 € neben dem kalkulierten von gut 300.000 €, mit der Differenz, je Steuersatz und in Summe. Das ist das Ergebnis der Prüfung.
- **Rohgewinnaufschlag**: Einsatz der Portionen, Umsatz, Rohgewinn und Aufschlagsatz je Sparte, hier Getränke und Speisen, zusammen rund 350 %. Ermittelt wird der Satz nur an Portionen mit Preis.
- **Zusammenfassung**: die Brücke von den erfassten Einkäufen über den Wareneinsatz zum Einsatz der verkauften Portionen und zum geschätzten Umsatz. **davon Schwund und Abzüge** steht auf `0,00 €`, weil keine Ertragsregel gewählt ist.

## Der Bericht in der Vorschau

Auf **5. Bericht** wechseln. Der Reiter zeigt den fertigen Bericht so, wie er als PDF gespeichert wird.

1. **Umsätze vor und nach Betriebsprüfung**: der erklärte Umsatz von 196.418 € neben dem kalkulierten, mit der Differenz je Steuersatz.
1. **Rohgewinnaufschlag**: Einsatz, Umsatz, Rohgewinn und Aufschlagsatz je Sparte, darunter je Produkt Einsatz und Nettopreis je Portion.
1. **Anhang**: die Eingangspositionen, die Rezepturen je Produkt, die Ausbeute mit der angewandten Ertragsregel und die Portionen je Ware, damit jede Zahl des Berichts nachvollziehbar bleibt.

Stimmt noch etwas nicht, etwa ein Produkt ohne Preis, stehen vorneweg **Hinweise**. Ein Bericht ohne Hinweise hat keine offenen Punkte. Ins PDF gehen die Hinweise nicht mit. Gibt es mehrere Berichtsvorlagen, wählt die Auswahl über der Vorschau eine andere für diese Prüfung.

## Als PDF speichern

Das Pfeilsymbol rechts oben (**Als PDF speichern**) legt den Bericht als PDF ab, wo man will.

Die Vorschau gibt immer den aktuellen Stand wieder: Wer danach eine Rechnung, eine Zuordnung oder einen Preis ändert, sieht das dort sofort. Ein schon gespeichertes PDF ändert sich nicht mit; nach einer Änderung wird es neu gespeichert.

## Die Prüfung wiederfinden

Der Pfeil links oben führt zurück zur Liste der Prüfungen. Dort steht das Gasthaus jetzt mit Zeitraum und dem Datum der letzten Änderung. Ein Klick auf die Zeile öffnet die Prüfung wieder, beim nächsten Start genauso. Bei vielen Prüfungen hilft das Feld **Filtern**.

**Geschafft, wenn …**

- auf **4. Kalkulation → Ergebnis** neben den erklärten `196.418,00 €` der kalkulierte Umsatz nach BP steht, gut 300.000 €
- das PDF gespeichert ist und sich öffnen lässt

**Zum Nachlesen**

Die Abschnitte des Berichts im Einzelnen beschreibt [Bericht](https://docs.umsatzschaetzung.amtstools.de/dev/bericht/index.md), die Ertragsregeln [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#ertragsregeln) und [Kalkulation](https://docs.umsatzschaetzung.amtstools.de/dev/kalkulation/#ertragsregeln).

Damit ist die Beispielprüfung vollständig.

[Weiter: Danach Die Prüfung weitergeben und die eigene beginnen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/danach/index.md)
