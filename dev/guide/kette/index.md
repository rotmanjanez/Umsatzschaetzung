# Die Produktionskette

Unter **Regeln → Produkte** steht alles, womit gerechnet wird: was eingekauft wird, was daraus gemacht wird und was verkauft wird. Das **Rezept** eines Produkts nennt andere Produkte, und deren Rezepte wieder andere, bis zu Produkten ohne Rezept, die eingekauft werden. Zusammen beschreiben die Produkte so eine Produktionskette. Im Beispiel wird das Schnitzel mit Pommes bis zum Einkauf zurückverfolgt, und die Pommes frites bekommen ein Rezept aus Kartoffeln.

[Übung 2: Die Produktionskette Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/kette/)

## Oben in der Kette

Rechts oben **Regeln** öffnen; der erste Reiter ist **Produkte**. In **Filtern** `Schnitzel mit Pommes` eintippen und **Schnitzel mit Pommes** anklicken. Sein **Rezept** hat zwei Zeilen, und beide nennen keinen Einkauf, sondern andere Produkte: **Schnitzel, ohne Beilage** und **Pommes, Beilage**, je 1 Stück. **Das Rezept ergibt** 1 Stück, eine Portion.

## Eine Stufe tiefer

Eine Stufe tiefer: `Pommes Beilage` filtern und **Pommes, Beilage** anklicken. Eine Portion sind 180 g **Pommes frites** und 100 ml **Pflanzenöl**, das Altöl aus der Fritteuse eingerechnet. Weil die Beilage ein eigenes Produkt ist, steht sie nur einmal in den Regeln, und jedes Gericht mit Pommes rechnet mit derselben Portion.

## Unten in der Kette

Die **Pommes frites** haben kein Rezept: **Ohne Rezept wird das Produkt eingekauft, nicht hergestellt.** Dafür haben sie **Warenarten**, die Namen, unter denen sie auf Rechnungen stehen. Hier beginnt die Kette: Was die Zuordnung den Pommes frites zuordnet, geht von hier nach oben in jedes Gericht, das sie braucht.

Die Kalkulation geht diese Kette in jeder Prüfung durch, vom Einkauf unten bis zum verkauften Produkt oben. Ein Rezept darf sich dabei nicht selbst enthalten, auch nicht über andere Produkte.

## Ein Glied mehr

Das Gasthaus kauft Pommes frites beim Großmarkt und Frühkartoffeln bei der Gärtnerei. Damit die Kalkulation auch Pommes aus Kartoffeln kennt, bekommen die Pommes frites ein Rezept: unter **Rezept** das Plus (**Rezeptzeile hinzufügen**) anklicken.

`Kartoffeln` tippen und aus der Liste **Kartoffeln** wählen, als **Menge** `1200`. Die Einheit steht schon auf Gramm, weil Kartoffeln in Gramm gezählt werden.

**Das Rezept ergibt** sagt, wie viel das Rezept hervorbringt, in der Einheit des Produkts: hier `1000` Gramm Pommes frites aus 1.200 g Kartoffeln. Bei einer Portion ist es 1 Stück, bei einem Kuchen 12 Stück.

Gespeichert wird von selbst, sobald jede Rezeptzeile Produkt und Menge hat und eine kurze Pause im Tippen eintritt.

## Was das bringt

Die Pommes frites werden jetzt eingekauft oder aus Kartoffeln gemacht. Die Kalkulation rechnet mit beidem und verteilt den Einkauf so, dass möglichst wenig übrig bleibt. Kauft ein Betrieb nur Kartoffeln und keine Pommes, zählt jede Portion Pommes trotzdem. Das gilt in jeder Prüfung danach, für jedes Gericht mit Pommes.

**Geschafft, wenn …**

- unter **Regeln → Produkte** die **Pommes frites** ein Rezept haben: `1200` Gramm **Kartoffeln**, **Das Rezept ergibt** `1000` Gramm

**Zum Nachlesen**

Was Produkte und Rezepte bedeuten, steht unter [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#produkte) und [Rezepte](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#rezepte), wie die Kalkulation einen Einkauf auf die Produkte verteilt unter [Kalkulation](https://docs.umsatzschaetzung.amtstools.de/dev/kalkulation/index.md).

[Weiter: Warenarten Wo die Rechnungen in die Kette kommen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/warenarten/index.md)
