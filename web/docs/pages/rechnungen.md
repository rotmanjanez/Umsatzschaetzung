# Rechnungen importieren

Die Datengrundlage einer Prüfung sind die Eingangsrechnungen des Betriebs. Das
Programm nimmt sie als E-Rechnung oder als Scan entgegen. Eine E-Rechnung lässt
sich maschinell fehlerfrei lesen. Ein Scan ist zunächst nur ein Bild; das
Programm liest die Daten aus dem Bild heraus. Gehen die gelesenen Beträge
vollständig auf, übernimmt es den Scan direkt in die Prüfung. Sonst wartet die
Rechnung auf eine [Durchsicht](#durchsicht), in der Sie die gelesenen Felder
korrigieren.

## Formate

| Format | Dateien | Übernahme |
|---|---|---|
| XRechnung (UBL oder CII) | `.xml` | direkt |
| ZUGFeRD, Factur-X | `.pdf` mit eingebettetem XML | direkt, das XML zählt, nicht das Bild |
| Scan | `.pdf`, `.png`, `.jpg`, `.tif` | Texterkennung, dann Prüfung |

## Hinzufügen

Rechnungen fügen Sie über die Schaltfläche **Rechnungen hinzufügen** oben
rechts über der Liste hinzu oder ziehen die Dateien in die Liste.
Der Import läuft im Hintergrund und zeigt den Fortschritt und die verbleibende
Zeit. Diese ist anfangs eine Schätzung und wird mit jeder gelesenen Datei
genauer, denn ein Scan braucht ein Vielfaches einer E-Rechnung. Am Ende steht,
wie viele Rechnungen übernommen wurden und wie viele zur Durchsicht warten.

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
korrigieren, Zeilen hinzufügen oder entfernen, dann mit dem Häkchen oben rechts
(**Bestätigen**) als durchgesehen markieren; das Fenster schließt sich.

Jede Änderung wird sofort gespeichert. Eine schon durchgesehene Rechnung, die
noch einmal geändert wird, ist danach wieder offen und will erneut bestätigt
werden.

Das Häkchen ist gesperrt, solange eine Zeile nicht aufgeht oder die Summe der
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
