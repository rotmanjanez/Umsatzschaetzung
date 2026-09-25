# Headless

Drives the real interface headlessly and photographs it. The driver knows neither
cases nor file names nor target folders - all of that lives in the script and in
the switches. Same run, same bytes: if an image changes, the interface changed.

    dotnet run --project tools/headless -- web/docs/shots/guide.jsonl \
        --width 1320 --height 860 --out web/docs/pages/guide/img

    --out     target folder for the images (default: next to the script)
    --rules   rule set as JSON (default: the app's own seeded rule set)
    --width   window width, --height window height (default: as the app opens it)
    --scale   pixels per point (default 2)
    --pad     padding around a crop (default 16)
    --readings folder of recorded readings (default: none, every scan is read)

With `--readings` a scan is read once and its reading, the words, the correction and the
tagged draft, is kept under the hash of its bytes and the readers. Later runs replay it
instead of reading the page again, so the images no longer depend on how fast or how
exactly the machine reads. The `shots` job keeps that folder in the Actions cache.

Files named in a script live where they live: absolute, or relative to the working
directory.

The images of the documentation are not kept in the repository. The `shots` job of
`deploy-web` takes them and hands them to the docs job as an artifact. Anyone who
needs them while writing takes them themselves - `web/docs/pages/img` and
`web/docs/pages/guide/img` are ignored:

    dotnet run --project tools/headless -- web/docs/shots/pruefungen.jsonl \
        --width 1320 --height 860 --out web/docs/pages/img

## Script

One step per line, JSONL. Blank lines and `//` separate and explain:

    { "do": "click", "at": { "text": "Neue Prüfung" } }
    { "do": "shot", "name": "neue-pruefung", "at": { "name": "NewLabel", "up": "StackPanel" } }

Every step has a `do`, may set `"window": "dialog"` (it then applies to the topmost
window above the main window) and waits afterwards until the interface has settled.

| `do`       | fields | does |
|------------|--------|------|
| `shot`     | `name`, `at?`, `trim?`, `clip?` | writes `<out>/<name>.png`; without `at` the whole window |
| `click`    | `at` | triggers the button - if `at` is not one itself, the next one above it, else the first one inside it (a row's own button); a radio button is checked instead |
| `type`     | `at`, `text` | writes `text` into the field - if `at` is none, into the first one inside it (the price in a row) |
| `choose`   | `at`, `text`, `item` | types `text` into a search box and takes the entry `item` from its drop-down |
| `pick`     | `files` | the next file dialog answers with these files |
| `focus`    | `at?` | sets the focus; without `at` it takes it away (otherwise the caret blinks into the image) |
| `deselect` | `at` | clears the selection of a list |
| `select`   | `at` | selects the row of a list that `at` sits in |
| `top`      | `at` | scrolls the list `at` sits in back to its first row |
| `edit`     | `at`, `column`, `text?` | puts the cursor on the cell in `column` of the row `at` sits in; with `text` it is typed there and committed |
| `open`     | `number` | opens the invoice with this number in its own window |
| `tab`      | `header` | switches to the tab with this caption |
| `import`   | `files` | imports these files, or every file of a folder named here, into the open case and waits for them |
| `wait`     | `rounds?` | waits further rounds, in case one is not enough |
| `restart`  | | quits the app, deletes the rule store, starts it again on the same cases and opens the case that was open |

`at` looks for a control: `name` is the `x:Name` from the XAML, `text` the visible
caption, `starts` the beginning of one (for a long row that the scan may have read
with a twist at the end), `tip` the tooltip (for buttons that only show an icon),
`type` the type (`"DataGrid"`). Several fields narrow it down further, and
`up` then climbs to the nearest ancestor of that type:

    { "text": "Lieferant", "up": "DataGrid" }

A `text` or `starts` that nothing on screen shows is looked for in the rows of the
lists: the row that holds it is scrolled into view, and the search runs again.

A `shot` crops to `at`. `trim` shrinks that frame beforehand (`{ "top": 16 }`),
`clip` pulls the bottom edge onto the last element of a type inside it
(`"DataGridRow"`, so that no empty rows come along).
