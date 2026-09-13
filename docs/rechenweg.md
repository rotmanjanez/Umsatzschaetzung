# Der Rechenweg der Ausbeutekalkulation

Diese Darstellung richtet sich an Prüferinnen und Prüfer sowie an den
Steuerpflichtigen und seinen Berater. Sie beschreibt, wie das Programm aus den
Eingangsrechnungen eines Betriebs einen kalkulierten Umsatz ableitet, und in
welcher Reihenfolge die einzelnen Schritte aufeinander aufbauen. Jede Zahl im
Bericht lässt sich über die hier genannten Schritte zurückverfolgen.

## 1. Wareneinsatz

Ausgangspunkt sind die Eingangsrechnungen des Prüfungszeitraums, entweder als
E-Rechnung (XRechnung im UBL- oder CII-Format, ZUGFeRD/Factur-X) oder als Scan.
Aus jeder Rechnungszeile werden Bezeichnung, Artikelnummer bzw. GTIN, Menge,
Mengeneinheit, Einzelpreis und Zeilennettobetrag übernommen. Bei Scans ist jede
Zeile vor der Verwendung zu bestätigen; der Bericht vermerkt, wann die Rechnung
geprüft wurde.

Über die Artikelzuordnung wird jede Zeile auf eine Zutat in einer Basiseinheit
(Milliliter, Gramm, Stück) umgerechnet. Der Faktor der Zuordnung gibt an, wie
viele Basiseinheiten auf eine verrechnete Einheit entfallen — eine Kiste mit
20 Flaschen zu 0,5 l sind 10.000 ml, ein Fass 50.000 ml. Zuordnungen, die das
Programm selbst vorgeschlagen hat und die noch niemand bestätigt hat, gehen in
die Rechnung ein, werden im Bericht aber als unbestätigt ausgewiesen.

In die Kalkulation gehen nur Zeilen ein, deren Zutat in der Rezeptur eines
aktiven Produkts vorkommt. Zeilen ohne Zuordnung bleiben **ungeklärt**, Zeilen
mit Zuordnung zu einer Zutat ohne Rezeptur bleiben **unberücksichtigt**. Beide
zählen weder zum Wareneinsatz noch zur Ausbeute und erscheinen nicht im
Bericht. Das Programm weist sie stattdessen in der Prüfung gesondert aus, mit
ihrem Anteil an allen erfassten Einkäufen: Dieser Anteil zeigt, wie viel Umsatz
die Kalkulation nicht erklären kann. Verpackung, Pfand, Frachtkosten oder
Reinigungsmittel gehören dauerhaft dorthin; ein nicht erkanntes Lebensmittel
oder eine teure Zutat ohne Rezeptur dagegen ist vor der Auswertung zuzuordnen
bzw. mit einer Rezeptur zu versehen.

Ist ein **Bestand** erfasst, also gelagerte Ware zu Beginn und Ende des
Zeitraums, gilt

    verbraucht    = Anfangsbestand + Einkauf − Endbestand,
    Wareneinsatz  = Einkaufskosten × verbraucht / Einkauf.

Der Verbrauch wird mit dem durchschnittlichen Einkaufspreis des Zeitraums
bewertet; die Differenz zu den Einkaufskosten ist die Bestandsveränderung. Ohne
Bestandsangaben wird der Einkauf des Zeitraums als Verbrauch angesetzt. Das ist
bei annähernd gleichbleibenden Beständen sachgerecht und im Bericht
ausdrücklich vermerkt.

## 2. Abzüge bis zur verkaufsfähigen Menge

Nicht die gesamte eingekaufte Ware wird verkauft. Abgezogen werden

- **Schankverlust bzw. Zubereitungsverlust** — Überschäumen und Anstichverluste
  beim Fassbier, Zuschnitt-, Auftau- und Bratverluste bei Fleisch, Garverluste
  bei Beilagen,
- **Eigenverbrauch** des Betriebsinhabers und seiner Familie,
- **Personalverpflegung**,
- **Freirunden und Bruch**, also unentgeltliche Abgaben und Verderb.

Jeder Abzug ist ein Satz in Basispunkten. Die Sätze stehen in
**Ertragsregeln**, die wie Rezepturen zum gemeinsamen, standardisierten
Regelwerk gehören. Eine Ertragsregel hat einen Namen, gilt für eine Warengruppe
oder eine einzelne Zutat und trägt eine Quellenangabe, die im Bericht mit
ausgedruckt wird. Für dieselbe Warengruppe können mehrere Regeln bestehen, etwa
„Fassbier Standard“ und „Fassbier Altanlage“; eine davon ist als Standard
gekennzeichnet. Die Sätze werden addiert und in einem Schritt angewandt:

    verkaufsfähig = verbraucht × (10000 − Schwund − Eigenverbrauch
                                  − Personal − Freirunden) / 10000.

Als übliche Quelle für die Höhe dieser Sätze dient die **Richtsatzsammlung des
Bundesministeriums der Finanzen** in der für das Prüfungsjahr maßgebenden
Fassung, jeweils für die einschlägige Gewerbeklasse. Das Programm gibt keine
Werte vor; die hinterlegten Regeln sind gepflegte Erfahrungswerte.

