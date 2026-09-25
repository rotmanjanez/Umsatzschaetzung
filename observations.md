# Beobachtungen zur Beispielprüfung (http://127.0.0.1:8000/guide/)

Durchgespielt am 2026-09-24. Die laufende macOS-App ließ sich nicht fernsteuern (keine
Bedienungshilfen-Freigabe für osascript), deshalb lief jeder Schritt über eine Kopie von
`tools/headless` gegen die echte Oberfläche, mit dem mitgelieferten Regelsatz und leerem Datenstand,
also wie bei einer Erstinstallation.

## Start (index)

- 18 der 106 Dateien sind PDFs; „alle als Scan“ stimmt nur, wenn es gescannte PDFs sind.
- „Beide Dateien sind an der Stelle verlinkt“ – gemeint sind das ZIP mit den Rechnungen und die
  Sortiments-CSV. „Beide Dateien“ ist missverständlich, weil davor von 106 Rechnungen (also 106
  Dateien) die Rede ist.
- Fassbier-Rechnung: 600 l ergeben 1.200 Gläser zu 0,5 l, „rund 1.100“ unterstellt ~8 %
  Schankverlust. Das stimmt vermutlich, aber der Umsatz aus dem Beispiel („Mal 4,60 € je Glas“) wird
  nicht ausgerechnet. Dabei ist das die erste Zahl, an der ein Leser prüfen könnte, ob er es
  verstanden hat (≈ 5.060 € brutto, ≈ 4.252 € netto). Außerdem ist nicht klar, ob 4,60 € brutto oder
  netto gemeint sind; der erklärte Umsatz ist netto.
- „Dafür braucht es fünf Dinge, und genau das sind die fünf Reiter“ – laut „Prüfung anlegen“ gibt
  es sechs Reiter (Prüfung + 1–5), das Bild dort heißt auch „mit den sechs Reitern“.
- Der Hinweis „Mitmachen“ verspricht, dass am Ende *jeder* Seite steht, woran man den Erfolg
  erkennt. Auf den Installationsseiten, bei „Kalkulation“ (nur für den Abschnitt Übrige Einkäufe,
  nicht für das Ergebnis) und bei „Danach“ fehlt das.
- Der ZIP-Download ist ~450 MB groß (entpackt 432 MB). Die Anleitung erwähnt die Größe nirgends;
  bei langsamer Leitung eine eigene Wartezeit.

## Installation (macOS)

- Die App läuft bereits (`dist/osx-arm64/Umsatzschätzung.app`), deshalb nur gelesen.
- Kein Wort zu Gatekeeper („kann nicht geöffnet werden, da der Entwickler nicht verifiziert werden
  kann“). Wenn die DMG nicht notarisiert ist, bleibt ein Neuling genau hier hängen.
- „wer schon über die Startseite geladen hat“ – welche Startseite? Die Anleitung selbst beginnt
  nicht mit einem Download.
- Kein „Geschafft, wenn …“ (z. B. „die leere Liste der Prüfungen erscheint“).

## Rechnungen holen

- Die ZIP-Datei enthält die 106 Dateien direkt, ohne Ordner darin. Die Anleitung sagt „Es entsteht
  ein Ordner `beispiel-rechnungen-2025`“. Das stimmt mit Doppelklick auf dem Mac und mit „Alle
  extrahieren“ unter Windows (beide benennen den Ordner nach dem ZIP), aber mit `unzip` oder
  manchen Entpackern landen die 106 Dateien lose im Download-Ordner.

## Prüfung anlegen

- „**Neue Prüfung** öffnet das Formular“ – im Programm gibt es keinen Knopf mit dieser Aufschrift,
  nur ein blaues „+“ mit Tooltip „Neue Prüfung“ (die Überschrift „Neue Prüfung“ erscheint erst im
  Formular). Besser „Das Plus rechts oben (**Neue Prüfung**)“, wie es die Rechnungsseite mit
  „Das Symbol oben rechts (**Rechnungen hinzufügen**)“ macht.
- „Die Gewerbekennzahl bleibt leer“ ohne Begründung. Der Platzhalter „z. B. 56101.0
  (Richtsatzsammlung)“ lässt vermuten, dass sie für einen Richtsatzvergleich gebraucht wird. Ein
  Halbsatz dazu, was ohne sie fehlt, oder warum sie im Beispiel egal ist, würde helfen.
