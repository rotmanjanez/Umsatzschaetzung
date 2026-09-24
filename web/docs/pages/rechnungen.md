# Rechnungen importieren

Die Datengrundlage einer Prüfung sind die Eingangsrechnungen des Betriebs. Das Programm
nimmt sie als E-Rechnung oder als Scan entgegen. E-Rechnungen sind maschinell fehlerfrei lesbar. Ein Scan ist allerdings zunächst
nur ein Bild; das Programm liest den Scan ein und versucht die Daten aus dem Bild herauszulesen.
In einigen Fällen kann das Program so Scans direkt in die Prüfung aufnehmen.
Das klappt leider nicht immer. Wenn das Program den Inhalt der Rechnung nicht fehlerfrei erkennt, wird eine Rechnung nicht direkt übernommen und muss erst von dem Benutzer korrigiert werden.

## Formate

| Format | Dateien | Übernahme |
|---|---|---|
| XRechnung (UBL oder CII) | `.xml` | direkt |
| ZUGFeRD, Factur-X | `.pdf` mit eingebettetem XML | direkt, das XML zählt, nicht das Bild |
| Scan | `.pdf`, `.png`, `.jpg`, `.tif` | Texterkennung, dann Prüfung |

## Hinzufügen

Sie können neue Rechnungen über then "+" knopf hinzufügen.
Der Import läuft im Hintergrund und zeigt den Fortschritt.
Die abschaezung wie lange ein Import benoetigt kann sich 
Am Ende steht, wie viele Rechnungen übernommen wurden
und wie viele zur Durchsicht warten.

! ""Sie koennen alternativ auch klick and drag verwenden""

Eine Datei ist eine Rechnung. Mehrseitige Rechnungen als eine PDF mit allen
Seiten in Reihenfolge; ein Bild ist immer genau eine Seite.

## Was aus einem Scan gelesen wird

- Lieferant, Rechnungsnummer, Datum
- je Position: Menge, Einheit, Bezeichnung, Artikelnummer, Einzelpreis,
  Nettobetrag, Umsatzsteuersatz
- die auf dem Beleg ausgewiesene Netto- und Bruttosumme

Die ausgewiesenen Summen sind der Maßstab. Nur an ihnen lässt sich erkennen,
ob die Lesung eine Zeile übersehen hat.

## Wann ein Scan ohne Durchsicht übernommen wird

Nur, wenn niemand ihn ansehen müsste:

- Lieferant, Rechnungsnummer und Datum sind gelesen
- mindestens eine Position, und jede ergibt Menge × Einzelpreis
- die Summe der Positionen stimmt mit dem ausgewiesenen Nettobetrag überein
- Netto zuzüglich Umsatzsteuer ergibt den ausgewiesenen Bruttobetrag

Rundungsdifferenzen bis 0,5 % gelten als Übereinstimmung. Trifft eine
Bedingung nicht zu, erhält die Rechnung den Status **Durchsicht offen** und zählt
noch nicht zum Wareneinsatz.

## Durchsicht { #durchsicht }

Die Rechnung öffnen. Oben die gelesenen Felder, darunter der Beleg. Jede
Abweichung ist an allen Feldern markiert, die in die Prüfung eingehen, denn
jedes davon kann falsch gelesen sein, und nennt, was nicht zusammenpasst, etwa
„Menge × Einzelpreis ergibt 12,40 €, Gesamtpreis ist 21,40 €“. Felder
korrigieren, Zeilen hinzufügen oder entfernen, dann **Bestätigen**.

Bestätigen ist gesperrt, solange eine Zeile nicht aufgeht oder die Summe der
Positionen vom Nettobetrag abweicht und der Beleg keinen Nettobetrag lesbar
ausweist. Ein ausgewiesener Betrag ist verbindlich; die Zeilen müssen zu ihm
passen, nicht umgekehrt.

Nach dem Bestätigen trägt die Rechnung **Durchgesehen am …**, eine
automatisch übernommene **Ohne Durchsicht übernommen am …**. Der Bericht nennt beides.

## Scanqualität

Die Lesung ist so gut wie der Scan. Was der Scanner nicht mitgebracht hat,
lässt sich nachträglich nicht herstellen.

- **300 dpi.** PDFs werden mit 300 dpi gelesen, Bilder so, wie sie sind.
  Weniger Auflösung kostet zuerst die Ziffern: eine 3 wird zur 8, ein Komma
  verschwindet. Mehr als 4000 Pixel Kantenlänge bringen nichts, das Bild wird
  darauf verkleinert.
- **Alles im Bild.** Kopf, Positionen und Summenblock vollständig auf der
  Seite. Kein abgeschnittener Rand, keine Hand, keine Büroklammer, kein Knick
  über einem Betrag. Fehlt die Summe, fehlt der Maßstab.
- **Gerade und flach.** Schräg eingezogene oder gewellte Seiten verschieben
  Spalten gegeneinander; Mengen landen dann bei der falschen Position.
- **Original statt Fax.** Faxkopien lesen sich am schlechtesten von allen
  Vorlagen. Liegt das Original vor, dieses scannen.
- **Foto nur im Notfall.** Wenn, dann von oben, plan aufliegend, gleichmäßig
  beleuchtet, ohne Schatten über dem Text.
- **Keine Nachbearbeitung.** Schärfen, Kontrastfilter und Umwandlung in
  Schwarzweiß helfen nicht und schaden bei Fotos.