In der Prüfung werden keine freien Prozentsätze eingetragen. Die Prüferin
**wählt** je Zutat oder Warengruppe, welche der vorhandenen Regeln gilt; ohne
Wahl gilt die Standardregel der Zutat, sonst die ihrer Warengruppe, und fehlt
auch eine Kennzeichnung als Standard, die Regel mit der niedrigsten Kennung.
Weicht der Betrieb belegbar von den Erfahrungswerten ab, ist dafür eine eigene,
benannte Regel im Regelwerk anzulegen und in der Prüfung zu wählen. Der Bericht
nennt zu jeder Zutat die angewandte Regel mit Namen, Quelle und Änderungsnummer
und kennzeichnet, ob sie in der Prüfung gewählt wurde.

## 3. Rezepturen

Jedes verkaufte Produkt hat eine Rezeptur: eine Liste von Zutaten mit der Menge
je Portion. Getränke haben in der Regel eine einzeilige Rezeptur ("Pils 0,5 l" =
500 ml Fassbier), Speisen mehrere Zeilen ("Cordon Bleu" = 200 g Schweinefleisch,
30 g Schinken, 30 g Käse, 200 g Pommes). Rezepturen gehören zum gemeinsamen
Regelwerk; Preise gehören nicht dazu (siehe Abschnitt 5).

## 4. Allokation

Dieselbe Ware kann in verschiedene Produkte fließen: Fassbier in Gläser zu 0,3 l
und 0,5 l, Schweinefleisch in Schnitzel und Cordon Bleu. Wie sich der Einkauf auf
die Produkte verteilt, ist zunächst unbekannt. Das Programm setzt hier keine
Quote, sondern rechnet die Verteilung aus.

Gesucht werden ganze Portionszahlen je Produkt, deren Zutatenbedarf die
verkaufsfähigen Mengen nicht überschreitet. Das ist ein
**mehrdimensionales Rucksackproblem** und wird exakt gelöst. Die Rechnung
beantwortet eine prüfungsnahe Frage: Welche Verteilung erklärt den Einkauf am
vollständigsten? Gewählt wird stets die **vollständigste Ausnutzung der
eingekauften Waren** — diejenige Verteilung, die möglichst wenig unverkaufte
Ware übrig lässt. Der Rest wird dabei mit dem Einkaufspreis bewertet, damit ein
übrig gebliebenes Gramm Schinken schwerer wiegt als ein Gramm Pommes. Die
Verkaufspreise spielen für die Verteilung keine Rolle; sie kommen erst im
nächsten Schritt hinzu.

An der Verteilung nehmen nur die Produkte teil, die in der Prüfung **aktiv**
sind. Ein Produkt, das der Betrieb nachweislich nicht führt, wird in der Prüfung
deaktiviert und erhält keine Portionen; das ist eine Feststellung der Prüferin
und erscheint als solche im Bericht.

In die Rechnung gehen zusätzlich die Vorgaben der Prüferin ein: festgesetzte
Portionszahlen, etwa aus dem Kassenbericht oder einer Zählung. Ihr Verbrauch wird
vor der Zuteilung von den verfügbaren Mengen abgezogen; eine Zutat, die dabei ins
Minus geriete, gilt als aufgebraucht, und der Bericht meldet die Überschreitung.

Zu jeder Teilrechnung nennt der Bericht die **bindende Zutat** — diejenige, die
zuerst aufgebraucht ist und die Portionszahl begrenzt — sowie den Rest je Zutat.
Lassen mehrere Verteilungen denselben Rest, wird nach einer festen Reihenfolge
entschieden, damit dasselbe Ergebnis reproduzierbar bleibt. Ist ein Fall so groß,
dass die exakte Rechnung unwirtschaftlich wird, rechnet das Programm auf einem
gröberen Mengenraster oder näherungsweise und kennzeichnet das Ergebnis
ausdrücklich als Näherung.

## 5. Preise und Umsatz

Die Verkaufspreise sind **Eingaben der Prüfung**, nicht Teil des gemeinsamen
Regelwerks: Jede Prüfung trägt zu jedem Produkt den Bruttopreis der Speisekarte
und den Umsatzsteuersatz ein, wie sie im Prüfungszeitraum galten. Sie können in
der Prüfung jederzeit geändert werden; die Kalkulation wird dann neu gerechnet.
Aus den Portionszahlen und diesen Preisen ergibt sich der Umsatz:

    Bruttoumsatz je Produkt = Portionen × Bruttopreis
    Nettoumsatz je Produkt  = Bruttoumsatz × 10000 / (10000 + Umsatzsteuersatz)

Ein Produkt, dem noch kein Preis zugeordnet ist, behält seine Portionen im
Rechenbaum, trägt aber nichts zum Umsatz bei; der Bericht weist es mit dem
Hinweis „Preis fehlt“ aus, damit die Lücke nicht als Ergebnis gelesen wird.