- Die Felder im Formular heißen „Zeitraum von *“/„Zeitraum bis *“, im Reiter Prüfung danach
  „Zeitraum“ → „Von *“/„Bis *“. Im Formular kommt Steuernummer vor PaB-Nr., unter Eckdaten ist es
  umgekehrt. Name, Steuernummer und PaB-Nr. sind beim Anlegen Pflicht (*), unter Eckdaten nicht mehr
  markiert. Das sind App-Inkonsistenzen, keine Fehler der Anleitung.
- Der Reiter Prüfung zeigt oben eine Karte „Nächster Schritt: Rechnungen“ mit Pfeil-Knopf. Die
  Anleitung erwähnt sie nicht, obwohl sie der naheliegende Weg zum nächsten Schritt ist.
- „Die übrigen Abschnitte des Reiters bleiben für das Beispiel leer“ – das sind Bestand und
  Eckdaten, und die Eckdaten sind *nicht* leer (dort stehen die Angaben aus dem Formular). Besser:
  „Bestand bleibt leer“.
- Das Rechnungssymbol (Tooltip „Rechnungen hinzufügen“) ist ein Pfeil nach unten in eine Ablage,
  also das übliche *Download*-Symbol. In der Prüfungsliste steht dasselbe Symbol für
  „Importieren“. Wer nur „das Symbol oben rechts“ liest, sucht eher ein Plus.

## 1. Rechnungen importieren

- Der Text nennt „Metzgerei Hofmann“ und „Weingut Sommer“. In der Liste stehen sie in Großbuchstaben
  (`METZGEREI HOFMANN`, `WEINGUT SOMMER`), so wie sie vom Beleg gelesen werden. Kein Problem, aber
  wer mit Strg+F oder dem Filter sucht, sollte das wissen.
- Mehrere Rechnungen sind **„Automatisch“ mit verlesenem Lieferanten** übernommen worden:
  `& Barbedarf` (RE2501944), `Brückner Spirituosen & GmbH` (RE2503135), `FR OSTW ER` (TK25-1147,
  eigentlich Frostwerk). Die Anleitung sagt nur „Geht die Rechnung auf, wird sie übernommen“ und
  erwähnt nicht, dass bei automatischen Rechnungen nur die Beträge geprüft sind, nicht der
  Lieferant. Die verlesenen Namen tauchen später in der Zuordnung (Spalte Lieferant) wieder auf.
  Im Korrekturteil wird dann gerade beim Tiefkühllieferanten der Lieferant von Hand eingetragen,
  während seine andere Rechnung als „FR OSTW ER“ stehen bleibt.
- „Das Symbol oben rechts (**Rechnungen hinzufügen**) öffnet die Dateiauswahl“ – nach dem Import
  bleibt das Symbol ein Download-Pfeil (siehe oben). „Genauso geht es, die Dateien … auf die
  gestrichelte Fläche zu ziehen“: Nach dem Import ist die gestrichelte Fläche weg. Für einen
  späteren Nachimport ist unklar, wohin man zieht.

## 1. Rechnungen korrigieren

### WS-2025-346 (fehlende Einheit)
- „auf dem Beleg ist dieselbe Stelle orange umrandet“ (allgemeiner Absatz oben): Bei der fehlenden
  Einheit ist auf dem Beleg **nichts** orange umrandet, weil es für einen fehlenden Wert keine Stelle
  gibt. Bei 2025/0561 stimmt es. Der allgemeine Satz sollte das einschränken.
- „Das Häkchen oben rechts“: Ganz rechts oben sitzt das „?“, links vom Häkchen das Export-Symbol.
  Das Häkchen ist das große blaue Feld, trotzdem „rechts oben, blau“ statt „oben rechts“.

### 2025/0561 (verlesene Ziffer)
- Bemerkenswert: „Aus Positionen“ Netto zeigt schon *vor* der Korrektur 433,43 € = Beleg, weil die
  Zeilenbeträge summiert werden, nicht Menge × Preis. Das ist richtig, verwirrt aber, wenn man die
  Summentabelle als Kontrolle liest.

### TK25-1583 (zerrissene Zeile)
- „Bei den beiden Hälften ist nur die fehlende Einheit markiert“ – nicht ganz: Von Anfang an sind
  auch bei **allen** Zeilen (auch bei den Hälften) die USt-Zellen markiert. Die Anleitung erwähnt
  die Steuermarkierung erst nach dem Zusammenführen („Markiert sind *jetzt nur noch* …“), als wäre
  sie erst dann aufgefallen.
