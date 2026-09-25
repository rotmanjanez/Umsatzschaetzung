# Rechnungen korrigieren

<p class="us-steps"><a href="../start/">Prüfung</a><span class="here">1. Rechnungen</span><a href="../zuordnung/">2. Zuordnung</a><a href="../sortiment/">3. Sortiment</a><a href="../kalkulation/">4. Kalkulation</a><a href="../bericht/">5. Bericht</a></p>

<div class="us-side" markdown>
<div markdown>

Ein Scan wird von der Maschine gelesen, und dabei geht manches schief. Das
Programm rechnet deshalb jede Rechnung nach: Menge mal Einzelpreis muss den
Zeilenbetrag ergeben, die Zeilen den Nettobetrag, Netto und Steuer den
Bruttobetrag. Was nicht aufgeht oder unvollständig ist, steht mit
**Durchsicht offen** ganz oben in der Liste. Im Beispiel ist das eine
Rechnung, an der gleich mehreres schiefgegangen ist:

</div>

![Eine Rechnung mit „Durchsicht offen“](img/rechnungen-liste.png)

</div>

Was das Programm nicht zusammenbringt, ist markiert: Das Feld ist orange
hinterlegt, und auf dem Beleg ist dieselbe Stelle orange umrandet. Wer mit der
Maus auf das Feld zeigt, sieht den Grund, etwa „Einheit fehlt“.

## Die Rechnung TK25-1583

<div class="us-side" markdown>
<div markdown>

Die Rechnung des Tiefkühllieferanten mit einem Doppelklick öffnen (oder mit
dem Symbol **Öffnen** rechts in der Zeile). Sie öffnet sich in einem eigenen
Fenster: links die gelesenen Werte, rechts der Beleg. Quer über der Tabelle
liegt der Stempel „ZWEITSCHRIFT“, und oben stehen zwei Hinweise:

- „Summe der Positionen 821,80 € weicht vom Nettobetrag 842,80 € ab“: Es fehlt
  eine Zeile.
- „Netto 842,80 € zzgl. 0 % MwSt ergibt 842,80 €, Bruttobetrag ist 912,12 €“:
  Die Steuersätze fehlen.

Dazu ist der Lieferant nur halb gelesen: `FROSTW`.

</div>

![Die Rechnung TK25-1583 nach dem Import](img/korrektur-tk.png)

</div>

### Die fehlende Zeile

<div class="us-side" markdown>
<div markdown>

Auf dem Beleg stehen acht Positionen, in den gelesenen Werten nur sieben. Die
letzte liegt unter dem Stempel und ist beim Lesen verloren gegangen:

Dort steht *Thermobox Leihgebühr je Woche*, 6 Stk zu 3,50 €, zusammen 21,00 €.
Das Plus unter den Positionen (**Zeile hinzufügen**) legt eine leere Zeile an.
Mit einem Doppelklick (oder F2) wird eine Zelle bearbeitbar; eintragen:

</div>

![Der Beleg: Position 8 unter dem Stempel](img/korrektur-tk-beleg.png){ width="690" }

</div>

| Menge | Einheit | Position | Einzelpreis | Netto |
|---|---|---|---|---|
| `6`{.copy} | `Stück`{.copy} | `Thermobox Leihgebühr je Woche`{.copy} | `3,50`{.copy} | `21,00`{.copy} |

Dann oben den **Lieferanten** vervollständigen: `Frostwerk Tiefkühl-Service GmbH`{.copy}.

![Acht Zeilen, der Lieferant ist eingetragen](img/korrektur-tk-zeile.png)

Die Summe der Positionen stimmt jetzt mit dem Nettobetrag überein, der erste
Hinweis ist verschwunden.

### Die Steuersätze

<div class="us-side" markdown>
<div markdown>

Bleibt der zweite Hinweis: Netto geht auf, Brutto aber nicht. Laut Beleg sind
es 912,12 €, aus den Positionen nur 842,80 €. Die Differenz ist genau die
Umsatzsteuer: Alle Zeilen stehen auf 0 %, denn der Beleg druckt den Satz nicht
in eine eigene Spalte, sondern klein unter jeden Artikel („MwSt. 7 %“).

In der Spalte **USt** für die Lebensmittel `7`{.copy} eintragen, für Trockeneis und
Thermobox `19`{.copy}. Danach ergeben auch die Positionen 912,12 €.

</div>

![Die korrigierte Rechnung: beide Summen gleich](img/korrektur-tk-fertig.png){ width="836" }

</div>

## Bestätigen

Mit dem Häkchen oben rechts (**Bestätigen**) die Rechnung als durchgesehen
markieren; das Fenster schließt sich. In der Rechnungsliste steht sie jetzt mit
**Manuell**, und die Bilanz über der Liste zeigt keine offene Rechnung mehr:

![Keine Rechnung mehr offen](img/korrektur-liste-fertig.png)

## Für eigene Rechnungen

Was bei Scans sonst noch schiefgeht, und woran es zu erkennen ist:

| Fehler | So zeigt es das Programm | Korrektur |
|---|---|---|
| Ein Wert fehlt | Das Feld ist markiert | Wert vom Beleg abschreiben |
| Eine Ziffer ist verlesen | Menge × Einzelpreis ergibt nicht den Zeilenbetrag; alle drei Felder sind markiert, das Häkchen sperrt nach dem ersten Klick | Auf dem Beleg nachsehen, im Zweifel zurückrechnen |
| Eine Zeile fehlt (Stempel, Knick) | Die Summe der Positionen weicht vom Nettobetrag ab | Die Zeile mit dem Plus anlegen und vom Beleg abschreiben |
| Eine Zeile ist zerrissen (Umbruch) | Eine Zeile ohne Namen oder mit 0,00 €, dazu fehlende Werte | Die Werte in einer Zeile zusammenführen, das Bruchstück löschen |
| Der Lieferant fehlt oder ist abgeschnitten | Leeres oder unvollständiges Feld | Eintragen |
| Der Steuersatz ist falsch gelesen | Brutto ist markiert, **Laut Beleg** und **Aus Positionen** weichen voneinander ab | Steuersatz je Zeile eintragen |

Nur ein Zeilenbetrag, der nicht zu Menge und Einzelpreis passt, sperrt das
Bestätigen. Alles andere lässt sich bestätigen, gehört aber trotzdem korrigiert:
Die Kalkulation rechnet mit genau diesen Zeilen.

!!! geschafft "Geschafft, wenn …"
    - über der Liste `0 offen, 105 automatisch, 1 manuell` steht
    - keine Rechnung mehr **Durchsicht offen** trägt

!!! nachlesen "Zum Nachlesen"
    Wann ein Scan ohne Durchsicht übernommen wird und was das Fenster im
    Einzelnen prüft, steht unter [Durchsicht](../rechnungen.md#durchsicht).

[Weiter: 2. Zuordnung <span>Eingekaufte Waren den Zutaten zuordnen</span>](zuordnung.md){ .us-next }
