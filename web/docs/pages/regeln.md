# Regeln

Regeln sind das Wissen, das nicht zu einem Betrieb gehört, sondern zum Handwerk:
dass ein Bier 0,5 l aus 500 ml Fassbier besteht, dass beim Fassbier ein Teil
als Schankverlust verloren geht, dass „Frankenbräu Pils Fass 50 l KEG“ Fassbier
ist. Das Programm bringt einen Grundstock mit, jede Prüfung ergänzt ihn.

Die Regeln liegen auf dem Rechner, nicht in der Prüfung. Eine Änderung wirkt
deshalb auf jede Prüfung, die danach gerechnet wird. Geöffnet werden sie über
das Reglersymbol (**Regeln**) in der [Liste der Prüfungen](pruefungen.md). Nur ein Rezept kann eine
Prüfung zusätzlich für sich anpassen, siehe
[Rezeptur anpassen](kalkulation.md#rezeptur).

Änderungen speichern sich von selbst, sobald eine kurze Pause im Tippen
eintritt. Ein neues Produkt entsteht, sobald sein Name eingetragen ist und das
Feld verlassen wird. Fehlt beim Schließen noch eine Angabe oder ist die
Regel-Datenbank nicht erreichbar, bleibt das Fenster offen und fragt nach.

| Regel | Gepflegt unter |
|---|---|
| Produkte und Rezepte | **Regeln → Produkte** |
| Ertragsregeln | **Regeln → Ertragsregeln** |
| Gewerbekennzahlen | **Regeln → Gewerbe** |
| Berichtsvorlagen | **Regeln → Vorlagen**, siehe [Verwaltete Installation](verwaltung.md#berichtsvorlagen) |
| Zuordnungen | in der Prüfung unter [Zuordnung](zuordnung.md) |

## Produkte { #produkte }

Alles, womit gerechnet wird, ist ein Produkt: Fassbier, Schweinefleisch und
Kartoffeln ebenso wie Bier 0,5 l, Pommes und Schnitzel mit Pommes. Ein Produkt
kann eingekauft werden, auf Lager liegen, nach seinem Rezept hergestellt und
verkauft werden, auch ohne Rezept. Was davon im Betrieb geschieht, steht nicht
im Produkt, sondern ergibt sich aus der Prüfung: aus den Rechnungen, dem
[Bestand](pruefung.md#bestand) und dem [Sortiment](kalkulation.md#sortiment).

Ein Beispiel: Pommes frites kommen meist tiefgekühlt vom Großhändler, manche
Wirte schneiden sie aber selbst aus Kartoffeln. Die Kalkulation kann beides,
sobald das Produkt Pommes frites ein Rezept aus Kartoffeln hat: Sie rechnet dann
mit den eingekauften Pommes und mit Pommes aus den eingekauften Kartoffeln, so
dass vom ganzen Einkauf möglichst wenig übrig bleibt. Verkauft wird die Portion Pommes,
ein eigenes Produkt mit 180 g Pommes frites und 100 ml Pflanzenöl im Rezept,
das Altöl aus der Fritteuse eingerechnet.

Produkte sind bewusst grob gefasst. Hähnchenschenkel und ganze Hähnchen sind
beide Hähnchenfleisch; den Unterschied trägt das Rezept in der Menge.

- **Einheit**: worin das Produkt gezählt wird, in Gramm, Milliliter oder Stück,
  in Einkäufen, im Bestand und in Rezepten gleich. Pommes in Stück und Pommes in
  Gramm wären zwei Produkte. Die Einheit ist fest, solange Rezepte oder
  Zuordnungen das Produkt verwenden.
- **Kategorie**: die Warengruppe, etwa Bier oder Fleisch. Ertragsregeln gelten
  meist für eine ganze Kategorie, und die Kategorie bestimmt die Sparte im
  Rohgewinnaufschlag.
- **Warenarten**: was unter diesem Produkt gebucht wird, eine je Zeile. Sie
  helfen der Zuordnung bei Artikeln, die sie noch nicht kennt, siehe
  [Warenarten pflegen](zuordnung.md#warenarten).
- **Stückgewicht**: ein Richtwert für Waren, die in Stück berechnet, aber in
  Gramm verarbeitet werden, etwa 1 Gurke ≈ 400 g. Leer lassen, wenn es keinen
  sinnvollen Wert gibt.

## Rezepte { #rezepte }

Das **Rezept** sagt, aus welchen anderen Produkten eines gemacht wird und wie
viel davon. Bei Getränken ist das meist eine Zeile, bei Speisen mehrere: Ein
Cordon Bleu sind 200 g Schweinefleisch, 30 g Schinken, 40 g Käse, 30 g
Paniermehl und 25 g Ei. Ohne Rezept wird das Produkt eingekauft, nicht
hergestellt.

**Das Rezept ergibt** sagt, wie viel ein Rezept hervorbringt, in der Einheit
des Produkts. Bei einer Portion ist das 1 Stück. Ein Kuchen ergibt 12 Stück,
1.200 g Kartoffeln ergeben 1.000 g Pommes frites.

Eine Zeile kann jedes Produkt nennen, auch eines mit eigenem Rezept: Schnitzel
mit Pommes ist ein Schnitzel und eine Portion Pommes, Beilage. So steht die
Beilage einmal in den Regeln, und jedes Gericht, das sie mitbringt, rechnet mit
derselben Menge. Die Kalkulation geht diese Kette bis zum Einkauf hinunter; ein
Rezept darf sich dabei nicht selbst enthalten.

Nur was ein verkauftes Produkt braucht, zählt in der Kalkulation. Ein
eingekauftes Produkt, das weder im Sortiment steht noch in einem Rezept des
Sortiments vorkommt, landet im Bericht unter **in keiner Rezeptur**. Ein Rezept
ist ein Durchschnitt, keine Feststellung; es soll die übliche Portion treffen,
nicht jede. Weicht ein einzelner Betrieb belegbar ab, wird das Rezept in dessen
Prüfung [angepasst](kalkulation.md#rezeptur), nicht hier.

Preise stehen nicht hier. Sie gehören zum Betrieb und werden im
[Sortiment](kalkulation.md#sortiment) der Prüfung eingetragen.

## Ertragsregeln { #ertragsregeln }

Nicht alles, was eingekauft wird, wird verkauft. Eine Ertragsregel hat einen
Namen und einen Abzug in Prozent, etwa „Ausschankverlust“ mit 2 %. Der Name
sagt, wofür der Abzug steht: Schwund, Eigenverbrauch, Personalverpflegung,
Freirunden, Bruch oder Verderb.

Links stehen alle Kategorien und Produkte, rechts die Regeln der gewählten als
Tabelle. Eine neue Regel wird in die leere letzte Zeile geschrieben und ist
gespeichert, sobald Name und Abzug stehen. Änderungen an bestehenden Zeilen
speichern sich ebenso von selbst.

Eine Regel je Kategorie oder Produkt kann **Standard** sein. Sie gilt, solange in
der Prüfung nichts anderes [gewählt](kalkulation.md#ertragsregeln) ist; die
Standardregel des Produkts geht der seiner Kategorie vor. Ohne Standardregel wird
nichts abgezogen.

Die Sätze sind Erfahrungswerte, als Anhalt dient die Richtsatzsammlung des
Prüfungsjahres. Weicht ein Betrieb belegbar ab, bekommt er keine freie
Prozentzahl, sondern eine eigene Regel mit sprechendem Namen. So steht im
Bericht nicht nur der Satz, sondern auch, warum er gilt.

## Gewerbe { #gewerbe }

Die Liste der Gewerbekennzahlen, die eine Prüfung wählen kann, jede mit ihrer
Bezeichnung aus der Richtsatzsammlung, etwa `56101.0` Gast-, Speise- und
Schankwirtschaften. Mitgeliefert sind alle Kennzahlen der beiliegenden
Sammlungen. Eine fehlende lässt sich mit **Neu** anlegen, eine Bezeichnung
ändern oder eine Kennzahl löschen, die im Betrieb nie vorkommt.

Die Kennzahl entscheidet, welche Produkte die [Zuordnung](zuordnung.md)
vorschlägt: Kategorien gelten nur für Kennzahlen, die mit einem ihrer Präfixe
beginnen, etwa `561` für alle Gaststätten. Eine Prüfung übernimmt deshalb nur
Kennzahlen aus dieser Liste.