- **Die gelesenen Positionsnamen enthalten den Steuervermerk als Müll**, den die Anleitung nicht
  erwähnt: `Blattspinat portioniert, 10 kg 7 %`, `Alaska-Seelachsfilet paniert, 125 g O 7 %`,
  `Seelachsfilet ohne Haut, 5 kg Block us 5`, `Beerenmischung 4-Frucht, 4 x 1 kg 4 o 7 %`,
  `Trockeneis Pellets, 10 kg Gebinde Art. -Nr. TK-9001. · MwSt. 19 %` und die 0,00-€-Zeile
  `Thermobox Leihgebühr je Woche o 19 %`. Die Anleitung empfiehlt, den Namen „am einfachsten aus
  der Zeile mit 0,00 € kopieren“. Genau dann übernimmt man das „o 19 %“. Entweder den Tipp streichen
  (Namen abtippen, wie im Codeblock angegeben) oder sagen, dass das Anhängsel weg muss.
- „Bei den Pommes hat der Scan die Packungsangabe abgeschnitten“ (Seite Zuordnung): Auf dem Scan
  steht `blanchiert, 4 x 2,5 kg` vollständig. Abgeschnitten hat die *Erkennung*, nicht der Scan.
- Die USt-Zellen zeigen nach dem Eintippen nur `7` bzw. `19` ohne „%“, bis die Rechnung neu geöffnet
  wird. Danach stehen sie als „7 %“ da. Kosmetisch.

## 2. Zuordnung

Ergebnis am Ende: `1 offen, 153 automatisch, 4 manuell`, nur die Petersilie offen. Die Anleitung
erwartet `1 offen, **149** automatisch, 4 manuell`. **„Geschafft, wenn …“ ist damit formal nicht
erfüllt.**

- **Warum 153 statt 149:** Die Liste führt einen Artikel „einmal, auch wenn er auf 25 Rechnungen
  vorkommt“, aber getrennt *je Lieferant*. Der verlesene Lieferant `& Barbedarf` (RE2501944, siehe
  Import) erzeugt genau 4 zusätzliche Zeilen (Fränkischer Doppelkorn, Jägermeister, Moskovskaya,
  Gorbatschow), die es bei korrekt gelesenem Lieferanten nicht gäbe. 153 − 4 = 149. Die
  OCR-Ergebnisse sind also nicht auf jedem Rechner gleich, oder die Anleitungszahlen stammen aus
  einem anderen Modellstand. Auch `Brückner Spirituosen & GmbH` wirkt wie eine Variante, dort fällt
  es aber nicht auf. Entweder die Zahl weicher formulieren („rund 150 automatisch“) oder in der
  Anleitung erwähnen, dass verlesene Lieferanten Artikel doppelt erscheinen lassen.
- **Start: 6 offen statt 5.** Zusätzlich offen ist `Pfand Fass KEG 30/50 l` (FRANKENBRÄU). Die
  Anleitung erwähnt diese Position nicht. Öffnet man sie, steht rechts „Zugeordnet: Pfand und
  Leergut · automatisch“, **und allein durch das Anklicken springt die Bilanz auf „5 offen, 153
  automatisch“**, ohne dass man etwas bestätigt hat. Das wirkt wie ein App-Fehler: Der Status in der
  Liste passt nicht zur Detailansicht und ändert sich beim bloßen Ansehen.
- Domina: Die Sicherheit für Rotwein ist **52 %** (Text: 51 %). Mit „gleich dahinter“ ist die
  Aussage trotzdem richtig.
- Die Einheit heißt in der App „Flasche“ (Singular auch bei 158). Der Text sagt „158 Flaschen“.
  Kosmetisch.
- Der „grüne Hinweis rechts oben“ nach **Zuordnen** war auf meinem Bild ~1 s danach nicht (mehr) zu
  sehen, rechts stand nur „Keine Position gewählt“. Vielleicht verschwindet er schnell. Dann ist
  „bestätigt ein grüner Hinweis“ als Erfolgskriterium wackelig.
