# Prüfung anlegen

Eine Prüfung ist im Programm ein Fall: ein Betrieb, ein Zeitraum und alles, was dazugehört. Nach dem Start zeigt das Programm die Liste der Prüfungen. Beim ersten Mal ist sie leer.

## Die Eckdaten

**Neue Prüfung** öffnet das Formular.

Einzutragen sind:

| Feld         | Wert                                         |
| ------------ | -------------------------------------------- |
| Bezeichnung  | `Gasthaus Zur Linde, Bp 2025`                |
| Zeitraum von | `01.01.2025`                                 |
| Zeitraum bis | `31.12.2025`                                 |
| Name         | `Gasthaus Zur Linde, Inh. Renate Vogel e.K.` |
| Steuernummer | `203/128/40507`                              |
| PAB-Nr.      | `PAB 2025/0417`                              |

Der Zeitraum entscheidet, welche Rechnungen in die Kalkulation eingehen. Die Gewerbekennzahl bleibt leer.

**Anlegen** öffnet die Prüfung.

## So sieht eine geöffnete Prüfung aus

Oben steht die Bezeichnung, darunter die Reiter **Prüfung**, **1. Rechnungen** bis **5. Bericht**: die fünf Schritte in der Reihenfolge, in der diese Anleitung sie durchgeht. Der Pfeil links oben führt zurück zur Liste.

Einen Knopf zum Speichern gibt es nicht. Jede Eingabe wird sofort gespeichert. Das Programm kann jederzeit geschlossen werden; die Prüfung steht beim nächsten Start wieder in der Liste.

## Erklärte Umsätze

Nach dem Anlegen ist der Reiter **Prüfung** geöffnet:

Er sammelt alles, was nicht aus den Rechnungen kommt. Für den Bericht wird davon eines gebraucht: der Umsatz, den der Betrieb erklärt hat. Unter **Erklärte Umsätze, netto in €** steht er getrennt nach Steuersatz.

| Feld           | Wert         |
| -------------- | ------------ |
| Umsatz zu 19 % | `196.418,00` |
| Umsatz zu 7 %  | leer lassen  |
| Umsatz zu 0 %  | leer lassen  |

Das Gasthaus hat alles zu 19 % erklärt. Die übrigen Abschnitte des Reiters bleiben für das Beispiel leer.

**Geschafft, wenn …**

- oben **Gasthaus Zur Linde, Bp 2025** steht und der Reiter **Prüfung** offen ist
- unter **Umsatz zu 19 %** `196.418,00` steht

**Zum Nachlesen**

Alle Felder des Reiters im Einzelnen beschreibt [Prüfung](https://docs.umsatzschaetzung.amtstools.de/dev/pruefung/index.md), die Liste der Prüfungen beschreibt [Prüfungen](https://docs.umsatzschaetzung.amtstools.de/dev/pruefungen/index.md).

[Weiter: 1. Rechnungen importieren Die 106 Rechnungen des Gasthauses einlesen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/rechnungen/index.md)
