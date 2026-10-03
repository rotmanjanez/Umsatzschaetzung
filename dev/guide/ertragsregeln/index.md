# Ertragsregeln

Nicht alles, was unten in die [Produktionskette](https://docs.umsatzschaetzung.amtstools.de/dev/guide/kette/index.md) geht, kommt oben als Verkauf heraus. Eine Ertragsregel ist ein Abzug mit Namen: Wie viel vom Einkauf gilt nicht als verkauft, und warum. In der Prüfung wird nur gewählt, welche Regel gilt. Angelegt, geändert und gelöscht werden die Regeln unter **Regeln → Ertragsregeln**, und was dort als **Standard** angekreuzt ist, gilt in jeder Prüfung, die nichts anderes wählt. Im Beispiel wird der Schankverlust beim Fassbier geregelt.

[Übung 5: Ertragsregeln Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/ertragsregeln/)

## Die Abzüge

Eine Ertragsregel gilt für eine Kategorie oder ein Produkt, meist unten in der Kette, bei dem, was eingekauft wird: Von 100 l Fassbier kommen bei 3 % Schankverlust 97 l in die Gläser, und erst diese rechnet die Kalkulation die Kette hinauf bis zum Bier 0,5 l.

## Das Bier im Sortiment

Im Sortiment der Sonderprüfung steht schon **Bier 0,5 l vom Fass** mit `4,60`. Erst ein Produkt mit Fassbier im Rezept bringt die Kategorie **Bier vom Fass** in die Kalkulation dieser Prüfung: Die Kette vom Fassbier unten zum Bier 0,5 l oben ist dann geschlossen.

## Was gerade gilt

Auf **4. Kalkulation** die Seite **Ertragsregeln** öffnen. Mit dem Bier steht **Bier vom Fass** in der Liste, mit **Kein Abzug**. Zur Wahl stehen drei Regeln **Schankverlust** mit 3 %, 5 % und 8 %, keine davon ist Standard. Solange niemand eine wählt, wird nichts abgezogen, in dieser Prüfung wie in jeder anderen.

## Die Regeln für das Fassbier

Über das Reglersymbol rechts oben (**Regeln**) die Regeln öffnen, dort den Reiter **Ertragsregeln**. Links stehen alle Kategorien und dann alle Produkte, daneben die Zahl ihrer Regeln. `Fass` ins Suchfeld tippen und die Kategorie **Bier vom Fass** anklicken. Rechts stehen ihre Regeln, eine je Zeile, mit **Name**, **Abzug** und **Standard**:

## Ein Standard für das Amt

**Standard** in der Zeile mit 3 % ankreuzen. Das ist sofort gespeichert. Ab jetzt zieht jede Prüfung beim Fassbier 3 % Schankverlust ab, solange sie unter **4. Kalkulation → Ertragsregeln** nichts anderes wählt, auch die schon angelegten wie die Bp 2025. Ein schon gespeichertes PDF ändert sich nicht mit. Standard kann je Kategorie nur eine Regel sein; ein neues Häkchen nimmt das alte weg.

## Eine eigene Regel

Eine neue Regel kommt in die leere letzte Zeile (**Neue Regel …**): `Schankverlust, alte Schankanlage` als Name, `6` als Abzug. Sie ist gespeichert, sobald Name und Abzug stehen; darunter erscheint wieder eine leere Zeile. Wählbar ist sie ab jetzt in jeder Prüfung, für einen Betrieb, der den höheren Verlust belegt. Im Bericht steht dann ihr Name, nicht nur der Satz.

## Eine Regel löschen

Gelöscht wird mit dem Mistkübel am Ende der Zeile, hier bei 8 %, und einem **Ja** auf die Nachfrage. Schon erstellte Berichte bleiben unverändert. Hatte eine Prüfung genau diese Regel gewählt, rechnet sie wieder mit der Standardregel, und die Vorschau des Berichts nennt vorneweg einen Hinweis, dass die gewählte Ertragsregel nicht mehr existiert.

## Was in der Prüfung ankommt

Nach dem Schließen der Regeln steht beim **Bier vom Fass** jetzt **Schankverlust (Standard)** mit 3 %, ohne dass in der Prüfung etwas gewählt wurde. Die Auswahl bietet auch die neue Regel an, die 8 % ohne Namen sind verschwunden. Wählt eine Prüfung hier eine andere Regel, geht diese Wahl dem Standard vor.

**Geschafft, wenn …**

- unter **Regeln → Ertragsregeln** beim **Bier vom Fass** drei Regeln stehen: **Schankverlust** mit 3 % als Standard und mit 5 %, dazu **Schankverlust, alte Schankanlage** mit 6 %
- die Sonderprüfung auf **4. Kalkulation → Ertragsregeln** beim **Bier vom Fass** **Schankverlust (Standard)** zeigt, ohne dass dort etwas gewählt wurde

**Zum Nachlesen**

Wie Ertragsregeln angelegt werden und was **Standard** heißt, steht unter [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/#ertragsregeln), wie die Prüfung eine Regel wählt unter [Kalkulation](https://docs.umsatzschaetzung.amtstools.de/dev/kalkulation/#ertragsregeln). Wo die Regeln liegen und wer sie teilt, beschreibt [Verwaltete Installation](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank).

Damit ist das Kapitel **Regeln pflegen** vollständig.

[Weiter: Danach Die Prüfung weitergeben und die eigene beginnen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/danach/index.md)
