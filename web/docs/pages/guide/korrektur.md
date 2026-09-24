# Rechnungen korrigieren

<p class="us-steps"><a href="../start/">Prüfung</a><span class="here">1. Rechnungen</span><a href="../zuordnung/">2. Zuordnung</a><a href="../sortiment/">3. Sortiment</a><a href="../kalkulation/">4. Kalkulation</a><a href="../bericht/">5. Bericht</a></p>

Ein Scan wird von der Maschine gelesen, und dabei geht manches schief. Das
Programm rechnet deshalb jede Rechnung nach: Menge mal Einzelpreis muss den
Zeilenbetrag ergeben, die Zeilen den Nettobetrag, Netto und Steuer den
Bruttobetrag. Was nicht aufgeht oder unvollständig ist, steht mit
**Durchsicht offen** ganz oben in der Liste. Im Beispiel sind das drei
Rechnungen, jede mit einer anderen Art Fehler:

![Drei Rechnungen mit „Durchsicht offen“](img/rechnungen-liste.png)

Eine Rechnung mit einem Doppelklick in der Liste öffnen.
Was das Programm nicht zusammenbringt, ist markiert: Das Feld ist orange
hinterlegt, und auf dem Beleg ist dieselbe Stelle orange umrandet. Wer mit der
Maus auf das Feld zeigt, sieht den Grund, etwa „Einheit fehlt“.

## Eine fehlende Einheit: WS-2025-346

Die Weinrechnung mit einem Doppelklick öffnen (oder mit dem Symbol **Öffnen**
rechts in der Zeile). Sie öffnet sich in einem eigenen Fenster: links die
gelesenen Werte, rechts der Beleg.

![Die Rechnung WS-2025-346, beim Sekt fehlt die Einheit](img/korrektur-ws.png)

Alle Beträge gehen auf, nur beim Sekt ist die Einheit markiert. Ein Klick auf
die Menge der Zeile zeigt auf dem Beleg blau umrandet, woher der Wert stammt:

![Auf dem Beleg steht „17 Fl“](img/korrektur-ws-beleg.png)

Dort steht `17 Fl`. Mit einem Doppelklick auf die Einheit (oder F2) wird die
Zelle bearbeitbar; `Flasche` eintragen.

![Alle vier Weine in Flaschen](img/korrektur-ws-fertig.png)

Die Markierung verschwindet. Das Häkchen oben rechts (**Bestätigen**) markiert
die Rechnung als durchgesehen und schließt das Fenster.

## Eine verlesene Ziffer: 2025/0561

Die Metzgereirechnung öffnen. Beim Rinderhackfleisch sind Menge, Einzelpreis und
Betrag markiert, und das Häkchen ist gesperrt:

![Die Rechnung 2025/0561, eine Zeile geht nicht auf](img/korrektur-0561.png)

Der Grund: „Menge × Einzelpreis ergibt 48,18 €, Gesamtpreis ist 48,13 €“. Einer
der drei Werte ist falsch gelesen, aber welcher? Ein Klick auf den Einzelpreis
zeigt ihn auf dem Beleg:

![Auf dem Beleg steht 8,75, nicht 8,76](img/korrektur-0561-beleg.png)

Den falsch gelesenen Einzelpreis `8,76` auf `8,75` korrigieren. Die Zeile geht
auf, das Häkchen ist wieder frei.

Mit dem Häkchen bestätigen.

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

Bei den beiden Hälften ist nur die fehlende Einheit markiert, denn jede geht für
sich auf. Eine Zeile ohne Namen oder mit 0,00 € ist fast immer die Hälfte einer
zerrissenen Zeile. Auf dem Beleg ist Position 8 eine einzige Zeile, der Stempel
läuft quer darüber:

![Der Beleg: Position 8 in einer Zeile, der Stempel quer darüber](img/korrektur-tk-beleg.png)

Zum Korrigieren die Werte in der Zeile mit dem Betrag vervollständigen:

1. **Einheit**: `Stück`
2. **Position**: `Thermobox Leihgebühr je Woche`, am einfachsten aus der Zeile
   mit 0,00 € kopiert
3. Die Zeile mit 0,00 € mit dem Papierkorb rechts löschen.

!!! windows "Unter Windows"
    Die markierte Zelle kopiert ++ctrl+c++, ++ctrl+v++ fügt sie in eine andere
    markierte Zelle ein.

!!! macos "Auf dem Mac"
    Die markierte Zelle kopiert ++cmd+c++, ++cmd+v++ fügt sie in eine andere
    markierte Zelle ein.

Dann oben den **Lieferanten** eintragen: `Frostwerk Tiefkühl-Service GmbH`.

![Der Lieferant ist eingetragen](img/korrektur-tk-zusammengefuehrt.png)

### Die Steuersätze

Markiert sind jetzt nur noch die Steuersätze und die Summen: Netto geht auf,
Brutto aber nicht. Laut Beleg sind es 912,12 €, aus den Positionen nur
842,80 €. Die Differenz ist genau die Umsatzsteuer: Alle Zeilen stehen auf
0 %, denn der Beleg druckt den Satz nicht in eine eigene Spalte, sondern klein
unter jeden Artikel („MwSt. 7 %“).

In der Spalte **USt** für die Lebensmittel `7` eintragen, für Trockeneis und
Thermobox `19`. Danach ergeben auch die Positionen 912,12 €.

![Die korrigierte Rechnung: beide Summen gleich](img/korrektur-tk-fertig.png)

## Bestätigen

Mit dem Häkchen bestätigen. In der Rechnungsliste stehen die drei jetzt mit
**Manuell**, und die Bilanz
über der Liste zeigt keine offene Rechnung mehr:

![Keine Rechnung mehr offen](img/korrektur-liste-fertig.png)

## Für eigene Rechnungen

Die drei Fälle decken das meiste ab, was bei Scans schiefgeht:

| Fehler | So zeigt es das Programm | Korrektur |
|---|---|---|
| Ein Wert fehlt | Das Feld ist markiert | Wert vom Beleg abschreiben |
| Eine Ziffer ist verlesen | Menge × Einzelpreis ergibt nicht den Zeilenbetrag; alle drei Felder sind markiert, das Häkchen ist gesperrt | Auf dem Beleg nachsehen, im Zweifel zurückrechnen |
| Eine Zeile ist zerrissen (Stempel, Knick, Umbruch) | Eine Zeile ohne Namen oder mit 0,00 €, dazu fehlende Werte | Die Werte in einer Zeile zusammenführen, die andere löschen |
| Der Lieferant fehlt | Leeres Feld | Eintragen |
| Der Steuersatz ist falsch gelesen | Brutto ist markiert, **Laut Beleg** und **Aus Positionen** weichen voneinander ab | Steuersatz je Zeile eintragen |

Nur ein Zeilenbetrag, der nicht zu Menge und Einzelpreis passt, sperrt das
Bestätigen. Alles andere lässt sich bestätigen, gehört aber trotzdem korrigiert:
Die Kalkulation rechnet mit genau diesen Zeilen.

!!! geschafft "Geschafft, wenn …"
    - über der Liste `0 offen, 103 automatisch, 3 manuell` steht
    - keine Rechnung mehr **Durchsicht offen** trägt

!!! nachlesen "Zum Nachlesen"
    Wann ein Scan ohne Durchsicht übernommen wird und was das Fenster im
    Einzelnen prüft, steht unter [Durchsicht](../rechnungen.md#durchsicht).

[Weiter: 2. Zuordnung <span>Eingekaufte Waren den Zutaten zuordnen</span>](zuordnung.md){ .us-next }