- „Bei zweien ist die Zutat klar, … **Exakter Treffer**, aber der Faktor fehlt“ – tatsächlich sind
  es **drei**: Pommes frites, Sahne *und* Frische Kräuter (Petersilie) sind „Exakter Treffer“ mit
  leerem Pflichtfaktor („Faktor *“). Der Satz „Aus ‚Karton‘ oder ‚Bund‘“ nennt sogar die Einheit
  der Petersilie mit. Die Frühkartoffeln, die in der Tabelle zu diesen zwei gehören könnten, sind
  **kein** exakter Treffer, sondern „Kartoffeln × 12,5 kg“ mit 73 %, Faktor schon befüllt.
- Kaffeesahne „10 × 7,5 g 240er“: Die Anleitung erklärt „240 Portionen zu 7,5 g sind rund 1,8 l“.
  Was dann das „10 ×“ im Artikeltext heißt (10 × 24?), bleibt offen. Außerdem sind 7,5 **g** × 240 =
  1.800 **g**, und das Rezept rechnet in **ml**. Die Gleichsetzung g ≈ ml sollte einmal erwähnt
  werden, damit niemand die Dichte umrechnet.
- Der Filter über der Liste hilft sehr beim Finden der Positionen (die Liste hat 158 Zeilen). Die
  Anleitung erwähnt ihn auf dieser Seite nicht.

## 3. Sortiment

- Die leere Seite zeigt rechts eine Liste **„Vorschläge“** mit „Ins Sortiment“/„Führt der Betrieb
  nicht“ je Produkt. Die Anleitung erwähnt sie nicht. Der erste Vorschlag ist **„Schnitzel,
  zugekauft paniert, mit Pommes“**. Er kommt aus der falschen automatischen Zuordnung des
  Schweineschnitzels, die erst in Schritt 4 korrigiert wird. Wer den Vorschlag hier annimmt, weil
  das Programm ihn anbietet, bekommt ein falsches Produkt. Ein Satz „Die Vorschläge rechts für das
  Beispiel ignorieren“ würde helfen, oder die Schnitzel-Korrektur gehört vor das Sortiment.
- Auch nach dem CSV-Import stehen noch Vorschläge da (Schnitzel zugekauft, Pommes Beilage,
  Kartoffeln lose, Tomaten lose, Edamame). Ein Neuling fragt sich, ob die noch dazu sollen.
- CSV-Import: Nach dem Import sah ich **keine** Rückmeldung (wie viele übernommen, übersprungen). Der KI-Tipp verspricht aber, dass der Import
  übersprungene Namen „danach meldet“. Ob die Meldung nur bei Fehlern erscheint, sagt die Anleitung
  nicht.
- Die CSV setzt alle Speisen auf 19 %. Ein Satz in der Anleitung, *warum* das für 2025 stimmt (die
  7 % für Speisen im Lokal galten bis Ende 2023), würde Rückfragen vermeiden.

## 4. Kalkulation

### Übrige Einkäufe
- Beim Öffnen gibt es nur **eine** Liste („Nicht Teil der Ermittlung des Aufschlagsatzes“,
  8.534,09 €, 12,79 % der Einkäufe, geschätzter Umsatz 38.675,22 €). Die zweite Liste „Nicht in der
  Umsatzschätzung“ erscheint erst, wenn etwas darin steht. Die Anleitung spricht von „zwei Listen“.
- Darunter steht „Nicht in den Einkäufen: Pfand berechnet +7.674,40 €, Leergut gutgeschrieben
  −5.351,80 €“. Die Anleitung sagt „Pfand und Leergut … gleichen sich über die Zeit aus“. Hier
  bleiben **2.322,60 €** Pfand offen, das ist nicht „ausgeglichen“. Entweder die Aussage relativieren
  (über *ein* Jahr nicht zwingend) oder erklären, warum der Rest egal ist.
- Schnitzel: Die Netto-Spalte ist zu schmal, der Betrag wird als `3.493,79…` abgeschnitten.
- Einfacher als beschrieben: In der Detailansicht steht `Schweinefleisch` schon als Vorschlag mit
  85 % und dem Etikett „In Rezeptur“ (Tooltip: Mittagstisch, Schnitzel mit Pommes). Ein Klick darauf
  und **Bestätigen** hätte gereicht. Die Anleitung geht den Umweg über „Manuell zuordnen“.
