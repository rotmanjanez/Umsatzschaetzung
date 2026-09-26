# Verwaltete Installation

Diese Seite richtet sich an die IT: wo das Programm seine Daten ablegt, wie sich die Regeln für mehrere Anwender gemeinsam führen lassen und welche Einstellungen sich zentral setzen lassen.

## Was wo liegt

|                            |                                                        |
| -------------------------- | ------------------------------------------------------ |
| Programm und Sprachmodelle | `C:\Program Files\Umsatzschätzung`                     |
| Regel-Datenbank            | siehe [unten](#regel-datenbank)                        |
| Prüfungen                  | `Dokumente\Umsatzschätzung`, folgt der Ordnerumleitung |

Eine **Prüfung** ist eine einzelne Datei mit allen Rechnungen und Scans. Sie gehört einer Person und wird nicht geteilt, sondern bei Bedarf [weitergegeben](https://docs.umsatzschaetzung.amtstools.de/dev/import-export/#prufung-weitergeben).

Die **Regel-Datenbank** `rules.db` enthält Zutaten, Produkte, Rezepte, Ertragsregeln und alle bestätigten Zuordnungen. Sie ist die gesammelte Arbeit aller Prüfungen und wächst mit jeder, siehe [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/index.md).

## Regel-Datenbank

Der Ordner der Regel-Datenbank liegt entweder auf dem Rechner oder auf einer Freigabe.

**Auf dem Rechner.** Das Installationspaket legt `%ProgramData%\Umsatzschätzung\store` an und gibt der Gruppe *Benutzer* Schreibrechte. Alle, die sich an diesem Rechner anmelden, arbeiten mit denselben Regeln.

**Auf einer Freigabe.** Ein UNC-Pfad wie `\\server\umsatzschaetzung\regeln`. Alle Installationen, die darauf zeigen, arbeiten mit denselben Regeln: Was eine Person zuordnet oder anlegt, steht beim nächsten Start auch den anderen zur Verfügung. Die Gruppe der Anwender braucht in dem Ordner Lese-, Schreib-, Erstell- und Löschrechte.

Auf einem Terminalserver und in einer nicht dauerhaften VDI übersteht ein Ordner auf dem Rechner die Abmeldung nicht. Dort gehört die Regel-Datenbank auf eine Freigabe.

### Gleichzeitige Nutzung

Mehrere Personen können gleichzeitig mit derselben Regel-Datenbank arbeiten. Die meiste Zeit werden Regeln nur gelesen. Geschrieben wird, wenn jemand eine Zuordnung bestätigt oder eine Zutat, ein Produkt oder eine Ertragsregel ändert, und jede Änderung betrifft nur diesen einen Eintrag.

### Sicherungen

Bei jedem Start legt das Programm eine Kopie der Datenbank im Unterordner `snapshots` an und behält die letzten zehn. Ist `rules.db` beim Start beschädigt, stellt es die jüngste Kopie wieder her und meldet das.

Lässt sich im Ordner keine Kopie anlegen, startet das Programm nicht, sondern nennt den Ordner und die fehlenden Rechte. Der ganze Ordner gehört in die Datensicherung.

## Voraussetzungen

Windows 10 ab 22H2 oder Windows 11, 64 Bit. Unter Windows 10 muss die **Microsoft Edge WebView2 Runtime** installiert sein, sonst bricht die Installation mit einem Hinweis ab. Windows 11 bringt sie mit.

## Installation mit Dialog

Das Installationspaket `umsatzschaetzung-win-x64.msi` installiert für alle Benutzer des Rechners und fragt nach dem Installationsordner nach der Regel-Datenbank: **Nur auf diesem Rechner** oder **In einem gemeinsamen Ordner** mit dem Pfad der Freigabe.

## Unbeaufsichtigte Installation

Ohne Dialog werden die Ordner als Eigenschaften übergeben:

```
msiexec /i umsatzschaetzung-win-x64.msi /qn STORE="\\server\umsatzschaetzung\regeln"
```

| Eigenschaft    | Bedeutung                                                                                                              |
| -------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `STORE`        | Ordner der Regel-Datenbank, lokal oder als UNC-Pfad                                                                    |
| `STORELOCAL=1` | den Ordner `STORE` auf dem Rechner anlegen und der Gruppe *Benutzer* Schreibrechte geben; nur für einen lokalen Ordner |
| `CASEDIR`      | Ordner für die Prüfungen                                                                                               |

Für einen lokalen, gemeinsamen Ordner wie beim Dialog:

```
msiexec /i umsatzschaetzung-win-x64.msi /qn STORELOCAL=1 STORE="C:\ProgramData\Umsatzschätzung\store"
```

Ohne `STORE` legt jede Person ihre Regeln unter `%LOCALAPPDATA%\Umsatzschätzung\store` ab, jede für sich.

Die Werte stehen danach unter `HKLM\SOFTWARE\Umsatzschätzung`. Ein Update übernimmt sie; übergebene Eigenschaften ersetzen sie.

## Gruppenrichtlinien

Die administrativen Vorlagen liegen jeder Version bei: [umsatzschaetzung-policy.zip](https://github.com/rotmanjanez/Umsatzschaetzung/releases/latest/download/umsatzschaetzung-policy.zip). Das Archiv hat den Aufbau von `PolicyDefinitions` (`Umsatzschätzung.admx`, `de-DE\Umsatzschätzung.adml`) und wird unverändert in den zentralen Speicher entpackt. Unter **Computerkonfiguration → Administrative Vorlagen → Umsatzschätzung**:

| Richtlinie                     | Wert                                                |
| ------------------------------ | --------------------------------------------------- |
| **Gemeinsame Regel-Datenbank** | Ordner der Regel-Datenbank, lokal oder als UNC-Pfad |
| **Ordner für Prüfungsakten**   | Ordner für die Prüfungen der angemeldeten Person    |

Umgebungsvariablen wie `%ProgramData%` oder `%USERPROFILE%` werden aufgelöst. Die Richtlinie hat Vorrang vor dem, was bei der Installation angegeben wurde. Das Programm liest die Ordner in dieser Reihenfolge und nimmt den ersten gesetzten Wert:

1. `HKLM\SOFTWARE\Policies\Umsatzschätzung` (Gruppenrichtlinie)
1. `HKLM\SOFTWARE\Umsatzschätzung` (Installation)
1. `HKCU\SOFTWARE\Umsatzschätzung`
1. der Standard aus der Tabelle [oben](#was-wo-liegt)

Die Werte heißen `Store` und `CaseDir`. Sie werden beim Start gelesen, eine Änderung wirkt ab dem nächsten Start.

## Berichtsvorlagen

Der Bericht entsteht aus einer HTML-Vorlage, die mit den Regeln in der Regel-Datenbank liegt. Wer den Bericht an eine eigene Form anpassen will, findet Aufbau und Datenmodell unter [Berichtsvorlagen](https://docs.umsatzschaetzung.amtstools.de/dev/vorlagen/index.md).

## Sicherheit und Datenschutz

Siehe [Sicherheit und Datenschutz](https://docs.umsatzschaetzung.amtstools.de/dev/datenschutz/index.md).
