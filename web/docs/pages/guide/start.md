# Die erste Prüfung

Am Ende der Anleitung steht ein Prüfbericht, der den kalkulierten Umsatz des
Betriebs dem erklärten gegenüberstellt. Die Schritte dorthin:

1. Prüfung anlegen – dieser Schritt
2. Erklärte Umsätze eintragen
3. Rechnungen importieren
4. Ausgelesene Rechnungen durchsehen
5. Eingekaufte Waren den Produkten zuordnen
6. Kalkulieren
7. Bericht exportieren

## Prüfung anlegen

![Kopfzeile der Prüfungsliste mit dem Knopf „Neue Prüfung“](img/neue-pruefung-knopf.png)

**Neue Prüfung** öffnet das Formular.

![Das leere Formular für eine neue Prüfung](img/neue-pruefung.png)

Einzutragen sind:

| Feld | Wert |
|---|---|
| Bezeichnung | `Gasthaus Zur Linde, Bp 2025` |
| Zeitraum von | `01.01.2025` |
| Zeitraum bis | `31.12.2025` |
| Name | `Gasthaus Zur Linde, Inh. Renate Vogel e.K.` |
| Steuernummer | `203/128/40507` |
| PaB-Nr. | `PaB 2025/0417` |

Der Zeitraum entscheidet, welche Rechnungen in die Kalkulation eingehen. Die
Gewerbekennzahl bleibt leer; sie wird erst für den Vergleich mit der
Richtsatzsammlung gebraucht.

**Anlegen** öffnet die Prüfung.

## Wo die Prüfung liegt

Jede Prüfung ist eine einzelne Datei im Ordner **Dokumente** unter
`Umsatzschätzung`, etwa `fall-20250502-080000-a1b2c3d4.db`. Darin steht alles,
was zu dieser Prüfung gehört: Eckdaten, erklärte Umsätze und Bestand, die
Rechnungen samt Scans und das Sortiment mit Preisen. Nicht in der Datei stehen
die Regeln, also Zutaten, Produkte und Zuordnungen; die liegen in der Regel-Datenbank
des Rechners.

Um eine Prüfung an Kollegen zu geben oder auf einem anderen Rechner
weiterzuarbeiten, wird diese Datei exportiert und dort wieder importiert, siehe
[Prüfung weitergeben](../import-export.md#prufung-weitergeben).

Weiter geht es mit [den Prüfungsdaten](pruefungsdaten.md).
