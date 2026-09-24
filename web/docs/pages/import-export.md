# Import und Export

## Prüfung weitergeben

Eine Prüfung ist eine einzelne Datei, siehe [Prüfungen](pruefungen.md).
Rechnungen, Belege und Scans liegen darin, es geht nichts verloren.

Das Pfeilsymbol in der Zeile der Liste legt eine Kopie dieser Datei ab, wo Sie
wollen. **Importieren** ist der Weg zurück: die Datei wandert in den
Prüfungsordner und wird geöffnet. Ist die Prüfung dort schon vorhanden, fragt
das Programm, ob sie ersetzt werden soll.

## Regeln gehen nicht mit

Zutaten, Produkte, Zuordnungen und Ertragsregeln stehen nicht in der Prüfung,
sondern in der Regel-Datenbank des Rechners, siehe [Regeln](regeln.md). Die Prüfung
verweist nur darauf.

Auf einem Rechner mit anderen Regeln sucht das Programm deshalb für jede
Rechnungszeile wieder selbst eine Zuordnung. Findet es keine, bleibt die Zeile
ungeklärt und fällt aus der Kalkulation. Die Zahlen können also von denen des
Absenders abweichen.

## Bericht und einzelne Rechnung

Der Bericht der Prüfung geht über das Pfeilsymbol (**Als PDF speichern**)
heraus, so wie er angezeigt wird.

In einer geöffneten Rechnung schreibt das Pfeilsymbol oben rechts
(**CSV exportieren**) deren gelesene Daten als Tabelle: Kopf mit Lieferant,
Nummer, Datum und Summen, darunter jede Position mit Menge, Preisen,
Steuersatz und der Zutat, der sie zugeordnet ist. Das Symbol ist gesperrt,
bis die letzte Änderung gespeichert ist — exportiert wird, was in der Prüfung
steht.

Trennzeichen ist das Semikolon, die Zahlen stehen so darin, wie sie am
Bildschirm stehen, mit Komma und Einheit (`1.110,00 €`, `12 Keg`). Excel öffnet
die Datei ohne Nachfrage.

## Sortiment

Das Sortiment einer Prüfung lässt sich als CSV exportieren und in dieselbe oder
eine andere Prüfung importieren. Beide Knöpfe stehen in der Kopfzeile von
**3. Sortiment**, neben **Produkt hinzufügen …**.

### Export

Das rechte der beiden Symbole (**CSV exportieren**) speichert die Prüfung und schreibt alle Produkte des
Sortiments nach Namen sortiert in eine Tabelle:

```
Produkt;Bruttopreis;USt;Produkt-ID
Bier 0,5 l vom Fass;4,60 €;19 %;…
```

Ein Produkt ohne Preis hat eine leere Zelle. Verworfene Vorschläge stehen nicht
darin. Bei leerem Sortiment ist der Knopf gesperrt.

### Import

**CSV importieren** liest eine solche Datei, egal ob exportiert, in Excel
bearbeitet oder von Hand geschrieben. Die erste Zeile benennt die Spalten:

| Spalte | |
|---|---|
| Produkt | Name, wie er im Katalog steht; Groß- und Kleinschreibung zählen nicht |
| Produkt-ID | alternativ oder zusätzlich; hat Vorrang vor dem Namen |
| Bruttopreis | optional; `4,60`, `4,60 €` und `4.60` gelten gleich |
| USt | optional; ohne Angabe 19 % |

Eine der beiden ersten Spalten muss es geben, die Reihenfolge ist frei.
Trennzeichen darf Semikolon, Komma oder Tabulator sein. Dateien aus älteren
Excel-Versionen mit Umlauten werden ebenfalls gelesen.

Was die Datei mit dem Sortiment macht:

- Neue Produkte kommen hinzu.
- Produkte, die schon mit gleichem Preis und Steuersatz dastehen, bleiben, wie
  sie sind.
- Weicht Preis oder Steuersatz ab, fragt das Programm je Produkt, ob der
  bisherige Wert oder der aus der Datei gelten soll. **Abbrechen** importiert
  nichts.
- Produkte, die nicht in der Datei stehen, bleiben im Sortiment.

Namen, die der Katalog nicht kennt, werden übersprungen und danach gemeldet.
Selbst angelegte Produkte stehen in der Regel-Datenbank, siehe
[Regeln gehen nicht mit](#regeln-gehen-nicht-mit): auf einem anderen Rechner
müssen sie dort erst angelegt werden. Ist ein Preis nicht lesbar, bricht der
Import ab und nennt die Zeile.

## Rechnungen

Rechnungen kommen nicht über diesen Weg in die Prüfung, sondern über den Import
in der Prüfung selbst, siehe [Rechnungen importieren](rechnungen.md).
