# Prüfung anlegen

<p class="us-steps"><span class="here">Prüfung</span><a href="../rechnungen/">1. Rechnungen</a><a href="../zuordnung/">2. Zuordnung</a><a href="../sortiment/">3. Sortiment</a><a href="../kalkulation/">4. Kalkulation</a><a href="../bericht/">5. Bericht</a></p>

Eine Prüfung ist im Programm ein Fall: ein Betrieb, ein Zeitraum und alles, was
dazugehört. Nach dem Start zeigt das Programm die Liste der Prüfungen. Beim
ersten Mal ist sie leer.

![Die leere Liste der Prüfungen mit dem Knopf „Neue Prüfung“](img/neue-pruefung-knopf.png)

## Die Eckdaten

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
Gewerbekennzahl bleibt leer.

**Anlegen** öffnet die Prüfung.

## So sieht eine geöffnete Prüfung aus

![Die Kopfzeile einer geöffneten Prüfung mit den sechs Reitern](img/pruefung-kopf.png)

Oben steht die Bezeichnung, darunter die Reiter **Prüfung**, **1. Rechnungen**
bis **5. Bericht**: die fünf Schritte in der Reihenfolge, in der diese
Anleitung sie durchgeht. Der Pfeil links oben führt zurück zur Liste.

Einen Knopf zum Speichern gibt es nicht. Jede Eingabe wird sofort gespeichert.
Das Programm kann jederzeit geschlossen werden; die Prüfung steht beim nächsten
Start wieder in der Liste.

## Erklärte Umsätze

Nach dem Anlegen ist der Reiter **Prüfung** geöffnet:

![Der Reiter „Prüfung“ direkt nach dem Anlegen](img/pruefung-leer.png)

Er sammelt alles, was nicht aus den Rechnungen kommt. Für den Bericht wird
davon eines gebraucht: der Umsatz, den der Betrieb erklärt hat. Unter
**Erklärte Umsätze, netto in €** steht er getrennt nach Steuersatz.

| Feld | Wert |
|---|---|
| Umsatz zu 19 % | `196.418,00` |
| Umsatz zu 7 % | leer lassen |
| Umsatz zu 0 % | leer lassen |

![Die erklärten Umsätze des Gasthauses](img/erklaerte-umsaetze.png)

Das Gasthaus hat alles zu 19 % erklärt. Die übrigen Abschnitte des Reiters
bleiben für das Beispiel leer.

!!! geschafft "Geschafft, wenn …"
    - oben **Gasthaus Zur Linde, Bp 2025** steht und der Reiter **Prüfung** offen ist
    - unter **Umsatz zu 19 %** `196.418,00` steht

!!! nachlesen "Zum Nachlesen"
    Alle Felder des Reiters im Einzelnen beschreibt [Prüfung](../pruefung.md),
    die Liste der Prüfungen beschreibt [Prüfungen](../pruefungen.md).

[Weiter: 1. Rechnungen importieren <span>Die 106 Rechnungen des Gasthauses einlesen</span>](rechnungen.md){ .us-next }
