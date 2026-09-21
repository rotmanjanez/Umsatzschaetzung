# Prüfungen

Die Startseite listet alle Prüfungen aud die das Program zugriff hat. Ein Klick auf eine Zeile
öffnet sie. Über `Neue Prüfung` kann eine neue Prüfung in der Datenbank angelegt werden.

![Filtern, Regeln, Importieren und Neue Prüfung über der Liste](img/pruefungen-leiste.png)

In der Prüfung liegen ihre Rechnungen und Belege und die Entscheidungen zu
diesem Betrieb: Zuordnung der Rechnungszeilen, Verkaufspreise, gewählte
Ertragsregeln, Bestand, erklärte Umsätze.

![Die Liste mit drei angelegten Prüfungen](img/pruefungen-liste.png)

## Suchen

Das Suchfeld filtert nach Bezeichnung, Zeitraum und Namen des
Steuerpflichtigen.

## Wo ist eine Prüfung gespeicher?

Jede Prüfung ist eine einzelne Datei im Ordner **Dokumente** unter
`Umsatzschätzung`, etwa `fall-20250502-080000-a1b2c3d4.db`. Alle informationen zu diesem Fall, darunter auch die Belege und Scan, Sind darin gespeicher.

!!! note "Verwaltete Installationen"
    Unter Windows kann der Ordner per Gruppenrichtlinie umgelegt sein, etwa auf
    ein Netzlaufwerk.

## Importieren und Exportieren

**Importieren** übernimmt eine Falldatei in das Programm und öffnet sie. Sie
liegt danach im selben Ordner wie die übrigen Prüfungen.

Das Pfeilsymbol in der Zeile legt umgekehrt eine Kopie der Prüfung ab, wo Sie
wollen. Genaueres unter [Import und Export](import-export.md).


## Löschen

Das Papierkorbsymbol am Ende der Zeile löscht eine Prüfung mitsamt Rechnungen und Belegen.
Löschen kann i.d.R. nicht rückgängig gemacht werden.

![Die Rückfrage vor dem Löschen](img/pruefungen-loeschen.png)
