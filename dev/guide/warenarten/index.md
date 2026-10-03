# Warenarten

Unten in der [Produktionskette](https://docs.umsatzschaetzung.amtstools.de/dev/guide/kette/index.md) stehen die Produkte, die eingekauft werden: Fassbier, Schweinefleisch, Rotwein. Jedes nennt unter **Warenarten** die Namen, unter denen es auf Rechnungen steht. Die helfen der Zuordnung bei Artikeln, die sie noch nicht kennt, und bringen so den Einkauf in die Kette. Im Beispiel bekommt der Rotwein eine dazu: Domina.

[Übung 3: Warenarten Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/warenarten/)

## Der Domina, noch einmal

Die Zuordnung, die in der ersten Prüfung bestätigt wurde, gilt für genau diesen Artikel: **2022 Domina trocken 0,75 l** von Weingut Sommer. Was ein Domina ist, weiß das Programm damit noch nicht. Unter den Warenarten des Rotweins steht er nicht, und als nächsten Vorschlag nennt es Schaumwein.

## Das Produkt finden

Rechts oben **Regeln** öffnen und in **Filtern** `Rotwein` eintippen. Gesucht wird auch in Kategorie und Warenarten, darum steht nicht nur der Rotwein da: auch der Essig, mit „Rotweinessig“ unter seinen Warenarten, und **Rotwein 0,2 l**, das Glas Rotwein, ein Glied weiter oben in der Kette.

Auf **Rotwein** klicken. Rechts steht, was zu ihm gehört:

## Was zu einem Produkt gehört

- **Name** und **Einheit**: Der Rotwein wird in Millilitern gezählt, im Einkauf, im Bestand und in jedem Rezept. Solange Rezepte oder Zuordnungen ihn verwenden, bleibt die Einheit fest.
- **Kategorie**: hier Wein. Sie bestimmt, welche Ertragsregeln gelten, und die Sparte im Rohgewinnaufschlag.
- **Warenarten**: unter welchen Namen das Produkt eingekauft wird, einer je Zeile, von Merlot bis Dornfelder. Sie helfen der Zuordnung bei Artikeln, die sie noch nicht kennt.
- **Rezept**: keines. Der Rotwein wird eingekauft, nicht hergestellt; hier beginnt seine Kette.

## Eine Warenart anfügen

Ins Feld **Warenarten** klicken, ans Ende der Liste gehen und in einer neuen Zeile `Domina` eintragen. Gespeichert wird von selbst, sobald eine kurze Pause im Tippen eintritt.

Zur Probe in **Filtern** `Domina` eintippen. Vorher stand dort „Keine Treffer“, jetzt findet die Suche genau ein Produkt: Rotwein.

## Was das bringt

Kommt ab jetzt ein Domina vor, von welchem Weingut und aus welchem Jahrgang auch immer, findet die Zuordnung das Wort beim Rotwein, und von dort geht der Einkauf die Kette hinauf, etwa ins Glas **Rotwein 0,2 l**. Das gilt für jede Prüfung danach und für alle, die dieselbe [Regel-Datenbank](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank) nutzen.

Eine gute Warenart ist die Ware, nicht der Artikeltext: „Domina“ ja, „2022 Domina trocken 0,75 l“ nein. Den Artikel deckt die bestätigte Zuordnung schon ab.

## Neu und Löschen

Das Plus neben **Filtern** legt ein neues Produkt an. Für den Einkauf braucht es das selten: wenn eine Ware ganz fehlt, oder wenn sie anders gezählt wird. Pommes in Stück und Pommes in Gramm wären zwei Produkte.

Der Mistkübel ganz unten löscht das Produkt, nach einer Rückfrage. Solange ein Rezept, eine Zuordnung oder eine Ertragsregel es noch braucht, lehnt das Programm ab und nennt, wer es verwendet. Beim Rotwein wären das unter anderem die Zuordnung des Domina und das Produkt „Rotwein 0,2 l“: Ein Glied mitten aus der Kette lässt sich nicht herausnehmen.

## Die Regeln schließen

Das Kreuz rechts oben (oder Esc) schließt die Regeln.

**Geschafft, wenn …**

- unter **Regeln → Produkte** beim Rotwein die letzte Warenart `Domina` ist
- die Suche nach `Domina` genau ein Produkt findet: Rotwein

**Zum Nachlesen**

Was eine gute Warenart ist, steht unter [Warenarten pflegen](https://docs.umsatzschaetzung.amtstools.de/dev/zuordnung/#warenarten), was Einheit, Kategorie und Stückgewicht bedeuten unter [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#produkte).

[Weiter: Produkte Ein neues Gericht oben an die Kette](https://docs.umsatzschaetzung.amtstools.de/dev/guide/produkte/index.md)
