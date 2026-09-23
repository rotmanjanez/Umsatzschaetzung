# Rechnungen korrigieren

Ein Scan wird von der Maschine gelesen, und dabei geht manches schief. Das
Programm rechnet deshalb jeden Scan gegen sich selbst: Menge mal Einzelpreis
gegen den Zeilenbetrag, die Zeilen gegen den Nettobetrag, Netto und Steuer gegen
den Bruttobetrag. Was nicht aufgeht oder unvollständig ist, wird nicht
übernommen, sondern steht mit **Prüfung offen** in der Liste.

Im Beispieldatensatz sind das drei Rechnungen. Sie stehen ganz oben:

![Drei Rechnungen mit „Prüfung offen“](img/korrektur-liste.png)

Jede zeigt eine andere Art Fehler. Auf dieser Seite werden alle drei korrigiert,
von der einfachsten zur schwierigsten.

## Was schiefgehen kann

| Fehler | So zeigt es das Programm | Korrektur |
|---|---|---|
| Ein Wert fehlt | Das Feld ist markiert | Wert vom Beleg abschreiben |
| Eine Ziffer ist verlesen | Menge × Einzelpreis ergibt nicht den Zeilenbetrag; alle drei Felder sind markiert, **Bestätigen** ist gesperrt | Auf dem Beleg nachsehen, im Zweifel zurückrechnen |
| Eine Zeile ist zerrissen (Stempel, Knick, Umbruch) | Eine Zeile ohne Namen oder mit 0,00 €, dazu fehlende Werte | Die Werte in einer Zeile zusammenführen, die andere löschen |
| Der Lieferant fehlt | Leeres Feld | Eintragen |
| Der Steuersatz ist falsch gelesen | Brutto **Laut Beleg** und **Aus Positionen** weichen voneinander ab | Steuersatz je Zeile eintragen |

Markiert heißt: Das Feld ist orange hinterlegt, und auf dem Beleg ist dieselbe
Stelle orange umrandet. Wer mit der Maus auf das Feld zeigt, sieht den Grund,
etwa „Einheit fehlt“.

Nur ein Zeilenbetrag, der nicht zu Menge und Einzelpreis passt, sperrt das
Speichern. Alles andere lässt sich bestätigen, gehört aber trotzdem korrigiert:
Die Kalkulation rechnet mit genau diesen Zeilen.

## Eine fehlende Einheit: WS-2025-346

Die Weinrechnung mit einem Doppelklick öffnen (oder mit dem Symbol **Öffnen**
rechts in der Zeile). Sie öffnet sich in einem eigenen Fenster: links die
gelesenen Werte, rechts der Beleg.

![Die Rechnung WS-2025-346, beim Sekt fehlt die Einheit](img/korrektur-ws.png)

Alle Beträge gehen auf, nur beim Sekt ist die Einheit markiert. Ein Klick auf
die Menge der Zeile zeigt auf dem Beleg blau umrandet, woher der Wert stammt:

![Auf dem Beleg steht „17 Fl“](img/korrektur-ws-beleg.png)

Dort steht `17 Fl`. Das „Fl“ klebt so dicht an der Zahl, dass es nicht als
Einheit erkannt wurde. Mit einem Doppelklick auf die Einheit (oder F2) wird die
Zelle bearbeitbar; `Flasche` eintragen.

![Alle vier Weine in Flaschen](img/korrektur-ws-fertig.png)

Die Markierung verschwindet. **Bestätigen** speichert die Rechnung, das Fenster
kann geschlossen werden.

## Eine verlesene Ziffer: 2025/0561

Die Metzgereirechnung öffnen. Beim Rinderhackfleisch sind Menge, Einzelpreis und
Betrag markiert, und **Bestätigen** ist gesperrt:

![Die Rechnung 2025/0561, eine Zeile geht nicht auf](img/korrektur-0561.png)

Der Grund: „Menge × Einzelpreis ergibt 48,18 €, Gesamtpreis ist 48,13 €“. Einer
der drei Werte ist falsch gelesen, aber welcher? Ein Klick auf den Einzelpreis
zeigt ihn auf dem Beleg:

