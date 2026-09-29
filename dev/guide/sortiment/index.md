# Sortiment anlegen

Bis hierher weiß das Programm, was das Gasthaus eingekauft hat. Jetzt braucht es die andere Seite: was das Gasthaus verkauft und zu welchem Preis. Das ist das Sortiment, im Grunde die Speisekarte mit Preisen. Aus Sortiment und Zutaten rechnet die Kalkulation den Umsatz.

Die Produkte selbst, samt Rezept, stehen wie die Zuordnungen in der [Regel-Datenbank](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank) und gelten für jede Prüfung. Das Programm bringt einen Grundstock mit, damit man gleich loslegen kann; was fehlt, legt man unter [Regeln → Produkte](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#produkte) an, und so wächst der Katalog mit jeder Prüfung. Aus ihm wird das Sortiment zusammengestellt; nur der Preis kommt vom Betrieb und gehört allein zu dieser Prüfung.

[Übung 4: Sortiment anlegen Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/sortiment/)

## Das erste Produkt von Hand

In der Prüfung auf **3. Sortiment** wechseln. Rechts oben steht das Suchfeld **Produkt hinzufügen …**; über dieses Feld kommt ein Produkt aus dem Katalog ins Sortiment.

In das Suchfeld `Schnitzel` tippen. Darunter erscheint eine Liste passender Produkte aus dem Katalog; **Schnitzel mit Pommes** anklicken. Das Produkt steht jetzt im Sortiment, noch mit dem Hinweis **Preis fehlt**:

In das Feld **Bruttopreis** `15,90` eintragen, also den Preis auf der Karte, mit Umsatzsteuer. Die Umsatzsteuer bleibt bei 19 %.

## Die übrigen Produkte

Die übrigen 27 Produkte werden hier nicht einzeln abgetippt. Für die folgenden Schritte liegen sie mit den Preisen des Beispiels in einer vorbereiteten Datei: [sortiment-gasthaus.csv](https://docs.umsatzschaetzung.amtstools.de/dev/guide/sortiment-gasthaus.csv) . Herunterladen, dann links neben **Produkt hinzufügen …** auf das linke der beiden Symbole (**CSV importieren**) klicken und die Datei auswählen. Das rechte, **CSV exportieren**, schreibt das Sortiment in eine solche Datei.

Die Produkte werden über ihren Namen im Katalog gefunden und mit Preis und Steuersatz ins Sortiment übernommen. Danach stehen 28 Produkte in der Liste:

**Geschafft, wenn …**

- 28 Produkte in der Liste stehen
- bei keinem **Preis fehlt** steht

**Wieder derselbe Betrieb**

Wird ein Betrieb ein zweites oder drittes Mal geprüft, muss das Sortiment nicht neu entstehen: in der alten Prüfung **CSV exportieren**, in der neuen dieselbe Datei importieren. Danach nur noch geänderte Preise anpassen und löschen, was nicht mehr auf der Karte steht.

**Die Tabelle von einer KI schreiben lassen**

Aus einer Speisekarte, als Foto oder von der Website des Betriebs, macht eine KI wie Claude, Gemini oder ChatGPT die passende Tabelle. Die Karte einfügen und dazu schreiben:

> Mach aus dieser Speisekarte eine CSV-Datei mit Semikolon als Trennzeichen und den Spalten Produkt, Bruttopreis und USt, Preise mit Komma. Den Steuersatz je Produkt nach deutschem Umsatzsteuerrecht für Verzehr im Lokal im Jahr 2025. Jedes Gericht nur mit seinem üblichen Namen, ohne Nummer auf der Karte und ohne Beschreibung.

Der Import findet ein Produkt nur über seinen Namen im Katalog. Aus „7. Elsässer Flammkuchen, hauchdünn, mit Speck und Zwiebeln“ wird kein Treffer, aus „Flammkuchen“ schon. Ohne diesen Hinweis übernimmt die KI meist die Karte wörtlich.

Das Jahr auf den Prüfungszeitraum setzen: Der Satz für Speisen im Lokal hat sich mehrfach geändert, der für Getränke nicht.

Das Ergebnis als `.csv` speichern und importieren. Namen, die der Katalog anders führt, überspringt der Import und meldet sie danach; diese in der Datei umbenennen oder über **Produkt hinzufügen …** ergänzen.

Nur mit öffentlichen Angaben wie der Karte aus dem Internet: Was aus der Akte stammt, unterliegt dem Steuergeheimnis und gehört in keine KI.

**Zum Nachlesen**

Wie die CSV-Datei aufgebaut ist und was beim Import mit vorhandenen Produkten geschieht, steht unter [Sortiment importieren und exportieren](https://docs.umsatzschaetzung.amtstools.de/dev/import-export/#sortiment). Was ein Produkt mit seinem Rezept ist, steht unter [Produkte](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#produkte).

[Weiter: 4. Kalkulation Den Umsatz aus dem Einkauf berechnen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/kalkulation/index.md)
