# Zuordnung

Eine Rechnung nennt Waren so, wie der Lieferant sie nennt: „GOUDA JUNG 48% BLOCK
ca 3kg“. Die Kalkulation rechnet mit Zutaten: Schnittkäse. Die Zuordnung ist die
Verbindung dazwischen. Sie gilt ab dann für jede weitere Rechnung desselben
Artikels, auch in späteren Prüfungen.

Zugeordnet wird je Artikel, nicht je Rechnungszeile. Steht derselbe Gouda auf
zwölf Lieferscheinen, ist er einmal zuzuordnen.

## Was beim Import geschieht

Nach dem Import sind die meisten Positionen bereits zugeordnet. Offen bleibt,
was das Programm nicht sicher genug einschätzt. Unter **Zuordnung** stehen alle
Artikel der Prüfung in der Liste links, mit ihrem Stand in der ersten Spalte:

- **Offen** — noch keine Zutat, die Position wartet auf eine Entscheidung.
- **Automatisch** — das Programm hat zugeordnet, niemand hat es bestätigt.
- **Manuell** — ein Mensch hat die Zuordnung gewählt oder bestätigt.

Die offenen Positionen stehen oben, die automatischen darunter.

Wird eine Rechnung nachträglich geändert, prüft das Programm die Zuordnung ihrer
Positionen neu: Eine Position, deren Artikelnummer oder Einheit nicht mehr zur
Regel passt, wird erneut zugeordnet; eine automatische Zuordnung gilt nur für
den Wortlaut, aus dem sie entstanden ist, und fällt bei geändertem Text ebenfalls
zurück in die Durchsicht.

Solange eine Position offen ist, zählt sie nicht zum Wareneinsatz. Die
Kalkulation weist die offenen Positionen aus, damit kein Einkauf unbemerkt
unter den Tisch fällt.

## Woher die Vorschläge kommen

Das Programm vergleicht den Text der Rechnungsposition mit dem, was über die
Zutaten bekannt ist:

- dem **Namen** der Zutat,
- ihren **Warenarten**, also dem, was unter ihr gebucht wird,
- und **jeder Zuordnung, die schon bestätigt wurde**.

Der dritte Punkt ist der wichtigste. Jede bestätigte Zuordnung ist ein Beispiel,
an dem das Programm die Sprache der Lieferanten lernt — Abkürzungen,
Gebindeangaben, Schreibweisen. Wer die ersten zwanzig Positionen eines neuen
Lieferanten von Hand zuordnet, bekommt den Rest weitgehend geschenkt.

## Die Sicherheit { #sicherheit }

Zu jedem Vorschlag steht eine Sicherheit in Prozent. Sie ist als Häufigkeit zu
lesen: Von hundert Vorschlägen mit 90 % treffen etwa neunzig zu. Sie ist kein
Maß dafür, wie ähnlich zwei Texte aussehen.

Liegt der beste Vorschlag über der Schwelle, ordnet das Programm die Position
schon beim Import zu. Darunter legt es sie zur Durchsicht vor. Ein **exakter
Treffer** — gleiche Artikelnummer beim gleichen Lieferanten, gleiche GTIN oder
ein bereits zugeordneter Name — braucht keine Sicherheit; er ist keine
Schätzung.

## Eine Position zuordnen

Position links auswählen. Rechts stehen der Rechnungstext, Artikelnummer,
Einheit und Menge, darunter die Vorschläge. Den richtigen wählen und
**Zuordnen**.

Passt keiner, **Manuell zuordnen** und die Zutat selbst wählen. Bei Gebinden ist
zusätzlich der **Faktor** anzugeben: der Inhalt eines Gebindes in der Einheit des
Rezepts, etwa 3000 bei einem 3-kg-Block zu einem Rezept in Gramm. Bei kg, l und
Stück rechnet das Programm selbst um.

Steht die Packungsgröße im Artikeltext („12 × 400 g“) oder hat die Zutat ein
**Stückgewicht**, ist der Faktor schon ausgefüllt; darunter steht, woher er
kommt, und der Preis je kg oder l, der sich daraus ergibt. Das Stückgewicht ist
ein Richtwert — rechnet ein Großmarkt einen 10-kg-Sack als „1 Stk“ ab, verrät
ihn der viel zu hohe Kilopreis. Dann den Faktor überschreiben.

Wird eine Position einzeln in Stück berechnet und das Rezept in g oder ml, ist
der eingetragene Faktor das Gewicht eines Stücks. Er wird bei der Zutat
gespeichert (**Regeln → Zutaten → Stückgewicht**) und gilt dann für jede Position
dieser Zutat in Stück. Bei Gebinden wie Kiste oder Karton bleibt er bei der
Zuordnung.

## Eine Zuordnung korrigieren

Auch eine automatisch zugeordnete Position lässt sich auswählen. Rechts steht
dann die aktuelle Zuordnung an erster Stelle, darunter die Alternativen.
**Bestätigen** macht aus der automatischen eine manuelle Zuordnung; ein anderer
Vorschlag oder **Manuell zuordnen** ersetzt sie. Die Regel für den Artikel wird
dabei korrigiert, nicht verdoppelt: Jede weitere Rechnung mit diesem Artikel
folgt der Korrektur.

Wer stichprobenartig prüfen will, beginnt bei den größten Beträgen — die
Kalkulation zeigt, welche Beträge auf welche Zutat entfallen, und dort fällt ein
Fehler am meisten ins Gewicht.

## Warenarten pflegen { #warenarten }

Warenarten stehen bei der Zutat unter **Regeln → Zutaten**, eine je Zeile.
Bei Schnittkäse etwa Gouda, Emmentaler, Edamer, Tilsiter.

Eine gute Warenart ist die Ware, nicht der Artikeltext des Lieferanten. „Gouda“
gehört dazu, „GOUDA JUNG 48% BLOCK ca 3kg“ nicht — diesen Fall deckt die
Zuordnung selbst ab, sobald sie einmal bestätigt ist. Es lohnt sich, Warenarten
dann zu ergänzen, wenn eine Warenart immer wieder zur Durchsicht vorgelegt wird,
obwohl sie eindeutig zu einer Zutat gehört.

Warenarten gelten betriebsübergreifend, weil sie zu den Regeln gehören und nicht zu
einer einzelnen Prüfung.

## Kein Wareneinsatz { #kein-wareneinsatz }

Nicht alles auf einer Eingangsrechnung ist Wareneinsatz. Pfand, Fracht,
Verpackung, Reinigungsmittel, Büromaterial: Diese Positionen sind Zutaten in der
Kategorie **Kein Wareneinsatz** zuzuordnen.

Sie sind damit erledigt und verschwinden aus der Liste der offenen Positionen,
gehen aber in keine Kalkulation ein. Im Bericht erscheinen sie als
**unberücksichtigt** — mit Betrag, damit erkennbar bleibt, was bewusst außen vor
geblieben ist, und nicht bloß vergessen wurde.

Pfand ist der häufigste Fall. Es steht als eigene Position auf fast jeder
Getränkerechnung, wird zurückgezahlt und hat mit dem Einkauf von Ware nichts zu
tun. Der Zutat **Pfand und Leergut** zugeordnet, zählt es auch nicht zu den
erfassten Einkäufen und nicht zu den unberücksichtigten; der Bericht nennt nur
die Summen von berechnetem Pfand und gutgeschriebenem Leergut.
