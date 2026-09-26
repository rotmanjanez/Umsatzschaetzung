# Dateiformate

Für die IT. Beschreibt, wie `rules.db` und die Datei einer Prüfung aufgebaut
sind, damit man sie mit einem SQLite-Werkzeug lesen und prüfen kann. Ändern
sollte man sie nur über das Programm.

## Schema und Version

Beide Dateien sind SQLite-Datenbanken. `PRAGMA user_version` im Dateikopf
nennt die Fassung des Schemas; diese Seite beschreibt **Schema 1**. Eine neue
Fassung entsteht nur, indem ein Schritt angehängt wird, der von der vorigen
auf die neue hebt. Ein veröffentlichter Schritt wird nie mehr geändert.

- Eine ältere Datei hebt das Programm beim Öffnen in einer einzigen
  Transaktion auf die aktuelle Fassung. Eine Prüfung wird davor als
  `<Name>.db.v<n>.bak` kopiert; die Kopie wird gelöscht, sobald die neue Datei
  die Integritätsprüfung besteht und sich vollständig lesen lässt, sonst ersetzt
  sie die neue Datei wieder.
- Eine neuere Datei, als das Programm kennt, wird nicht geöffnet und nicht
  verändert.
- `rules.db` läuft im Rollback-Journal (`journal_mode = DELETE`), weil sie auf
  einer Netzwerkfreigabe liegen kann, wo WAL nicht funktioniert. Vor jedem
  Start legt das Programm eine Kopie in `snapshots` ab, siehe
  [Sicherungen](verwaltung.md#sicherungen).

## Werte

Beträge und Mengen sind ganze Zahlen mit festem Maßstab, nie Gleitkomma:

| Größe | gespeichert als | Beispiel |
|---|---|---|
| Geldbetrag | Cent | 12,50 € = `1250` |
| Menge auf der Rechnung | Tausendstel der Einheit | 2,5 kg = `2500` |
| Einzelpreis auf der Rechnung | Millionstel Euro je `price_base_qty` | 0,925 € = `925000` |
| Preisbasis `price_base_qty` | Tausendstel der Einheit; `0` gilt als 1 | Preis je Stück = `1000` |
| Steuersatz, Abzug | Basispunkte, 1 % = 100 | 19 % = `1900` |
| Richtsatz (`satz`) | ganze Prozent | 250 % = `250` |
| Umsatzgrenzen der Richtsätze (`staffel`) | Cent | |
| Rezeptmenge, Bestand | ganze Einheiten der eigenen Einheit | 250 ml = `250` mit `MLT` |
| Faktor, Stückgewicht | ganze ml, g oder Stück | |

Weitere Regeln:

- Einheiten sind Codes nach UN/ECE Rec 20 (`LTR`, `KGM`, `H87`), Packmittel
  nach Rec 21 mit `X` davor (`XBO`, `XCS`).
- Ein Tag ist Text `JJJJ-MM-TT`, ein Zeitpunkt Text nach ISO 8601 mit Offset.
  `valid_from` gilt ab dem Tag, `valid_to` bis vor dem Tag; leer heißt offen.
- Aufzählungen stehen unter ihrem JSON-Namen (`zugferd`, `scan`, `lineNet`).
- Wahrheitswerte sind `0` und `1`.
- IDs sind Text; neue IDs sind UUID v7.
- `ord` hält die Reihenfolge einer Liste und beginnt bei 0.

## rules.db

`meta` hat genau eine Zeile: `store` ist die Kennung dieser Datenbank und
ändert sich, wenn sie aus einer Sicherung wiederhergestellt wird. `version`
steigt mit jeder Änderung an den Regeln, mindestens auf die Unix-Zeit in
Millisekunden, damit eine von Hand zurückkopierte Datei keinen Stand ein
zweites Mal vergibt. Es gilt nur zusammen mit `store`. `app`
ist die Programmversion, die sie zuletzt geöffnet hat.

Jede Regel trägt `valid_from`, `valid_to`, `changed_at`, `changed_by` und
`rev`, den Stand von `version` bei ihrer letzten Änderung. Gelöschtes bleibt
mit `deleted_at` stehen, damit eine mitgelieferte Regel nicht beim nächsten
Start wiederkommt.

| Tabelle | Inhalt | Maßstab und Sonderwerte |
|---|---|---|
| `category`, `category_gewerbe`, `category_gebinde` | Kategorien, auf Gewerbekennzahlen und Packmittel begrenzt | keine Zeile in `category_gewerbe` oder `category_gebinde` heißt: alle |
| `ingredient`, `ingredient_alias` | Zutaten und die Warenarten, unter denen sie gebucht werden | `piece_amount` in g oder ml je Stück, in `piece_unit`; leer: kein Richtwert |
| `mapping` | bestätigte Zuordnungen von Rechnungszeilen zu Zutaten | `factor` ist der Inhalt eines Gebindes in ml, g oder Stück; leer, wo die Einheit selbst umrechnet |
| `product`, `recipe_line` | Produkte und ihre Rezepturen | `amount` in `unit`; mit `sub_product_id` ist die Zeile ein Teilrezept in Stück |
| `yield_rule` | Ertragsregeln | `deduction` in Basispunkten |
| `gewerbe` | wählbare Gewerbekennzahlen | |
| `template` | Berichtsvorlagen | `is_default` bei genau einer |
| `sammlung`, `klasse`, `klasse_kennzahl`, `staffel`, `satz`, `synonym`, `pauschbetrag` | Richtsatzsammlungen je Jahr | `staffel.von` ausschließlich, `bis` einschließlich, in Cent, leer: offen; `satz` in ganzen Prozent, `von` und `bis` leer, wo nur ein Mittelsatz gedruckt ist; `pauschbetrag` in Cent |

`embeddings.db` enthält nur Rechenwerte für Vorschläge und wird bei Bedarf neu
erzeugt; sie ist kein Austauschformat.

## Datei einer Prüfung

Die Datei heißt wie die Bezeichnung der Prüfung. Eine weitergegebene Prüfung
hat dasselbe Format.

| Tabelle | Inhalt | Maßstab und Sonderwerte |
|---|---|---|
| `fall` | eine Zeile: Eckdaten, Steuerpflichtiger, Zeitraum | `mapped_store` und `mapped_at` nennen den Stand der Regeln, gegen den zuletzt zugeordnet wurde; `mapped_at = 0`: noch nie. `template_id` leer: Standardvorlage. `app_version`: Programmversion, die zuletzt geschrieben hat |
| `declared` | erklärter Umsatz je Steuersatz | `vat` in Basispunkten, `net` in Cent |
| `inventory` | Anfangs- und Endbestand | `opening`, `closing` in ganzen `unit` |
| `case_product`, `case_recipe` | Verkaufspreise und eigene Rezepturen | `gross_price` in Cent, `0` oder weniger: Preis fehlt. `recipe_basis` ist die Prüfsumme der Katalogrezeptur, von der kopiert wurde; leer: es gilt die des Katalogs |
| `yield_choice` | gewählte Ertragsregel je Zutat oder Kategorie | `yield_rule_id` leer: kein Abzug |
| `pinned` | fest vorgegebene Portionen | ganze Portionen |
| `no_revenue` | Zutaten ohne Umsatz, etwa Reinigungsmittel | |
| `case_mapping` | Zuordnungen, die das Programm selbst getroffen hat | wie `mapping` in `rules.db` |
| `invoice` | Rechnungen | Summen in Cent. `stated_net`, `stated_gross` leer: der Beleg druckt keine. `verified_at` leer: nicht durchgesehen |
| `invoice_line` | Rechnungszeilen | `quantity` in Tausendsteln, `unit_price` in Millionstel Euro je `price_base_qty`, `line_net` in Cent, `vat` in Basispunkten |
| `document` | die eingelesene Datei, PDF oder XML | |
| `reading_*` | was der Scan gelesen hat | Kästen in Pixeln der gelesenen Seite; `confidence` von 0 bis 1; `turn` in Grad; `scale` als Verhältnis, `skew` und `settle` in Grad |

Löscht man eine Rechnung, bleiben `document` und `reading_*` bis zum nächsten
Start stehen, damit sich das Löschen zurücknehmen lässt; eine weitergegebene
Prüfung enthält sie nicht. `secure_delete` überschreibt den frei gewordenen
Platz.