- Der Knopf in der Detailansicht heißt bei automatischen Zuordnungen **„Bestätigen“**, auf der
  Zuordnungsseite bei offenen **„Zuordnen“**. Die Anleitung nennt nur „Zuordnen“.
- **„Frittieröl pflanzlich 10 l“ steht nicht in der Liste und lässt sich deshalb nicht wie
  beschrieben markieren.** Es ist „Pflanzenöl × 10 l“ zugeordnet, und Pflanzenöl steht im Rezept von
  *Schnitzel mit Pommes* und *Rindersteak mit Pommes* (20 ml je Portion). Damit ist es Teil der
  Rezeptkalkulation und kein „übriger Einkauf“. Der Anleitungsschritt passt nicht mehr zum Katalog
  (oder das Katalogrezept ist neu). Die Begründung „Mit dem Frittieröl geht alles, was als
  Pflanzenöl zugeordnet ist“ stimmt dann auch nicht.
- Die drei übrigen Markierungen gehen, aber jeweils eine **ganze Kategorie** wandert mit, nicht
  nur „der zweite Klarspüler“:
  - Servietten → „Verpackung und Einweg“: auch Müllbeutel, Müllsäcke, Filterpapier.
  - CO2 → „Ausstattung und Bedarf“: auch **Trockeneis** und das **Ausschankmaß**.
  - Klarspüler → „Reinigung und Hygiene“: auch Handtuchrollen, Handspülmittel, Nitrilhandschuhe,
    Spülmaschinenreiniger, Fettlöser, Reinigungstabs.
  Das Ergebnis ist sachlich richtig, aber die Anleitung sagt „Die Festlegung gilt für die Zutat“.
  Bei Sammelzutaten wie „Reinigung und Hygiene“ überrascht es trotzdem, dass 20 Zeilen
  verschwinden. Der Tooltip nennt die Kategorie („„Reinigung und Hygiene“ bringt in diesem Betrieb
  keinen Umsatz“), die Anleitung sollte das auch tun.
- Weiter in der Liste und von der Anleitung nicht erwähnt: `Versandkostenpauschale` 44,70 € und
  `Thermobox Leihgebühr je Woche` 21,00 € (beide „Dienstleistung und Fracht“). Auf sie wird ein
  Umsatz geschätzt. Das widerspricht dem Geist von „Was keinen Umsatz bringt“.
- OCR-Varianten erzeugen Doppelzeilen: `Klarspüler 10 l` / `Klarspüler 101`,
  `Spülmaschinen-Reiniger flüssig 12 kg (ST[N 425:5552010?2`, `Reinigungstabs Siebtråger`,
  `Alaska-Seelachsfilet paniert, 125 g e`, `Rahmspinat portioniert, 10 kg N a`. Frittieröl gibt es
  unter drei Lieferantenschreibweisen (`GastroMarkt C+C`, `… Großhandel GmbH & Co. KG`,
  `GastroMarkt Ç+C …`).
- „Geschafft, wenn … Frittieröl, Servietten, CO2 und Klarspüler unter **Nicht in der
  Umsatzschätzung** stehen“ – für Frittieröl nicht erreichbar.
- Nebenbei: Der Filter der Zuordnungsliste ist unscharf. `Fritt` findet auch Ausschankmaß, Erbsen,
  Rahmspinat, Handspülmittel, Speisesalz usw.

### Ertragsregeln
- Alle 11 Kategorien stehen auf **„Ohne hinterlegten Abzug“**, auch **Bier vom Fass**. Die
  Anleitung beschreibt die Seite mit „Schankverlust, Eigenverbrauch, Personalverpflegung und
  Freirunden“ und sagt „Für das Beispiel bleiben die Standardregeln“. Die Startseite rechnet
  ausdrücklich mit Schankverlust („rund 1.100 Gläser“ aus 600 l). Im Beispiel wird also **kein**
  Schankverlust abgezogen, und die Einleitung verspricht etwas anderes. Außerdem heißt die Seite
  „Nach Kategorie“; von Regeln „je Zutat“ ist nichts zu sehen.
- Laut Anleitung stehen hier nur Kategorien, „für die es mehr als eine Regel gibt“. Welche
  Alternativen es gibt, sieht man erst im Aufklappmenü. Ein Satz, was man dort erwartet, fehlt.

### Rezeptur anpassen (nur gelesen, nichts geändert)
- Die Knöpfe „Für diese Prüfung anpassen“ und „Im Katalog bearbeiten“ sind reine Symbole mit
  Tooltip. Die Anleitung schreibt sie wie beschriftete Knöpfe.
