# Regeln pflegen

Eine Prüfung endet mit ihrem Bericht, die Arbeit daran nicht: Jede bestätigte Zuordnung, jedes Produkt mit seinem Rezept und jede Ertragsregel steht in den [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/index.md) und gilt für jede Prüfung danach. Dieses Kapitel zeigt an der zweiten Prüfung des Gasthauses, was die erste dort hinterlassen hat, und wie die Regeln selbst gepflegt werden. Im Mittelpunkt stehen die Produkte: Zusammen beschreiben sie eine Produktionskette, vom Einkauf bis zu dem, was verkauft wird.

[Übung 1: Was das Programm sich merkt Im Programm mitmachen, direkt im Browser](https://app.umsatzschaetzung.amtstools.de/lektionen/regeln/)

## Die zweite Prüfung

In der Liste der Prüfungen steht neben der **Bp 2025** eine zweite Prüfung des Gasthauses, die **USt-Sonderprüfung 2025** für Mai bis Juli, mit den 26 Rechnungen dieser drei Monate, wie unter [Eine zweite Prüfung](https://docs.umsatzschaetzung.amtstools.de/dev/guide/sonderpruefung/index.md) angelegt. Das Reglersymbol rechts oben öffnet die **Regeln**, hier und in jeder geöffneten Prüfung.

## Was die Zuordnung schon weiß

Auf **2. Zuordnung** der Sonderprüfung steht über der Liste `3 offen, 74 automatisch, 2 manuell`. Bei der ersten Prüfung waren es vor der ersten Entscheidung `5 offen, 117 automatisch, 0 manuell`. Diesmal sind zwei Positionen **Manuell**, obwohl in dieser Prüfung noch niemand etwas zugeordnet hat.

## Was offen blieb, kommt wieder

Offen sind **Petersilie glatt, Bund** und **Leberkäse am Stück, ungebacken**, genau die zwei, die in der ersten Prüfung offen geblieben sind: Bei der Petersilie fehlt, wie viel ein Bund wiegt, beim Leberkäse hat der Scan vom 5. Juni die Einheit wieder als „K8“ gelesen. Was nicht beantwortet wurde, kann das Programm sich nicht merken.

Bei der **Kaffeesahne** ist **Sahne** schon gewählt; gefragt wird nur, wie viel Sahne ein Karton enthält. Der Scan hat den Namen des Großmarkts diesmal als „GastroMarkt C+C Großhandei“ gelesen, darum greift die Regel nicht ganz.

## Was gelernt ist

Die zwei manuellen stehen am Ende der Liste: **Schweineschnitzel natur, ausgelöst** und **2022 Domina trocken 0,75 l**, die zwei Zuordnungen, die in der ersten Prüfung entschieden wurden, der Domina auf **2. Zuordnung**, das Schnitzel auf **4. Kalkulation**. Den Domina anklicken: Unter **Aus den Regeln** steht `Zugeordnet Rotwein × 750 ml · manuell`, der Vorschlag **Rotwein × 750 ml** trägt **Aktuelle Zuordnung**. Das ist keine Vermutung mit einer Sicherheit, sondern die Regel, die das **Zuordnen** damals angelegt hat. Sie gilt für die 62 Flaschen der zwei Rechnungen dieser Prüfung genauso.

## Die Regel-Datenbank

Eine Prüfung ist eine Datei: Rechnungen, Scans, Sortiment mit Preisen, der erklärte Umsatz. Die Regeln sind eine Datenbank daneben, die [Regel-Datenbank](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank): Produkte mit ihren Rezepten, Ertragsregeln, Gewerbekennzahlen und jede bestätigte Zuordnung. Die Prüfung verweist nur darauf.

Alle, die sich am selben Rechner anmelden, arbeiten mit denselben Regeln. Liegt die Regel-Datenbank auf einer Freigabe im Netz, gilt das für alle, die darauf zeigen: Was jemand heute zuordnet oder anlegt, haben die anderen beim nächsten Start.

Eine Prüfung, die an einen anderen Rechner geht, nimmt die Regeln nicht mit. Dort wird mit den Regeln dieses Rechners neu zugeordnet, siehe [Regeln gehen nicht mit](https://docs.umsatzschaetzung.amtstools.de/dev/import-export/#regeln-gehen-nicht-mit). Mit einer gemeinsamen Regel-Datenbank stellt sich die Frage nicht.

Mitgeliefert wird ein Grundstock von 1.067 Produkten, 696 davon mit Rezept, und 26 Ertragsregeln für jedes Gewerbe, vom Gasthaus bis zur Fahrradwerkstatt, auf kein Amt im Besonderen abgestimmt. Was im Amt anders gilt, wird dort geändert und gilt für jede Prüfung danach.

## Die Reiter

Das Reglersymbol rechts oben (**Regeln**) öffnet die Regeln, in der Liste der Prüfungen wie in jeder Prüfung. Sie haben vier Reiter:

| Reiter            | Was dort steht                                                                                                           |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **Produkte**      | alles, womit gerechnet wird, was eingekauft und was verkauft wird, jedes Produkt mit seinen Warenarten und seinem Rezept |
| **Ertragsregeln** | benannte Abzüge, etwa der Schankverlust beim Fassbier                                                                    |
| **Gewerbe**       | die Gewerbekennzahlen, die eine Prüfung wählen kann                                                                      |
| **Vorlagen**      | das Aussehen des Berichts, meist Sache der IT                                                                            |

Was hier geändert wird, gilt sofort, für jede Prüfung.

Die Produkte hängen über ihre Rezepte zusammen: **Schnitzel mit Pommes** ist ein **Schnitzel, ohne Beilage** und eine **Pommes, Beilage**; das Schnitzel sind 180 g **Schweinefleisch**, die Beilage **Pommes frites** und **Pflanzenöl**, und die werden eingekauft. Zusammen beschreiben die Produkte so eine Produktionskette, vom Einkauf bis zu dem, was verkauft wird.

**Geschafft, wenn …**

- auf **2. Zuordnung** der Sonderprüfung `3 offen, 74 automatisch, 2 manuell` steht
- der Domina dort **Manuell** trägt, ohne dass er in dieser Prüfung zugeordnet wurde
- die Regeln mit ihren vier Reitern einmal offen waren und wieder geschlossen sind

**Zum Nachlesen**

Was in den Regeln steht, beschreibt [Regeln](https://docs.umsatzschaetzung.amtstools.de/dev/regeln/index.md), wo die Regel-Datenbank liegt und wer sie teilt [Verwaltete Installation](https://docs.umsatzschaetzung.amtstools.de/dev/verwaltung/#regel-datenbank), was mit einer weitergegebenen Prüfung geschieht [Regeln gehen nicht mit](https://docs.umsatzschaetzung.amtstools.de/dev/import-export/#regeln-gehen-nicht-mit).

[Weiter: Die Produktionskette Ein Gericht bis zum Einkauf zurückverfolgen](https://docs.umsatzschaetzung.amtstools.de/dev/guide/kette/index.md)
