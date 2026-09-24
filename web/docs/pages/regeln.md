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

| Regel | Gepflegt unter |
|---|---|
| Zutaten | **Regeln → Zutaten** |
| Produkte mit Rezept | **Regeln → Produkte** |
| Ertragsregeln | **Regeln → Ertragsregeln** |
| Zuordnungen | in der Prüfung unter [Zuordnung](zuordnung.md) |

## Zutaten { #zutaten }

Eine Zutat ist eine Ware, wie sie eingekauft wird: Fassbier, Schweinefleisch,
Rotwein. Zutaten sind bewusst grob gefasst. Hähnchenschenkel und ganze Hähnchen
sind beide Hähnchenfleisch; den Unterschied trägt das Rezept in der Menge.

- **Kategorie**: die Warengruppe, etwa Bier oder Fleisch. Ertragsregeln gelten
  meist für eine ganze Kategorie, und die Kategorie bestimmt die Sparte im
  Rohgewinnaufschlag.
- **Warenarten**: was unter dieser Zutat gebucht wird, eine je Zeile. Sie
  helfen der Zuordnung bei Artikeln, die sie noch nicht kennt, siehe
  [Warenarten pflegen](zuordnung.md#warenarten).
- **Stückgewicht**: ein Richtwert für Waren, die in Stück berechnet, aber in
  Gramm verarbeitet werden, etwa 1 Gurke ≈ 400 g. Leer lassen, wenn es keinen
  sinnvollen Wert gibt.

Eine Zutat hat keine eigene Einheit. Worin sie gemessen wird, legt das Rezept
fest, und alle Rezepte müssen sie gleich messen. Pommes in Stück und Pommes in
Gramm wären zwei Zutaten.

## Produkte { #produkte }

Ein Produkt ist etwas, das verkauft wird, mit seinem **Rezept**: welche Zutaten
in einer Portion stecken und wie viel davon. Bei Getränken ist das meist eine
Zeile, bei Speisen mehrere: Ein Cordon Bleu sind 200 g Schweinefleisch, 30 g
Schinken, 30 g Käse und 200 g Pommes.

Nur was in einem Rezept vorkommt, zählt in der Kalkulation. Eine Zutat ohne
Rezept landet im Bericht unter **in keiner Rezeptur**. Ein Rezept ist ein
Durchschnitt, keine Feststellung; es soll die übliche Portion treffen, nicht
jede. Weicht ein einzelner Betrieb belegbar ab, wird das Rezept in dessen
Prüfung [angepasst](kalkulation.md#rezeptur), nicht hier.

Preise stehen nicht hier. Sie gehören zum Betrieb und werden im
[Sortiment](kalkulation.md#sortiment) der Prüfung eingetragen.

## Ertragsregeln { #ertragsregeln }

Nicht alles, was eingekauft wird, wird verkauft. Eine Ertragsregel sagt, wie
viel Prozent abgehen, getrennt nach:

- **Schwund**: Schank-, Zuschnitt-, Brat- und Garverlust
- **Eigenverbrauch** des Inhabers und seiner Familie
- **Personal**: Personalverpflegung
- **Freigetränke**: Freirunden, Bruch und Verderb

Eine Regel gilt für eine Kategorie oder eine einzelne Zutat; die Regel der
Zutat geht vor. Für dieselbe Kategorie kann es mehrere Regeln geben, etwa
„Fassbier Standard“ und „Fassbier Altanlage“. Eine davon ist die
**Standardregel** und gilt, solange in der Prüfung nichts anderes
[gewählt](kalkulation.md#ertragsregeln) ist.

Die Sätze sind Erfahrungswerte, als Anhalt dient die Richtsatzsammlung des
Prüfungsjahres. Weicht ein Betrieb belegbar ab, bekommt er keine freie
Prozentzahl, sondern eine eigene Regel mit sprechendem Namen. So steht im
Bericht nicht nur der Satz, sondern auch, warum er gilt.
