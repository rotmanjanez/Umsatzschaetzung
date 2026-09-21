# Headless

Fährt die echte Oberfläche kopflos und fotografiert sie. Der Treiber kennt weder
Prüfungen noch Dateinamen noch Zielordner - das steht alles im Skript und in den
Schaltern. Gleicher Lauf, gleiche Bytes: ändert sich ein Bild, hat sich die
Oberfläche geändert.

    dotnet run --project tools/headless -- web/docs/shots/guide.jsonl \
        --width 1320 --height 860 --out web/docs/docs/guide/img

    --out     Zielordner für die Bilder (Vorgabe: neben dem Skript)
    --rules   Regelsatz als JSON (Vorgabe: der mitgelieferte Regelsatz der App)
    --width   Fensterbreite, --height Fensterhöhe (Vorgabe: wie die App sie öffnet)
    --scale   Bildpunkte je Punkt (Vorgabe 2)
    --pad     Rand um einen Ausschnitt (Vorgabe 16)

Dateien im Skript liegen, wo sie liegen: absolut oder relativ zum Arbeitsverzeichnis.
In CI heißt das: Datensatz dorthin auspacken, wo das Skript ihn erwartet, `--out` in
den Docs-Baum, Bilder committen oder als Artefakt mitgeben.

## Skript

Ein Schritt je Zeile, JSONL. Leere Zeilen und `//` trennen und erklären:

    { "do": "click", "at": { "text": "Neue Prüfung" } }
    { "do": "shot", "name": "neue-pruefung", "at": { "name": "NewLabel", "up": "StackPanel" } }

Jeder Schritt hat ein `do`, darf `"window": "dialog"` setzen (dann gilt er für das
oberste Fenster über dem Hauptfenster) und wartet danach, bis die Oberfläche steht.

| `do`       | Felder | tut |
|------------|--------|-----|
| `shot`     | `name`, `at?`, `trim?`, `clip?` | schreibt `<out>/<name>.png`; ohne `at` das ganze Fenster |
| `click`    | `at` | löst den Knopf aus - trifft `at` keinen, wird der nächste darüber genommen |
| `type`     | `at`, `text` | schreibt `text` in das Feld |
| `focus`    | `at?` | setzt den Fokus; ohne `at` nimmt es ihn weg (sonst blinkt der Textcursor ins Bild) |
| `deselect` | `at` | hebt die Auswahl einer Liste auf |
| `tab`      | `header` | schaltet auf den Reiter mit dieser Beschriftung |
| `import`   | `files` | importiert diese Dateien in die offene Prüfung und wartet sie ab |
| `wait`     | `rounds?` | wartet weitere Runden, falls einmal nicht reicht |

`at` sucht ein Steuerelement: `name` ist das `x:Name` aus dem XAML, `text` die
sichtbare Beschriftung, `type` der Typ (`"DataGrid"`). Mehrere Felder grenzen
weiter ein, `up` geht danach zum nächsten Vorfahren dieses Typs hoch:

    { "text": "Lieferant", "up": "DataGrid" }

Ein `shot` schneidet auf `at` zu. `trim` verkleinert diesen Rahmen vorher
(`{ "top": 16 }`), `clip` zieht die Unterkante auf das letzte Element eines Typs
darin (`"DataGridRow"`, damit keine leeren Zeilen mitkommen).