- Schnitzel mit Pommes: 180 g Schweinefleisch, 200 g Pommes frites, 20 ml Pflanzenöl. Kein
  Paniermehl, obwohl die Anleitung betont, dass das Gasthaus selbst paniert. Die Semmelbrösel
  (164,43 € u. a.) landen dadurch in „Nicht Teil der Ermittlung“. Das passt nicht ganz zur
  Geschichte.

### Portionen
- **Vier Sortimentsprodukte haben 0 Portionen / 0,00 € Umsatz**: Weißweinschorle, Käsespätzle,
  Glühwein, Cheeseburger. Die Anleitung erwähnt das nicht. Ein Neuling hält es für einen Fehler
  (vermutlich fehlen passende Einkäufe oder Zutaten sind anderen Produkten zugeteilt).

### Ergebnis
- Umsatz vor BP 196.418,00 €, **nach BP 291.717,37 €, Differenz +95.299,37 € (+48,5 %)**. Die
  Anleitung nennt **keinen** Zielwert und hat für die Kalkulationsseite auch kein „Geschafft,
  wenn …“ für das Ergebnis. Man kann also nicht prüfen, ob man richtig gerechnet hat. Gerade hier
  wäre die erwartete Zahl am wichtigsten (auch wegen der OCR-Abweichungen oben).
- Zusammenfassung: Wareneinsatz 61.674,40 €, davon nicht zugeteilte Ware 8.776,89 €, „Nicht Teil
  der Ermittlung“ 3.889,10 €, geschätzter Umsatz 60.371,43 €, Aufschlag gesamt 337,34 %.
  - „Einsatz mit geschätztem Umsatz“ **12.665,71 €**, während nicht zugeteilte Ware + „Nicht Teil
    der Ermittlung“ = 8.776,89 + 3.889,10 = **12.665,99 €**. Es fehlen **0,28 €**. Entweder eine
    Rundung oder ein kleiner Posten, der irgendwo anders hängt.
  - Die Einrückung legt nahe, dass „davon nicht zugeteilte Ware“ ein Teil des Wareneinsatzes ist.
    „Einsatz mit geschätztem Umsatz“ (12.665,71) ist aber größer als die 8.776,89 und enthält
    Posten, die *nicht* im Wareneinsatz stehen (die 3.889,10 € sind schon aus 61.674,40
    herausgerechnet). Wie die Zeilen zusammenhängen, erklären weder Oberfläche noch Anleitung.
  - Geschätzter Umsatz 60.371,43 € ÷ 12.665,71 € ≈ Faktor 4,77, also Aufschlag ~377 %. Das ist der
    *Speisen*-Satz, nicht der Gesamtsatz 337,34 %. Welcher Satz auf welche Ware angewandt wird,
    ist nirgends erklärt.

## 5. Bericht

- Headless gibt es keine Web-Komponente („Berichtsvorschau nicht verfügbar, Web-Komponente konnte
  nicht geladen werden“, PDF-Ausgabe ebenso). Deshalb habe ich das Berichts-HTML über denselben
  Dienstaufruf geholt und mit Chrome als PDF gedruckt. Inhaltlich identisch, das Layout der echten
  PDF kann leicht abweichen. Den Dialog „Als PDF speichern“ selbst habe ich **nicht** getestet.
- Die Gliederung passt nur teilweise: Die Anleitung nennt 1 Umsätze, 2 Rohgewinnaufschlag,
  3 Anhang (Eingangspositionen, Rezepturen, Warenfluss). Der Bericht hat 1, 2 und **Anhang A–E**:
  A Eingangspositionen, B Rezepturen, C Warenfluss und Ausbeute, **D Umsatz über den
  Rohgewinnaufschlagsatz, E Kennzahlen**. D und E fehlen in der Beschreibung, obwohl der Bericht
  selbst auf Anhang D verweist.
- Kopf: **„Rechnungen 104“**, importiert und bestätigt sind aber 106. Die zwei fehlenden sind
  RG-2025114 und RG-2025987 (HygienePlus Gastro-Bedarf). Sie enthalten nur „Reinigung und
  Hygiene“, und das wurde in Schritt 4 als „kein Umsatz“ markiert, deshalb fallen sie aus
  Anhang A. Folgerichtig, aber ohne Erklärung sieht es aus, als wären zwei Rechnungen verloren
  gegangen. (Nebenbei: bei RG-2025114 ist der Lieferant als `HygienePius` verlesen und trotzdem
  „Automatisch“.)
