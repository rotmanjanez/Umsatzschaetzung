# Beispielprüfung

Diese Anleitung führt Schritt für Schritt durch eine vollständige Prüfung: von
der Installation über die Rechnungen bis zum fertigen Bericht. Vorkenntnisse
mit dem Programm sind nicht nötig, eigene Belege auch nicht. Alles, was
gebraucht wird, steht hier zum Herunterladen.

## Was das Programm rechnet

Ein Gasthaus erklärt für ein Jahr einen Umsatz. Ob der plausibel ist, lässt
sich aus dem Einkauf nachrechnen: Wer 600 Liter Fassbier einkauft, schenkt
daraus rund 1.100 Gläser zu 0,5 l aus, ein Teil geht als Schankverlust
verloren. Mal 4,60 € je Glas ergibt das den Umsatz allein mit Fassbier. Das
Programm macht diese Rechnung für jede eingekaufte Ware und stellt die Summe
dem erklärten Umsatz gegenüber.

Dafür braucht es fünf Dinge, und genau das sind die fünf Reiter einer Prüfung:

<ol class="us-flow">
<li><b>1. Rechnungen</b> Die Eingangsrechnungen des Betriebs: was er eingekauft hat.</li>
<li><b>2. Zuordnung</b> Jede Rechnungsposition wird einer Zutat zugeordnet, etwa „Frankenbräu Pils Fass 50 l“ zu Fassbier. Das Programm schlägt vor, der Prüfer bestätigt oder korrigiert.</li>
<li><b>3. Sortiment</b> Was der Betrieb verkauft und zu welchem Preis: Bier 0,5 l für 4,60 €, Schnitzel mit Pommes für 15,90 €.</li>
<li><b>4. Kalkulation</b> Aus Zutaten, Rezepten und Preisen rechnet das Programm den Umsatz, den der Einkauf ergeben müsste.</li>
<li><b>5. Bericht</b> Der kalkulierte Umsatz neben dem erklärten, als PDF.</li>
</ol>

Ein paar Wörter kommen dabei immer wieder vor:

| Begriff | Bedeutung |
|---|---|
| **Zutat** | Eine Ware, wie sie eingekauft wird: Fassbier, Schweinefleisch, Rotwein. |
| **Produkt** | Etwas, das verkauft wird: Bier 0,5 l vom Fass, Schnitzel mit Pommes. |
| **Rezept** | Wie viel von welcher Zutat in ein Produkt geht. In ein Bier 0,5 l gehen 500 ml Fassbier. Die Rezepte bringt das Programm mit; für eine einzelne Prüfung lässt sich ein Rezept anpassen, ohne den Katalog zu ändern. |
| **Sortiment** | Die Produkte, die dieser Betrieb führt, mit seinen Preisen. |
| **Zuordnung** | Die Verbindung zwischen einer Rechnungsposition und einer Zutat. |
| **Regeln** | Zutaten, Produkte, Rezepte und alle bestätigten Zuordnungen. Sie gehören nicht zu einer Prüfung, sondern zum Programm, und wachsen mit jeder Prüfung. Weicht ein Betrieb ab, trägt die Prüfung ein eigenes Rezept, die Regeln bleiben unverändert. |

## Das Beispiel

Geprüft wird das **Gasthaus Zur Linde** für das Jahr 2025. Es hat einen Umsatz
von 196.418 € netto erklärt. Vorliegen 106 Eingangsrechnungen von elf
Lieferanten, alle als Scan, dazu die Speisekarte als Tabelle. Beide Dateien
werden an der Stelle, an der sie gebraucht werden, noch einmal verlinkt:

- [beispiel-rechnungen-2025.zip](https://github.com/rotmanjanez/Umsatzschaetzung/releases/download/beispiel-rechnungen-2025/beispiel-rechnungen-2025.zip),
  die Rechnungen, rund 430 MB
- [sortiment-gasthaus.csv](sortiment-gasthaus.csv){ download="sortiment-gasthaus.csv" },
  das Sortiment mit Preisen

## Zeitbedarf

Rund eine Stunde, davon etwa die Hälfte Wartezeit: Das Programm liest jeden
der 106 Scans einzeln, und das dauert je nach Rechner 10 bis 30 Minuten. Die
Anleitung sagt an der Stelle, was sich währenddessen schon erledigen lässt.

!!! tip "Mitmachen"
    Am meisten bringt die Anleitung, wenn jeder Schritt gleich im Programm
    mitgemacht wird. Am Ende jeder Seite steht, woran zu erkennen ist, dass
    der Schritt geklappt hat.

## Vorbereitung

Vorab muss das Programm auf dem Rechner installiert sein:

[macOS](install-macos.md){ .md-button .md-button--primary }
[Windows](install-windows.md){ .md-button }

Wer es schon installiert hat, geht direkt weiter.

[Weiter: Prüfung anlegen <span>Den Fall im Programm eröffnen und den erklärten Umsatz eintragen</span>](start.md){ .us-next }
