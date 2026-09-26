# Rechnungen importieren

Die Rechnungen sind die Grundlage von allem Weiteren: Aus ihnen kommt, was der Betrieb eingekauft hat. Für das Beispiel liegen die 106 Eingangsrechnungen des Gasthauses für 2025 vor, alle als Scan.

## Die Dateien holen

[beispiel-rechnungen-2025.zip](https://github.com/rotmanjanez/Umsatzschaetzung/releases/download/beispiel-rechnungen-2025/beispiel-rechnungen-2025.zip) herunterladen und entpacken. Es entsteht ein Ordner `beispiel-rechnungen-2025` mit 106 Dateien, eine je Rechnung.

**Unter Windows**

Rechtsklick auf die heruntergeladene Datei, dann **Alle extrahieren**.

**Auf dem Mac**

Doppelklick auf die heruntergeladene Datei.

## Hinzufügen

In der Prüfung auf **1. Rechnungen** wechseln.

Das Symbol oben rechts (**Rechnungen hinzufügen**) öffnet die Dateiauswahl. In den entpackten Ordner gehen, alle Dateien markieren und **Öffnen**.

**Unter Windows**

Alle Dateien markiert `Ctrl`+`A`.

**Auf dem Mac**

Alle Dateien markiert `Cmd`+`A`.

Genauso geht es, die Dateien aus dem Ordner auf die gestrichelte Fläche zu ziehen.

## Warten

Das Programm liest jetzt jede Datei einzeln. Ein Scan ist zunächst nur ein Bild; das Programm erkennt darauf Lieferant, Nummer, Datum und jede einzelne Position und rechnet das Ergebnis gegen die Summen des Belegs. Geht die Rechnung auf, wird sie übernommen. Geht sie nicht auf, kommt sie zur Durchsicht: Sie wird trotzdem eingelesen, muss aber von Hand geprüft werden.

Je nach Rechner dauert das für die 106 Scans wenige Minuten bis eine halbe Stunde. Am Ende steht im Fenster, wie viele Rechnungen übernommen wurden und wie viele zur Durchsicht anstehen:

Den Dialog mit **Schließen** beenden. Jede Rechnung steht dann mit Lieferant, Nummer, Datum und Nettobetrag in der Liste. Die erste Spalte zeigt den Status:

- **Automatisch**: Die Rechnung ging auf und wurde übernommen.
- **Durchsicht offen**: Etwas passt nicht zusammen. Die Rechnung muss durchgesehen werden.

Über der Liste steht die Bilanz: `1 offen, 105 automatisch, 0 manuell`.

**Geschafft, wenn …**

- über der Liste `1 offen, 105 automatisch, 0 manuell` steht
- die oberste Rechnung, TK25-1583 ohne Lieferant, **Durchsicht offen** trägt

**Zum Nachlesen**

Welche Dateiformate das Programm liest, was es aus einem Scan holt und woran ein guter Scan zu erkennen ist, steht unter [Rechnungen importieren](https://docs.umsatzschaetzung.amtstools.de/dev/rechnungen/index.md).

[Weiter: 1. Rechnungen korrigieren Die offene Rechnung durchsehen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/korrektur/index.md)
