# Häufige Fragen

## Allgemein

??? frage "Lässt sich eine Änderung rückgängig machen?"

    Ja, mit **Strg+Z**, am Mac **⌘Z**, und zwar Schritt für Schritt, bis das
    Programm beendet wird. Das gilt auch für eine gelöschte Rechnung. Eine
    gelöschte Prüfung ist dagegen weg.

??? frage "Muss ich speichern?"

    Nein. Jede Änderung wird sofort in der Prüfung gespeichert. Nur ein
    gespeicherter Bericht als PDF bleibt so, wie er war, siehe
    [Als PDF speichern](bericht.md#als-pdf-speichern).

??? frage "Wo liegt eine Prüfung, und wie sichere ich sie?"

    Jede Prüfung ist eine einzelne Datei im Ordner **Dokumente** unter
    `Umsatzschätzung`, mit allen Rechnungen und Scans. Diese Datei zu sichern
    genügt, nur die [Regeln](regeln.md) liegen nicht darin.
    → [Prüfungen](pruefungen.md)

## Rechnungen

??? frage "Warum steht eine Rechnung auf „Durchsicht offen“?"

    Ein Scan wird nur übernommen, wenn Lieferant, Nummer und Datum gelesen sind,
    jede Zeile Menge × Einzelpreis ergibt und die Zeilen den Netto- und
    Bruttobetrag des Belegs ergeben. Etwas davon geht nicht auf. Die Rechnung
    öffnen: Die markierten Felder zeigen, was, und der Tooltip nennt den Grund.
    → [Durchsicht](rechnungen.md#durchsicht)

??? frage "Das Häkchen zum Bestätigen ist gesperrt."

    Eine Zeile ergibt nicht Menge × Einzelpreis, oder die Zeilen weichen vom
    Nettobetrag ab und der Beleg weist keinen lesbaren Nettobetrag aus. Der
    Betrag auf dem Beleg ist verbindlich: Die Zeilen werden an ihn angepasst,
    nicht umgekehrt.
    → [Durchsicht](rechnungen.md#durchsicht)

??? frage "Eine automatisch übernommene Rechnung hat einen falschen Lieferanten."

    „Automatisch“ heißt: Die Beträge gehen auf. Ob der Lieferant richtig gelesen
    ist, lässt sich nicht nachrechnen. Die Rechnung öffnen und den Namen
    korrigieren, danach ist sie noch einmal zu bestätigen.

??? frage "Im Namen einer Position steht noch „o 19 %“ oder eine Artikelnummer."

    Die Texterkennung hat einen Vermerk vom Beleg in die Bezeichnung gezogen.
    Vor dem Bestätigen entfernen, denn die Zuordnung lernt aus genau diesem
    Wortlaut.

??? frage "Viele Scans werden nicht übernommen."

    Meist liegt es an der Vorlage: zu geringe Auflösung, schräg eingezogen,
    abgeschnittener Summenblock, Fax statt Original.
    → [Scanqualität](rechnungen.md#scanqualitat)

??? frage "Eine Rechnung zählt nicht zum Wareneinsatz."

    Entweder ist sie noch nicht bestätigt, oder ihr Datum liegt außerhalb des
    Zeitraums der Prüfung. Sie bleibt dann in der Prüfung, geht aber nicht in die
    Kalkulation ein.
    → [Eckdaten](pruefung.md#eckdaten)

## Zuordnung

??? frage "Muss ich jede automatische Zuordnung prüfen?"

    Nein. Sinnvoll ist eine Stichprobe bei den größten Beträgen, denn dort fällt
    ein Fehler am meisten ins Gewicht. Was bestätigt oder korrigiert wird, gilt
    für jede weitere Rechnung mit diesem Artikel.
    → [Eine Zuordnung korrigieren](zuordnung.md#eine-zuordnung-korrigieren)

??? frage "Was sagt die Sicherheit in Prozent?"

    Wie oft ein solcher Vorschlag stimmt: Von hundert Vorschlägen mit 90 % treffen
    etwa neunzig zu. Sie misst nicht, wie ähnlich zwei Texte aussehen.
    → [Die Sicherheit](zuordnung.md#sicherheit)

??? frage "Was ist der Faktor, und warum fehlt er?"

    Der Inhalt eines Gebindes in der Einheit des Rezepts, etwa 3000 für einen
    3-kg-Block zu einem Rezept in Gramm. Steht die Packungsgröße nicht im
    Artikeltext und hat die Zutat kein Stückgewicht, kann das Programm ihn nicht
    ermitteln. Ohne Faktor lässt sich die Menge nicht in Portionen umrechnen.
    → [Eine Position zuordnen](zuordnung.md#eine-position-zuordnen)

??? frage "Wohin mit Pfand, Fracht, Reinigungsmitteln?"

    Auf eine Zutat der Kategorie **Kein Wareneinsatz**, Pfand auf **Pfand und
    Leergut**. Sie sind damit erledigt und gehen in keine Kalkulation ein.
    → [Kein Wareneinsatz](zuordnung.md#kein-wareneinsatz)

??? frage "Dieselbe Ware wird immer wieder zur Durchsicht vorgelegt."

    Bei der Zutat unter **Regeln → Zutaten** die Warenart ergänzen, etwa „Gouda“
    bei Schnittkäse.
    → [Warenarten pflegen](zuordnung.md#warenarten)

## Sortiment und Kalkulation

??? frage "Ein Produkt hat 0 Portionen."

    Die Kalkulation verteilt den Einkauf so auf die Produkte, dass möglichst
    wenig übrig bleibt. Ein Produkt geht leer aus, wenn eine seiner Zutaten gar
    nicht eingekauft wurde oder schon in anderen Produkten aufgeht. Rechts steht
    seine Rezeptur; ob jede Zutat darin im Einkauf vorkommt, zeigt die
    Zuordnung.
    → [Portionen](kalkulation.md#portionen)

??? frage "Ein Produkt hat Portionen, aber keinen Umsatz."

    Ihm fehlt der Preis. Es steht mit **Preis fehlt** im Sortiment, bis er
    nachgetragen ist.
    → [Sortiment](kalkulation.md#sortiment)

??? frage "Bei den Ertragsregeln steht „Ohne hinterlegten Abzug“."

    Für diese Zutat oder Kategorie gibt es keine Ertragsregel, es wird also
    nichts abgezogen, auch kein Schankverlust. Eine Regel wird unter **Regeln →
    Ertragsregeln** angelegt und gilt dann für jede Prüfung.
    → [Ertragsregeln](regeln.md#ertragsregeln)

??? frage "Kann ich einen Schwund von 8 % direkt eintragen?"

    Nein. Jeder Abzug ist eine benannte Regel, damit der Bericht nicht nur den
    Satz nennt, sondern auch, warum er gilt. Für einen Betrieb mit belegbar
    höherem Verlust eine eigene Regel anlegen und in der Prüfung wählen.
    → [Ertragsregeln](kalkulation.md#ertragsregeln)

??? frage "Der Betrieb schenkt größere Portionen aus als im Rezept."

    Das Rezept für diese Prüfung anpassen. Der Katalog bleibt unverändert, der
    Bericht führt das Rezept so, wie es gerechnet ist.
    → [Rezeptur anpassen](kalkulation.md#rezeptur)

??? frage "Eine Ware bringt keinen Umsatz, wird aber geschätzt."

    Unter **Übrige Einkäufe** mit dem × am Ende der Zeile aus der Schätzung
    nehmen. Das gilt für die ganze Zutat, bei Sammelzutaten wie „Reinigung und
    Hygiene“ also für alles, was ihr zugeordnet ist. Das ↺ nimmt es zurück.
    → [Übrige Einkäufe](kalkulation.md#nicht-berucksichtigt)

??? frage "Eine Ware fehlt unter „Übrige Einkäufe“."

    Dann steht ihre Zutat in einem Rezept des Sortiments und geht über Portionen
    in den Umsatz ein, nicht über den Aufschlagsatz. Welche Produkte sie
    verwenden, steht unter **Regeln → Produkte**.
    → [Produkte](regeln.md#produkte)

??? frage "Pfand und Leergut gleichen sich nicht aus."

    Über ein Jahr müssen sie das nicht. Beide zählen nicht zu den Einkäufen und
    verändern den kalkulierten Umsatz nicht; die Summen stehen nur zur
    Information unter der Liste.

??? frage "Die Lagerbestände zu Beginn und Ende waren sehr verschieden."

    Unter **Prüfung → Bestand** Anfangs- und Endbestand je Zutat eintragen. Das
    lohnt sich bei Getränken, Tiefkühlware und Wein, nicht bei Frischware.
    → [Bestand](pruefung.md#bestand)

## Bericht und Weitergabe

??? frage "Das PDF zeigt einen alten Stand."

    Ein gespeichertes PDF ändert sich nicht mit. Nach jeder Änderung an
    Rechnungen, Zuordnung oder Preisen neu speichern.
    → [Bericht](bericht.md#als-pdf-speichern)

??? frage "Die Hinweise aus der Vorschau fehlen im PDF."

    Absichtlich. Sie sind offene Punkte für den Prüfer, nicht Teil des Ergebnisses.
    Ein Bericht ohne Hinweise hat keine offenen Punkte mehr.
    → [Aufbau](bericht.md#aufbau)

??? frage "Der Richtsatzvergleich fehlt im Bericht."

    In der Prüfung ist keine Gewerbekennzahl eingetragen.
    → [Eckdaten](pruefung.md#eckdaten)

??? frage "Ein Kollege sieht in meiner Prüfung andere Zahlen."

    Die Prüfung nimmt Rechnungen, Preise und Entscheidungen mit, aber nicht die
    Regeln: Zutaten, Produkte, bestätigte Zuordnungen und Ertragsregeln liegen auf
    dem eigenen Rechner. Auf einem anderen Rechner ordnet das Programm neu zu,
    was es dort nicht kennt, und was offen bleibt, fehlt in der Kalkulation.

    In verwalteten Installationen liegen die Regeln oft für alle gemeinsam auf
    einer Freigabe, unter Windows per Gruppenrichtlinie **Gemeinsame
    Regel-Datenbank**. Dann rechnen alle mit denselben Regeln, und was einer
    bestätigt, gilt beim nächsten Start auch für die anderen. Ob das so
    eingerichtet ist, weiß die IT.
    → [Regeln gehen nicht mit](import-export.md#regeln-gehen-nicht-mit),
    [Verwaltete Installation](verwaltung.md#regel-datenbank)
