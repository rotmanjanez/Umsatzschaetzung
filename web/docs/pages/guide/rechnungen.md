# Rechnungen importieren

Der Beispieldatensatz enthält die 106 Eingangsrechnungen des Gasthauses für das
ganze Jahr 2025 von elf Lieferanten, alle als Scan:
[beispiel-rechnungen-2025.zip](https://github.com/rotmanjanez/Umsatzschaetzung/releases/download/beispiel-rechnungen-2025/beispiel-rechnungen-2025.zip)
(rund 430 MB). Herunterladen und entpacken.

In der Prüfung auf **1. Rechnungen** wechseln.

![Der noch leere Reiter „1. Rechnungen“](img/rechnungen-leer.png)

**Rechnungen hinzufügen** wählen und alle entpackten Dateien auswählen.

Jede Datei wird einzeln gelesen. E-Rechnungen (ZUGFeRD, XRechnung) enthalten ihre
Zeilen bereits maschinenlesbar und werden übernommen. Scans werden erkannt und
gegen die Belegsumme gerechnet: Geht die Rechnung auf, wird sie ebenfalls
übernommen, sonst landet sie in der Durchsicht. Der Fortschritt steht in einem
eigenen Fenster; am Ende steht dort, wie viele Rechnungen übernommen wurden und
wie viele zur Durchsicht anstehen.

![Das Importfenster am Ende eines Imports, hier mit sechs Dateien](img/rechnungen-import.png)

Danach steht jede Rechnung mit Lieferant, Nummer, Datum und Nettobetrag in der
Liste. Der Status sagt, woher die Zeilen stammen: **Automatisch** für eine
Rechnung, die aufging, **Prüfung offen** für eine, die durchgesehen werden muss.

![Die Rechnungsliste nach dem Import](img/rechnungen-liste.png)

Drei stehen auf **Prüfung offen**. Wie sie korrigiert werden, zeigt
[die nächste Seite](korrektur.md).
