# Ein neues Produkt

Oben in der [Produktionskette](https://docs.umsatzschaetzung.amtstools.de/dev/guide/kette/index.md) stehen die Produkte, die verkauft werden: Ihr **Rezept** sagt, aus welchen anderen Produkten sie gemacht werden und wie viel davon. Aus diesen Produkten stellt jede Prüfung ihr Sortiment zusammen. Im Beispiel fehlt ein Gericht des Gasthauses: das Schäufele.

[Übung 4: Ein neues Produkt Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/produkte/)

## Ein Produkt anlegen

Auf der Karte des Gasthauses steht ein **Schäufele mit Kloß**. Unter **Regeln → Produkte** zeigt die Suche nach `Schäufele` **Keine Treffer**: Die Regeln kennen es nicht.

Das Plus neben **Filtern** legt ein neues Produkt an. In **Name** `Schäufele mit Kloß` eintragen, so wie es auf der Karte heißt.

Sobald der Name steht und das Feld verlassen wird, ist das Produkt angelegt und steht links in der Liste. Alles Weitere wird von selbst gespeichert, nach einer kurzen Pause im Tippen.

Die **Einheit** steht auf **Stück**, und das passt: Verkauft wird das Schäufele portionsweise. Kartoffeln oder Rotwein werden in Gramm oder Millilitern gezählt, ein Gericht in Stück.

## Das Rezept

Das Plus unter **Rezept** fügt eine Zeile an; ein neues Produkt beginnt ohne. Ins Feld `Knochen` tippen und aus der Liste **Schweinefleisch mit Knochen** wählen. Gesucht wird in Name und Kategorie, dahinter folgen ähnliche Produkte.

In **Menge** `450` eintragen: Ein Schäufele wiegt roh, mit Knochen, etwa 450 g.

Die Einheit steht auf **Gramm**, denn Schweinefleisch mit Knochen wird in Gramm gezählt, im Einkauf wie in jedem Rezept.

Jeder weitere Bestandteil bekommt mit dem Plus eine eigene Zeile. Für den Kloß: noch einmal das Plus, `Klöße` tippen, **Klöße und Knödel** wählen und `200` Gramm eintragen. **Das Rezept ergibt** bleibt bei 1 Stück: Das Rezept ist eine Portion.

## Löschen

Der Mistkübel unter dem Rezept löscht das Produkt, nach einer Rückfrage. Steckt es im Rezept eines anderen Produkts, lehnt das Programm ab und nennt dieses.

Gelöscht wird nur, was im Amt nie vorkommt: Eine Prüfung, die das Produkt im Sortiment führt, rechnet danach ohne es. Schon gespeicherte Berichte bleiben unverändert.

## Im Sortiment

Die Regeln schließen und in der Prüfung auf **3. Sortiment** wechseln. In **Produkt hinzufügen …** `Schäufele` tippen: Das neue Produkt steht schon in der Liste darunter. Jede Prüfung kann es ab jetzt ins Sortiment nehmen, auch beim [Import des Sortiments](https://docs.umsatzschaetzung.amtstools.de/dev/import-export/#sortiment) aus einer CSV-Datei.

Einen Preis hat das Produkt in den Regeln nicht: Der ist betriebsspezifisch und kommt erst mit dem Sortiment der Prüfung dazu, von der Karte des Betriebs.

**Geschafft, wenn …**

- die Suche nach `Schäufele` unter **Regeln → Produkte** genau ein Produkt findet: **Schäufele mit Kloß**, in Stück, mit 450 g Schweinefleisch mit Knochen und 200 g Klöße und Knödel
- in der Sonderprüfung auf **3. Sortiment** die Suche nach `Schäufele` in **Produkt hinzufügen …** **Schäufele mit Kloß** anbietet

**Zum Nachlesen**

Was in ein Rezept gehört, steht unter [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#rezepte), wie ein Rezept nur für eine Prüfung angepasst wird unter [Kalkulation](https://docs.umsatzschaetzung.amtstools.de/dev/kalkulation/#rezeptur).

[Weiter: Ertragsregeln Was auf dem Weg durch die Kette verloren geht](https://docs.umsatzschaetzung.amtstools.de/dev/guide/ertragsregeln/index.md)