Der Umsatzsteuersatz folgt dem Produkt, nicht der Einkaufsrechnung: 7 % für die
Abgabe von Speisen zum Mitnehmen, 19 % für Getränke und für den Verzehr an Ort
und Stelle nach Maßgabe des jeweils geltenden Rechts. Der Einkauf kann also mit
7 % belastet sein, während der kalkulierte Umsatz 19 % trägt; beides wird
getrennt ausgewiesen. Sämtliche Beträge werden ganzzahlig in Cent geführt,
Mengen in Milliliter, Gramm oder Stück. Gerundet wird erst bei der Ausgabe, und
jede Stelle, an der ganzzahlig geteilt wird, steht als Formel im Bericht.

## 6. Rohgewinn und Rohgewinnaufschlag

    Rohgewinn              = kalkulierter Umsatz netto − Wareneinsatz
    Rohgewinnaufschlagsatz = Rohgewinn / Wareneinsatz

Der Rohgewinnaufschlagsatz ist die Kennzahl, die sich mit der Richtsatzsammlung
vergleichen lässt. Dabei ist zu beachten, dass der Wareneinsatz nur die
berücksichtigten Zutaten umfasst und Produkte ohne Preis Portionen ohne Umsatz
liefern. Ein hoher Anteil nicht berücksichtigter Einkäufe bedeutet, dass ein
entsprechender Teil des Umsatzes gar nicht kalkuliert wurde. Beides ist vor
einem solchen Vergleich zu klären. Der kalkulierte Umsatz ist das Ergebnis der
Verprobung, nicht bereits die Hinzuschätzung: er ist mit dem Steuerpflichtigen zu
erörtern, und die vorgetragenen Einwände gehören als gewählte Ertragsregeln,
Preise, Vorgaben oder deaktivierte Produkte in die Prüfung, wo sie die Rechnung
sichtbar verändern.

## 7. Nachvollziehbarkeit

Jede Zahl des Berichts hängt an einem Knoten mit Bezeichnung, Wert, Einheit und
Formel; die Knoten bilden einen Baum von der Endsumme bis zur einzelnen
Rechnungszeile. An jedem Knoten stehen seine Quellen: die Rechnung samt
Zeilennummer, die verwendete Regel mit ihrer Änderungsnummer, eine Angabe der
Prüfung (gewählte Ertragsregel, Preis), eine gesetzte Portionszahl oder eine
Allokationsentscheidung. Der Bericht trägt außerdem die Version und den
Prüfwert (Hash) des verwendeten Regelsatzes, sodass eine Kalkulation Jahre später
mit denselben Stammdaten wiederholt werden kann. Die Rechnung selbst ist
deterministisch: gleiche Eingaben ergeben stets dasselbe Ergebnis,
Gleichstände werden nach einer festen Reihenfolge aufgelöst.

## 8. Grenzen der Methode

Die Kalkulation ist eine Schätzung und ersetzt keine Buchführung. Ihre Grenzen
sind:

- **Unvollständiger Wareneinsatz.** Nicht verbuchte Einkäufe erscheinen nicht in
  den Rechnungen; die Kalkulation erfasst nur, was vorgelegt wurde.
- **Bestände.** Ohne Inventurwerte wird der Einkauf als Verbrauch angesetzt. Bei
  Bestandsaufbau oder -abbau verschiebt das das Ergebnis.
- **Rezepturen und Portionsgrößen.** Sie sind Durchschnittswerte. Großzügiges
  Einschenken oder abweichende Portionierung schlagen unmittelbar durch; hier
  helfen nur Feststellungen vor Ort.
- **Preise.** Sonderpreise, Aktionen, Happy Hour, Mitarbeiterrabatte und
  Preisänderungen im Zeitraum sind in einem Preis je Produkt nur als Mischpreis
  abbildbar, sonst wird der Umsatz überzeichnet.
- **Schwundsätze.** Erfahrungswerte der Richtsatzsammlung sind Bandbreiten, keine
  betriebsindividuellen Größen. Belegte Abweichungen sind als eigene Ertragsregel
  aufzunehmen und zu wählen.
- **Die Allokation ist eine Rechnung, keine Feststellung.** Dass die Ware am
  vollständigsten in eine bestimmte Verteilung passt, heißt nicht, dass so
  verkauft wurde. Vorgaben aus Kassenbericht oder Zählung und deaktivierte
  Produkte bringen die Feststellungen der Prüfung in die Rechnung ein.
- **Verderb, Diebstahl, außergewöhnliche Ereignisse.** Sie sind in den pauschalen
  Sätzen nur durchschnittlich enthalten; einzelne belegte Vorfälle gehören als
  eigene, belegte Ertragsregel in die Prüfung.

Die Kalkulation liefert damit ein begründetes, prüfbares Ergebnis. Sie ist
Anlass für die Erörterung mit dem Steuerpflichtigen und Grundlage einer
Schätzung, wenn die Buchführung nicht ordnungsgemäß ist — nicht deren Ersatz.
