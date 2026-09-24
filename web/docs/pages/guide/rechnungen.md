# Rechnungen importieren

<p class="us-steps"><a href="../start/">Prüfung</a><span class="here">1. Rechnungen</span><a href="../zuordnung/">2. Zuordnung</a><a href="../sortiment/">3. Sortiment</a><a href="../kalkulation/">4. Kalkulation</a><a href="../bericht/">5. Bericht</a></p>

Die Rechnungen sind die Grundlage von allem Weiteren: Aus ihnen kommt, was der
Betrieb eingekauft hat. Für das Beispiel liegen die 106 Eingangsrechnungen des
Gasthauses für 2025 vor, alle als Scan.

## Die Dateien holen

[beispiel-rechnungen-2025.zip](https://github.com/rotmanjanez/Umsatzschaetzung/releases/download/beispiel-rechnungen-2025/beispiel-rechnungen-2025.zip)
herunterladen und entpacken. Es entsteht ein Ordner `beispiel-rechnungen-2025`
mit 106 Dateien, eine je Rechnung.

!!! windows "Unter Windows"
    Rechtsklick auf die heruntergeladene Datei, dann **Alle extrahieren**.

!!! macos "Auf dem Mac"
    Doppelklick auf die heruntergeladene Datei.

## Hinzufügen

In der Prüfung auf **1. Rechnungen** wechseln.

![Der noch leere Reiter „1. Rechnungen“](img/rechnungen-leer.png)

**Rechnungen hinzufügen** öffnet die Dateiauswahl. In den entpackten Ordner
gehen, alle Dateien markieren und **Öffnen**.

!!! windows "Unter Windows"
    Alle Dateien markiert ++ctrl+a++.

!!! macos "Auf dem Mac"
    Alle Dateien markiert ++cmd+a++.

Genauso geht es, die Dateien aus dem Ordner auf die gestrichelte Fläche zu
ziehen.

## Warten

Das Programm liest jetzt jede Datei einzeln. Ein Scan ist zunächst nur ein
Bild; das Programm erkennt darauf Lieferant, Nummer, Datum und jede einzelne
Position und rechnet das Ergebnis gegen die Summen des Belegs. Geht die
Rechnung auf, wird sie übernommen. Geht sie nicht auf, kommt sie zur
Durchsicht: Sie wird trotzdem eingelesen, muss aber von Hand geprüft werden.

Je nach Rechner dauert das für die 106 Scans 10 bis 30 Minuten. Am Ende steht
im Fenster, wie viele Rechnungen übernommen wurden und wie viele zur Durchsicht
anstehen:

![Das Importfenster am Ende: 103 Rechnungen übernommen, 3 zur Durchsicht](img/rechnungen-import.png)

Den Dialog mit **Schließen** beenden. Jede Rechnung steht dann mit Lieferant,
Nummer, Datum und Nettobetrag in der Liste. Die erste Spalte zeigt den Status:

- **Automatisch**: Die Rechnung ging auf und wurde übernommen.
- **Durchsicht offen**: Etwas passt nicht zusammen. Die Rechnung muss
  durchgesehen werden.

Über der Liste steht die Bilanz: `3 offen, 103 automatisch, 0 manuell`.

![Die Rechnungsliste nach dem Import, die drei offenen Rechnungen oben](img/rechnungen-liste.png)

!!! geschafft "Geschafft, wenn …"
    - über der Liste `3 offen, 103 automatisch, 0 manuell` steht
    - die drei obersten Rechnungen **Durchsicht offen** tragen: eine ohne
      Lieferant, eine von der Metzgerei Hofmann, eine vom Weingut Sommer

!!! nachlesen "Zum Nachlesen"
    Welche Dateiformate das Programm liest, was es aus einem Scan holt und
    woran ein guter Scan zu erkennen ist, steht unter
    [Rechnungen importieren](../rechnungen.md).

[Weiter: 1. Rechnungen korrigieren <span>Die drei offenen Rechnungen durchsehen</span>](korrektur.md){ .us-next }