![Auf dem Beleg steht 8,75, nicht 8,76](img/korrektur-0561-beleg.png)

Die Schreibmaschinenschrift macht aus der 5 fast eine 6. Wo auch der Beleg
selbst keine Klarheit bringt, hilft das Zurückrechnen: 48,13 € ÷ 5,5 kg =
8,75 €. Einen Einzelpreis von 8,76 € kann es bei diesem Betrag nicht geben.

Den Einzelpreis auf `8,75` setzen. Die Zeile geht auf, **Bestätigen** ist wieder
frei:

![Die korrigierte Rechnung 2025/0561](img/korrektur-0561-fertig.png)

Bestätigen und das Fenster schließen.

## Eine zerrissene Zeile: TK25-1583

Die Rechnung des Tiefkühllieferanten öffnen. Hier fehlt schon der Lieferant, und
quer über der Tabelle liegt der Stempel „ZWEITSCHRIFT“:

![Die Rechnung TK25-1583 nach dem Import](img/korrektur-tk.png)

Der Stempel hat Position 8 zerrissen. Auf dem Beleg steht sie in einer Zeile:
*Thermobox Leihgebühr je Woche*, 6 Stk zu 3,50 €, zusammen 21,00 €. In den
Positionen ist daraus zweierlei geworden:

![Position 8 ist in zwei Zeilen zerfallen](img/korrektur-tk-zeilen.png)

- eine Zeile **ohne Namen und Einheit**, aber mit Menge, Einzelpreis und den
  21,00 €,
- eine Zeile **Thermobox Leihgebühr je Woche** mit Einheit, aber mit 0,00 €.

Markiert ist nur die fehlende Einheit, denn beide Hälften gehen für sich auf.
Eine Zeile ohne Namen oder mit 0,00 € ist fast immer die Hälfte einer
zerrissenen Zeile.

![Der Beleg: Position 8 in einer Zeile, der Stempel quer darüber](img/korrektur-tk-beleg.png)

Zum Korrigieren die Werte in der Zeile mit dem Betrag vervollständigen:

1. **Einheit**: `Stück`
2. **Position**: `Thermobox Leihgebühr je Woche`
3. Die Zeile mit 0,00 € mit dem Papierkorb rechts löschen.

Dann oben den **Lieferanten** eintragen: `Frostwerk Tiefkühl-Service GmbH`.

![Alle Zeilen vollständig, aber Brutto geht nicht auf](img/korrektur-tk-zusammengefuehrt.png)

### Die Steuersätze

Jetzt ist nichts mehr markiert, doch unten in den Summen stimmt etwas nicht:
Netto geht auf, Brutto aber nicht. Laut Beleg sind es 912,12 €, aus den
Positionen nur 842,80 €. Die Differenz ist genau die Umsatzsteuer: Alle Zeilen
stehen auf 0 %, denn der Beleg druckt den Satz nicht in eine eigene Spalte,
sondern klein unter jeden Artikel („MwSt. 7 %“).

!!! tip "Faustregel"
    Stimmt Netto, aber nicht Brutto, dann liegt es am Steuersatz.

In der Spalte **USt** für die Lebensmittel `7` eintragen, für Trockeneis und
Thermobox `19`. Danach ergeben auch die Positionen 912,12 €.

![Die korrigierte Rechnung: beide Summen gleich](img/korrektur-tk-fertig.png)

## Bestätigen

**Bestätigen** speichert die Rechnung. Der Status wechselt auf **Manuell
geprüft**:

![Die Rechnung ist manuell geprüft](img/korrektur-bestaetigt.png)

In der Rechnungsliste stehen jetzt alle drei mit **Manuell**:

![Keine Rechnung mehr offen](img/korrektur-liste-fertig.png)

Eine bestätigte Rechnung lässt sich jederzeit wieder öffnen und ändern; der
Knopf heißt dann **Änderungen speichern**.

Weiter geht es mit [der Kalkulation](kalkulation.md).