- „Kalkulation je Produkt“ lässt die vier Produkte mit 0 Portionen (Weißweinschorle, Käsespätzle,
  Glühwein, Cheeseburger) kommentarlos weg. Sie stehen aber im Sortiment. Für die Akte wäre ein
  Hinweis besser, dass sie mangels Einkauf keinen Umsatz tragen.
- Die Prozentwerte sind uneinheitlich formatiert: `1.587,4 %`, `431,6 %`, `439,8 %`, `149,3 %`
  neben `464,61 %`. Die Nachkomma-Null fällt weg. Im Bericht fürs Amt wäre durchgängig zweistellig
  besser. Dasselbe gilt in der App unter Portionen.
- Anhang D führt winzige Restposten wie „Röstkaffee 1 g … 0,02 €“ oder „Erfrischungsgetränk
  160 ml … 0,56 €“ einzeln auf. Rundungsrauschen, das die Tabelle aufbläht.
- Anhang E bestätigt die Zahlen aus „Ergebnis“, einschließlich der unklaren Beziehung zwischen
  Wareneinsatz 61.674,40 €, Nicht in der Umsatzschätzung 1.151,20 € und Einsatz mit geschätztem
  Umsatz 12.665,71 € (siehe oben, 0,28 € Differenz).
- „Geschafft, wenn … das PDF gespeichert ist und sich öffnen lässt“ – als Erfolgskriterium dünn. Die
  eine Zahl, auf die es ankommt (Umsatz nach BP), sollte hier stehen.

## Danach

- „Jede Prüfung ist eine einzelne Datei … auch die Scans“: Die Datei ist hier **~457 MB** groß
  (SQLite mit den 106 Scans). Wer sie „an Kollegen gibt“, braucht mehr als E-Mail. Ein Satz zur
  Größe wäre fair.
- Den Ordner `Dokumente/Umsatzschätzung` der echten App konnte ich nicht prüfen (macOS verweigert
  hier den Zugriff).
- **Robustheit (App):** Lag im Prüfungsordner eine fremde, leere `.db`-Datei (von mir versehentlich
  erzeugt), zeigte die Liste **gar keine** Prüfung mehr, nur „Noch keine Prüfungen“ und oben die
  Meldung „Ungültige Eingabe: Fall *.db: Falldatei enthält keinen Fall“. Eine einzelne kaputte
  Datei sollte die übrigen Prüfungen nicht ausblenden. Die Datei war danach 86 KB groß. Vermutlich hat
  die App beim Laden ein Schema hineingeschrieben, statt nur zu lesen (nicht weiter geprüft).
- „Hilfe im Programm: **?** und F1“: F1 habe ich nicht ausgelöst.

## Zusammenfassung der wichtigsten Punkte

1. **Frittieröl lässt sich in Schritt 4 nicht als „kein Umsatz“ markieren.** Pflanzenöl steht im
   Rezept von Schnitzel und Rindersteak mit Pommes und erscheint daher gar nicht unter „Übrige
   Einkäufe“. Anleitung und Katalog passen nicht mehr zusammen.
2. **Zuordnungsbilanz 153 statt 149 automatisch**, verursacht durch einen verlesenen Lieferanten
   (`& Barbedarf`), der vier Artikel doppelt erscheinen lässt. „Geschafft, wenn …“ scheitert daran.
   Dazu kommt `Pfand Fass KEG`, das zusätzlich als „Offen“ startet und beim bloßen Anklicken auf
   „Automatisch“ springt.
3. **Ertragsregeln: überall „Ohne hinterlegten Abzug“**, auch Bier vom Fass. Die Einleitung rechnet
   aber mit Schankverlust.
4. **Kein Zielwert für das Ergebnis** (hier 291.717,37 € nach BP, +95.299,37 €, noch mit
   abgeschnittenen Nettopreisen). Man kann am Ende nicht prüfen, ob man richtig liegt.
5. Kleinere Rechenlücke: Einsatz mit geschätztem Umsatz 12.665,71 € gegenüber 8.776,89 + 3.889,10 =
   12.665,99 € (0,28 €).
