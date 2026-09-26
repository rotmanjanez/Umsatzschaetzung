# Sicherheit und Datenschutz

Für IT und Datenschutzbeauftragte. Sicherheitslücken meldet man wie in [SECURITY.md](https://github.com/rotmanjanez/Umsatzschaetzung/blob/main/SECURITY.md) beschrieben.

## Was wo liegt

| Ort                              | Inhalt                                                                                | Zugriff              |
| -------------------------------- | ------------------------------------------------------------------------------------- | -------------------- |
| `Dokumente\Umsatzschätzung`      | eine Datei je Prüfung: Rechnungen, Scans, Name, Steuernummer, PAB-Nummer, Kalkulation | die Person           |
| Ordner der Regel-Datenbank       | `rules.db`, `embeddings.db`, `snapshots\`                                             | alle, die ihn teilen |
| `%LOCALAPPDATA%\Umsatzschätzung` | Einstellungen, Fehlerprotokoll, `pages\` mit dem gerade angezeigten Bericht           | die Person           |

Eine Prüfung unterliegt dem Abgabengeheimnis. Sie verlässt den Rechner nur, wenn jemand sie [weitergibt](https://docs.umsatzschaetzung.amtstools.de/dev/import-export/#prufung-weitergeben), dann als vollständige Kopie. Die Ordner lassen sich zentral setzen, siehe [Verwaltete Installation](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/index.md).

Ein Bericht wird zum Anzeigen und Drucken kurz als Datei in `pages\` abgelegt und danach gelöscht; was ein Absturz dort zurücklässt, löscht der nächste Start. Das Fehlerprotokoll `crash.log` kann Dateinamen und Text aus Rechnungen enthalten; auch es löscht der nächste Start.

## Die gemeinsame Regel-Datenbank

Bestätigt eine Person eine Zuordnung, speichert die Regel-Datenbank dazu den Lieferanten, die Artikelnummer, die GTIN, den Wortlaut der Rechnungszeile und die Zutat. Das Programm schlägt sie danach allen vor, die die Regel-Datenbank teilen. Zuordnungen, die das Programm selbst trifft, bleiben in der Prüfung.

Nicht gespeichert werden Daten der geprüften Person, Mengen, Preise und Daten der Rechnungen und aus welcher Prüfung eine Zuordnung stammt. Die Regel-Datenbank beschreibt, was Lieferanten verkaufen, nicht was ein Betrieb eingekauft hat. Grenzfälle sind ein Lieferant, der eine natürliche Person ist, und ein so seltener Artikel, dass er auf einen Betrieb hindeutet. Teilen sollen sie deshalb nur Personen derselben Stelle, die dem Abgabengeheimnis unterliegen.

`embeddings.db` enthält Rechenwerte zu denselben Texten und lässt sich jederzeit löschen. `snapshots\` hält die letzten zehn Stände von `rules.db`.

## Verschlüsselung

Das Programm verschlüsselt nicht selbst. Vorausgesetzt werden BitLocker oder eine gleichwertige Datenträgerverschlüsselung, NTFS-Rechte auf den Ordnern und zugelassene Wege für weitergegebene Prüfungen.

## Löschen

- **Prüfung:** Löschen entfernt die Datei. Kopien in Sicherungen folgen deren Fristen.
- **Bericht:** Die Datei in `pages\` wird nach dem Anzeigen gelöscht, nicht überschrieben.
- **Rechnung:** Sie bleibt bis zum nächsten Programmstart, damit sich das Löschen zurücknehmen lässt, und wird dann aus der Datei entfernt und überschrieben.
- **Regel-Datenbank:** Eine Zuordnung ist mit keiner Prüfung verknüpft. Das Löschen einer Prüfung ändert daher nichts an der Regel-Datenbank. Gelöschte Regeln bleiben als gelöscht markiert in `rules.db` und in den Ständen unter `snapshots\`.

## Netzwerk

Das Programm baut keine eigenen Verbindungen auf: keine Updates, keine Nutzungsdaten. Die Hilfe (F1) öffnet die Dokumentation im Browser.

Berichte zeigt die **Microsoft Edge WebView2 Runtime** von Windows an, im privaten Modus, ohne JavaScript und ohne Zugriff auf andere Adressen als die eigenen Berichtsdateien. Auch eine eigene [Berichtsvorlage](https://docs.umsatzschaetzung.amtstools.de/dev/vorlagen/index.md) kann nichts nach außen senden. Updates und Diagnosedaten der Runtime selbst regeln die Richtlinien für Microsoft Edge.

## Zugriff und Nachvollziehbarkeit

Alle, die eine Regel-Datenbank teilen, brauchen Schreibrechte darauf. Eine geänderte Regel wirkt auf die Schätzungen aller. Jede Regel trägt den Zeitpunkt ihrer letzten Änderung und den Windows-Anmeldenamen der Person, die sie vorgenommen hat. Das zeigt, wer eine Regel geändert hat, schützt aber nicht vor Änderungen an der Datei außerhalb des Programms. Dafür gibt es die Stände in `snapshots\` und die Dateiüberwachung von Windows.

## Herkunft

Das Programm ist [quelloffen](https://github.com/rotmanjanez/Umsatzschaetzung). Jede Version wird in GitHub Actions aus dem getaggten Stand gebaut und getestet.

- **Modelle:** Jede Modelldatei ist mit einer SHA-256-Prüfsumme im Quellcode festgelegt. Weicht sie ab, bricht der Build ab.
- **Signatur:** Programm und Installationspaket sind mit einem selbst ausgestellten Zertifikat signiert. Es liegt als `publisher.cer` bei. Damit Windows der Signatur vertraut, wird es per Gruppenrichtlinie sowohl unter *Vertrauenswürdige Stammzertifizierungsstellen* als auch unter *Vertrauenswürdige Herausgeber* verteilt. Der Schlüssel liegt passwortgeschützt in den Secrets des GitHub-Repositorys, nicht in einem Hardware-Sicherheitsmodul.
- **Nachweise:** Jede Version enthält Prüfsummen, eine Stückliste (CycloneDX) und eine Herkunftsbestätigung von GitHub, prüfbar mit `gh attestation verify <datei> --repo rotmanjanez/Umsatzschaetzung`.

## Sprachmodelle

Die Modelle laufen nur auf dem Rechner und lernen im Betrieb nicht dazu.

| Modell              | Aufgabe                      | Trainiert auf                                                                                          |
| ------------------- | ---------------------------- | ------------------------------------------------------------------------------------------------------ |
| PP-OCR (Apache-2.0) | Text auf dem Scan lesen      | unverändert übernommen                                                                                 |
| Belegtagger (MIT)   | Kopf und Positionen erkennen | erzeugten Rechnungen mit erfundenen Daten                                                              |
| Zuordnung (MIT)     | Positionen Zutaten zuordnen  | Artikeldaten von Lieferanten, von Sprachmodellen von OpenAI zugeordnet; keine Rechnungen aus Prüfungen |

Die Testrechnungen im Quellcode sind erfunden.
