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
sondern im Regelspeicher des Rechners, siehe [Regeln](regeln.md). Die Prüfung
verweist nur darauf.

Auf einem Rechner mit anderen Regeln sucht das Programm deshalb für jede
Rechnungszeile wieder selbst eine Zuordnung. Findet es keine, bleibt die Zeile
ungeklärt und fällt aus der Kalkulation. Die Zahlen können also von denen des
Absenders abweichen.

## Bericht und einzelne Rechnung

Der Bericht der Prüfung geht über **Als PDF speichern** heraus, so wie er
angezeigt wird.

In einer geöffneten Rechnung schreibt **CSV exportieren** deren gelesene Daten
als Tabelle: Kopf mit Lieferant, Nummer, Datum und Summen, darunter jede
Position mit Menge, Preisen, Steuersatz und der Zutat, der sie zugeordnet ist.
Der Knopf ist gesperrt, solange Änderungen offen sind — gespeichert wird, was
in der Prüfung steht.

Trennzeichen ist das Semikolon, die Zahlen stehen so darin, wie sie am
Bildschirm stehen, mit Komma und Einheit (`1.110,00 €`, `12 Keg`). Excel öffnet
die Datei ohne Nachfrage.

## Rechnungen

Rechnungen kommen nicht über diesen Weg in die Prüfung, sondern über den Import
in der Prüfung selbst, siehe [Rechnungen importieren](rechnungen.md).
