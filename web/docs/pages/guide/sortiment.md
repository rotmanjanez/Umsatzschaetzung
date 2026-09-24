# Sortiment anlegen

<p class="us-steps"><a href="../start/">Prüfung</a><a href="../rechnungen/">1. Rechnungen</a><a href="../zuordnung/">2. Zuordnung</a><span class="here">3. Sortiment</span><a href="../kalkulation/">4. Kalkulation</a><a href="../bericht/">5. Bericht</a></p>

Bis hierher weiß das Programm, was das Gasthaus eingekauft hat. Jetzt braucht
es die andere Seite: was das Gasthaus verkauft und zu welchem Preis. Das ist
das Sortiment, im Grunde die Speisekarte mit Preisen. Aus Sortiment und
Zutaten rechnet die Kalkulation den Umsatz.

Die Produkte selbst, samt Rezept, bringt das Programm mit. Aus diesem Katalog
wird das Sortiment zusammengestellt; nur der Preis kommt vom Betrieb.

## Das erste Produkt von Hand

In der Prüfung auf **3. Sortiment** wechseln. Rechts oben steht das Suchfeld
**Produkt hinzufügen …**, links daneben die beiden Symbole für den CSV-Import
und -Export:

![Die Kopfzeile des Sortiments](img/sortiment-kopf.png)

In das Suchfeld `Bier 0,5` tippen. Darunter erscheint eine Liste passender
Produkte aus dem Katalog; **Bier 0,5 l vom Fass** anklicken. Das Produkt steht
jetzt im Sortiment, noch mit dem Hinweis **Preis fehlt**:

![Das erste Produkt, noch ohne Preis](img/sortiment-erstes.png)

In das Feld **Bruttopreis** `4,60` eintragen, also den Preis auf der Karte,
mit Umsatzsteuer. Die Umsatzsteuer bleibt bei 19 %.

## Der Rest per Tabelle

Die übrigen 27 Produkte des Gasthauses stehen mit ihren Preisen in einer
Tabelle: [sortiment-gasthaus.csv](sortiment-gasthaus.csv){ download="sortiment-gasthaus.csv" }
[](../hilfe/csv.md){ title="Was ist eine CSV-Datei?" aria-label="Was ist eine CSV-Datei?" }.
Herunterladen, dann auf das linke Symbol der Kopfzeile (**CSV importieren**)
klicken und die Datei auswählen.

Die Produkte werden über ihren Namen im Katalog gefunden und mit Preis und
Steuersatz ins Sortiment übernommen. Danach stehen 28 Produkte in der Liste:

![Das Sortiment nach dem CSV-Import](img/sortiment-liste.png)

!!! geschafft "Geschafft, wenn …"
    - 28 Produkte in der Liste stehen
    - bei keinem **Preis fehlt** steht

!!! tip "Wieder derselbe Betrieb"
    Wird ein Betrieb ein zweites Mal geprüft, bei beratungsresistenten
    Mitbürgern auch ein drittes Mal, muss das Sortiment nicht neu entstehen:
    in der alten Prüfung **CSV exportieren**, in der neuen dieselbe Datei
    importieren. Danach nur noch geänderte Preise anpassen und löschen, was
    nicht mehr auf der Karte steht.

!!! tip "Die Tabelle von einer KI schreiben lassen"
    Aus einer Speisekarte, als Foto oder von der Website des Betriebs, macht
    eine KI wie Claude, Gemini oder ChatGPT die passende Tabelle. Die Karte
    einfügen und dazu schreiben:

    > Mach aus dieser Speisekarte eine CSV-Datei mit Semikolon als
    > Trennzeichen und den Spalten Produkt, Bruttopreis und USt, Preise mit
    > Komma. Den Steuersatz je Produkt nach deutschem Umsatzsteuerrecht für
    > Verzehr im Lokal im Jahr 2025.

    Das Jahr auf den Prüfungszeitraum setzen: Der Satz für Speisen im Lokal
    hat sich mehrfach geändert, der für Getränke nicht.

    Das Ergebnis als `.csv` speichern und importieren. Namen, die der Katalog
    anders führt, überspringt der Import und meldet sie danach; diese in der
    Datei umbenennen oder über **Produkt hinzufügen …** ergänzen.

    Nur mit öffentlichen Angaben wie der Karte aus dem Internet: Was aus der
    Akte stammt, unterliegt dem Steuergeheimnis und gehört in keine KI.

!!! nachlesen "Zum Nachlesen"
    Wie die CSV-Datei aufgebaut ist und was beim Import mit vorhandenen
    Produkten geschieht, steht unter
    [Sortiment importieren und exportieren](../import-export.md#sortiment).

[Weiter: 4. Kalkulation <span>Den Umsatz aus dem Einkauf berechnen</span>](kalkulation.md){ .us-next }
