"""Wortbestand des Korpus.

v12: Die Warengruppen sind von zehn auf 28 gewachsen und die Zahl der Artikelbasen von
136 auf über tausend. Der Grund steht in v12/PLAN.md: der Tagger soll die
Bezeichnungsspalte an ihrer **Lage** erkennen und nicht daran, dass er „Schweineschnitzel"
auswendig kann. Bei 136 Basen und zehn Variantenwörtern ist der gesamte Namensraum des
Korpus kleiner als das, was ein Modell mit 19 Klassen sich merken kann.

Die Basen sind hier nur der Rohstoff. Was gedruckt wird, setzt `content.article_name`
prozedural zusammen: erfundene Marke, Güteklasse, Herkunft, Zuschnitt, Gebindegröße aus
`sizes.phrase`, Fettgehalt, Alkoholgehalt, Farbe — und danach eine Schreibform
(VERSALIEN, abgekürzt, zahlenvoran, fremdsprachig, lang genug für zwei bis drei Zeilen).
Aus tausend Basen werden so Zehntausende verschiedener gedruckter Namen.

Eine Warengruppe braucht:
  bases     Artikelbasen (der Kern des Namens)
  variants  Variantenwörter, die direkt hinter die Basis dürfen
  sizes     kuratierte Gebindegrößen (sizes.phrase zieht sie als „curated")
  units     (gedruckter Einheitentext, Stück je Gebinde)
  price     (min, max) in Cent je Einheit
  rates     welche Steuersätze vorkommen ('reduced' / 'standard')

`sizes.SIZE_MIX` und `content.GROUPS` führen je Warengruppe einen Eintrag; ein neuer
Schlüssel hier ohne Eintrag dort fällt auf die Vorgabe zurück, und das ist ein Befund,
kein Ruhezustand — `coverage.py` prüft es beim Start (`vocab.check()`).
"""


def _cat(bases, variants, sizes, units, price, rates):
    """Eine Warengruppe aus `|`-getrennten Listen. Kompakter als verschachtelte
    Listenliterale und damit überhaupt noch lesbar bei 28 Gruppen."""
    return {
        "bases": [b.strip() for b in bases.split("|") if b.strip()],
        "variants": [v.strip() for v in variants.split("|")],
        "sizes": [s.strip() for s in sizes.split("|") if s.strip()],
        "units": units,
        "price": price,
        "rates": rates,
    }


CATEGORIES = {
    "baeckerei": _cat(
        "Brötchen|Semmel|Laugenbrezel|Kaiserbrötchen|Bauernbrot|Roggenmischbrot|Ciabatta|"
        "Baguette|Croissant|Nussschnecke|Apfeltasche|Kornspitz|Vollkornbrot|Dinkelweckerl|"
        "Butterhörnchen|Mohnflesserl|Toastbrot|Laugenstange|Laugenbrötchen|Sonnenblumenbrot|"
        "Kürbiskernbrot|Walnussbrot|Bauernkruste|Schwarzbrot|Pumpernickel|Sauerteigbrot|"
        "Fladenbrot|Panini|Focaccia|Brioche|Plundergebäck|Schokocroissant|Rosinenbrötchen|"
        "Quarktasche|Streuselschnecke|Berliner|Krapfen|Vanillekipferl|Zwieback|Semmelwürfel|"
        "Burger Bun|Hot-Dog-Brötchen|Pizzateig|Blätterteig|Strudelteig|Brezenknödel|Grissini|"
        "Baguettebrötchen|Körnerbrötchen|Milchbrötchen",
        "Weizen|Roggen|Dinkel|Vollkorn|Mehrkorn|hell|dunkel|rustikal|vorgebacken|TK|Mini|"
        "handgeformt|aufgebacken|glutenfrei||",
        "40 g|55 g|70 g|90 g|250 g|500 g|750 g|1 kg|Btl. 20 Stk|Kt 60 Stk|Tray 48 Stk",
        [("Stk", 1), ("Btl", 20), ("Kt", 60), ("Kt", 48), ("kg", 1)], (18, 620), ["reduced"]),
    "metzgerei": _cat(
        "Schweinerücken|Schweineschnitzel|Rinderhüfte|Rindergulasch|Bratwurst|Leberkäse|"
        "Schinkenspeck|Kalbsrücken|Faschiertes|Schweinebauch|Rinderbeiried|Hüftsteak|"
        "Grillwürstel|Schweinelachs|Schweinenacken|Kasseler|Schweinefilet|Rinderfilet|"
        "Rumpsteak|Entrecôte|Tafelspitz|Ochsenbrust|Rinderbrust|Kalbsschnitzel|Kalbsbries|"
        "Lammkarree|Lammkeule|Lammhaxe|Wildgulasch|Hirschrücken|Rehrücken|Wildschweinkeule|"
        "Weißwurst|Wiener Würstchen|Frankfurter|Debreziner|Käsekrainer|Bockwurst|Currywurst|"
        "Blutwurst|Leberwurst|Sülze|Mettwurst|Salami|Landjäger|Kochschinken|Rohschinken|"
        "Speckwürfel|Grillbauch|Hackfleisch gemischt|Bratenaufschnitt|Schweinshaxe|"
        "Rindsrouladen|Schmorbraten|Suppenfleisch",
        "frisch|TK|ausgelöst|pariert|geschnitten|mariniert|grob|fein|geräuchert|gepökelt|"
        "am Stück|Vakuum|küchenfertig|paniert||",
        "ca. 2 kg|ca. 5 kg|1 kg|2,5 kg|Vak.|im Netz|180 g|200 g|Kt 10 kg|Schale 400 g",
        [("kg", 1), ("Stk", 1), ("Kt", 10), ("Pack", 1), ("Stk", 1)], (420, 4800),
        ["reduced", "standard"]),
    "gefluegel": _cat(
        "Hähnchenbrust|Hähnchenkeule|Hähnchenschenkel|Hähnchenflügel|Hähnchen ganz|"
        "Putenschnitzel|Putenbrust|Putenoberkeule|Putengeschnetzeltes|Entenbrust|Entenkeule|"
        "Ente ganz|Gänsekeule|Gans ganz|Perlhuhn|Wachtel|Maispoularde|Stubenküken|"
        "Hähnchen-Innenfilet|Hähnchenschnitzel paniert|Chicken Nuggets|Chicken Wings|"
        "Putenwiener|Geflügelwurst|Geflügelleberpastete|Suppenhuhn|Hähnchen grillfertig|"
        "Putenrollbraten|Entenkeulen confit|Geflügelfond|Hähnchenhack|Putengulasch",
        "frisch|TK|ohne Haut|mit Haut|ohne Knochen|mariniert|paniert|Freiland|Mais|"
        "küchenfertig|Vakuum||",
        "ca. 1,2 kg|ca. 2 kg|1 kg|2,5 kg|5 kg|Kt 10 kg|Schale 500 g|Btl. 2,5 kg",
        [("kg", 1), ("Stk", 1), ("Kt", 10), ("Pack", 1)], (380, 2900), ["reduced"]),
    "fisch": _cat(
        "Lachsfilet|Lachsseite|Räucherlachs|Graved Lachs|Forellenfilet|Räucherforelle|"
        "Saibling|Zander|Zanderfilet|Hecht|Karpfen|Wels|Dorsch|Kabeljaufilet|Seelachsfilet|"
        "Rotbarschfilet|Scholle|Seezunge|Heilbutt|Thunfischsteak|Schwertfisch|Wolfsbarsch|"
        "Dorade|Makrele|Hering|Matjesfilet|Bismarckhering|Sardellenfilet|Sardinen|Garnelen|"
        "Black Tiger Garnelen|Flusskrebse|Jakobsmuscheln|Miesmuscheln|Venusmuscheln|"
        "Tintenfischringe|Pulpo|Nordseekrabben|Forellenkaviar|Surimi|Fischstäbchen|Backfisch|"
        "Lachsforelle|Steinbeißer|Seeteufel",
        "frisch|TK|geräuchert|gebeizt|ohne Haut|mit Haut|grätenfrei|Wildfang|Aquakultur|"
        "vakuumiert|ASC|MSC||",
        "1 kg|2,5 kg|5 kg|ca. 200 g|ca. 400 g|Schale 250 g|Kt 5 kg|Btl. 1 kg|Vak. 500 g",
        [("kg", 1), ("Stk", 1), ("Kt", 5), ("Pack", 1), ("Stk", 1)], (590, 8900),
        ["reduced"]),
    "molkerei": _cat(
        "Vollmilch|Frischmilch|H-Milch|Magermilch|Schlagobers|Schlagsahne|Sauerrahm|"
        "Creme fraiche|Schmand|Buttermilch|Naturjoghurt|Fruchtjoghurt|Griechischer Joghurt|"
        "Skyr|Kefir|Butter|Butterschmalz|Margarine|Gouda|Emmentaler|Bergkäse|Gruyère|"
        "Appenzeller|Tilsiter|Edamer|Cheddar|Parmesan|Grana Padano|Pecorino|Mozzarella|"
        "Büffelmozzarella|Burrata|Ricotta|Mascarpone|Frischkäse|Hüttenkäse|Topfen|Quark|"
        "Feta|Halloumi|Camembert|Brie|Gorgonzola|Roquefort|Blauschimmelkäse|Ziegenkäse|"
        "Raclettekäse|Käseaufschnitt|Reibekäse|Eier Größe M|Eier Größe L|Freilandeier|"
        "Bodenhaltungseier|Eiklar pasteurisiert",
        "3,5 %|1,5 %|0,1 %|36 %|20 %|45 % F.i.Tr.|48 % F.i.Tr.|gerieben|in Scheiben|"
        "am Stück|laktosefrei|pasteurisiert|Rohmilch||",
        "1 l|250 ml|500 g|1 kg|5 kg|Becher 180 g|Block 3 kg|Kt 12 x 1 l|Tray 10 Stk",
        [("l", 1), ("kg", 1), ("Stk", 1), ("Kt", 12), ("Kt", 10), ("Pack", 1)],
        (65, 2400), ["reduced"]),
    "gemuese": _cat(
        "Speisekartoffel|Frühkartoffel|Zwiebel|Schalotte|Rote Zwiebel|Karotte|Pastinake|"
        "Sellerieknolle|Staudensellerie|Lauch|Eisbergsalat|Kopfsalat|Romanasalat|"
        "Endiviensalat|Radicchio|Rucola|Feldsalat|Tomate|Rispentomate|Cherrytomate|"
        "Fleischtomate|Salatgurke|Paprika|Spitzpaprika|Chili|Champignon|Kräuterseitling|"
        "Austernpilz|Pfifferlinge|Brokkoli|Blumenkohl|Romanesco|Zucchini|Aubergine|"
        "Hokkaidokürbis|Spinat|Mangold|Grünkohl|Rotkohl|Weißkohl|Wirsing|Kohlrabi|Rote Bete|"
        "Radieschen|Rettich|Spargel weiß|Spargel grün|Petersilie|Schnittlauch|Knoblauch|"
        "Ingwer|Frühlingszwiebel",
        "festkochend|mehligkochend|rot|gelb|grün|braun|Klasse I|Klasse II|Bio|gewaschen|"
        "geschnitten|Rispe|geschält|küchenfertig||",
        "Kiste 10 kg|Netz 5 kg|Steige|Sack 25 kg|Beutel 1 kg|500 g|Kt 6 kg|Bund|Schale 250 g",
        [("kg", 1), ("Kiste", 1), ("Stk", 1), ("Btl", 1), ("Bund", 1), ("Kiste", 1)],
        (55, 1290), ["reduced"]),
    "obst": _cat(
        "Apfel Elstar|Apfel Braeburn|Apfel Gala|Apfel Jonagold|Birne Williams|Birne Abate|"
        "Banane|Orange|Blutorange|Mandarine|Clementine|Zitrone|Limette|Grapefruit|"
        "Weintraube weiß|Weintraube blau|Erdbeere|Himbeere|Heidelbeere|Brombeere|"
        "Johannisbeere|Kirsche|Zwetschke|Pflaume|Aprikose|Pfirsich|Nektarine|Kiwi|Ananas|"
        "Mango|Papaya|Honigmelone|Wassermelone|Avocado|Feige|Datteln|Granatapfel|Physalis|"
        "Rhabarber|Stachelbeere",
        "Klasse I|Klasse II|Bio|regional|Übersee|lose|vorverpackt|reif|kernlos||",
        "Kiste 10 kg|Steige 5 kg|Schale 500 g|Schale 250 g|Netz 2 kg|Kt 8 kg|1 kg|Tray 12 Stk",
        [("kg", 1), ("Kiste", 1), ("Stk", 1), ("Kiste", 1), ("Pack", 1)], (89, 1890),
        ["reduced"]),
    "feinkost": _cat(
        "Prosciutto di Parma|Mortadella Bologna|Bresaola|Coppa|Pancetta|Salame Milano|"
        "Jamón Serrano|Jamón Ibérico|Chorizo|Lomo|Manchego|Queso Fresco|Rillettes|"
        "Terrine de Campagne|Pâté de Foie|Confit de Canard|Saucisson Sec|Roquefort Papillon|"
        "Comté|Brie de Meaux|Reblochon|Tapenade|Pesto Genovese|Pesto Rosso|Aceto Balsamico|"
        "Olio Extra Vergine|Antipasti Misti|Oliven Kalamata|Oliven Gordal|Sardellen in Öl|"
        "Kapern|Kapernäpfel|Artischockenherzen|Getrocknete Tomaten|Trüffelcreme|Trüffelöl|"
        "Steinpilze getrocknet|Polenta|Risottoreis Carnaroli|Risottoreis Arborio|"
        "Passata di Pomodoro|Pomodori Pelati|Gnocchi|Tortellini|Ravioli|Tagliatelle|"
        "Pappardelle|Couscous|Harissa|Hummus|Baba Ganoush|Grissini Torinesi|Taralli",
        "extra|classico|piccante|dolce|stagionato|affumicato|nature|aux herbes|ibérico|"
        "DOP|IGP|g.g.A.|handgeschnitten||",
        "100 g|150 g|200 g|500 g|1 kg|Glas 280 g|Dose 400 g|Kt 6 x 1 kg|Eimer 5 kg",
        [("kg", 1), ("Stk", 1), ("Dose", 1), ("Kt", 6), ("Dose", 1)], (280, 9800),
        ["reduced", "standard"]),
    "tiefkuehl": _cat(
        "Pommes frites|Kroketten|Rösti|Kartoffelpuffer|Wedges|Süßkartoffel-Pommes|"
        "Gemüsemischung|Erbsen|Rahmspinat|Blattspinat|Buttergemüse|Brokkoliröschen|"
        "Beerenmischung|Himbeeren|Blätterteig|Pizzateig|Pizza Margherita|Pizza Salami|"
        "Baguette überbacken|Frühlingsrollen|Gyoza|Chicken Nuggets|Fischstäbchen|Backfisch|"
        "Garnelen|Kaiserschmarrn|Apfelstrudel|Topfenstrudel|Germknödel|Marillenknödel|"
        "Semmelknödel|Spätzle|Gnocchi|Lasagne|Cannelloni|Churros|Brezen|Croissant|"
        "Laugenstange|Backwaren-Mix|Ofenkartoffel|Gemüsepfanne",
        "TK|tiefgekühlt|7 mm|10 mm|vorfrittiert|backfertig|ungebacken|portioniert|"
        "IQF|blanchiert||",
        "2,5 kg|4 x 2,5 kg|5 kg|10 kg|Kt 4 x 2,5 kg|Btl. 1 kg|Kt 6 x 1 kg|Karton 30 Stk",
        [("kg", 1), ("Kt", 4), ("Btl", 1), ("Stk", 1), ("Karton", 1)], (140, 3400),
        ["reduced"]),
    "eis_dessert": _cat(
        "Vanilleeis|Schokoladeneis|Erdbeereis|Zitronensorbet|Mangosorbet|Stracciatella|"
        "Haselnusseis|Pistazieneis|Joghurteis|Eiskonfekt|Softeismischung|Eiswaffel|"
        "Waffelhörnchen|Bechereis|Eis am Stiel|Tiramisu|Panna Cotta|Crème brûlée|"
        "Mousse au Chocolat|Schokopudding|Vanillepudding|Grießbrei|Milchreis|Obstsalat|"
        "Käsekuchen|Sachertorte|Schwarzwälder Kirschtorte|Apfelkuchen|Brownie|Macarons|"
        "Profiteroles|Cheesecake|Eisdessert Coupe|Sahnetorte",
        "TK|Gastro|portioniert|vorgeschnitten|laktosefrei|zuckerreduziert|Bio|"
        "im Becher|in der Schale||",
        "Wanne 5 l|Wanne 2,4 l|Kt 24 Stk|Tray 12 Stk|Blech 2,5 kg|Schale 120 g|1 l|500 ml",
        [("Stk", 1), ("Eimer", 1), ("Kt", 24), ("l", 1), ("Stk", 1)], (120, 4200),
        ["reduced"]),
    "suesswaren": _cat(
        "Vollmilchschokolade|Zartbitterschokolade|Weiße Schokolade|Nussschokolade|"
        "Schokoriegel|Pralinen|Trüffelpralinen|Gummibärchen|Weingummi|Lakritz|Bonbons|"
        "Karamellbonbons|Pfefferminzdragees|Kaugummi|Mandeln geröstet|Cashewkerne|"
        "Erdnüsse gesalzen|Pistazien|Studentenfutter|Walnusskerne|Haselnusskerne|Kekse|"
        "Butterkekse|Waffeln|Lebkuchen|Spekulatius|Christstollen|Marzipan|Nougat|Honig|"
        "Konfitüre Erdbeere|Nuss-Nougat-Creme|Müsliriegel|Popcorn|Chips|Salzstangen|"
        "Cracker|Tortilla Chips|Salzbrezeln",
        "einzeln verpackt|lose|Großpackung|Displaykarton|Bio|ohne Zuckerzusatz|"
        "Hotelportion|Mini||",
        "100 g|200 g|Dose 1 kg|Kt 2,5 kg|Display 24 Stk|Beutel 500 g|Tray 30 Stk",
        [("Stk", 1), ("Kt", 24), ("Btl", 1), ("kg", 1), ("Kt", 1)], (75, 2600),
        ["reduced", "standard"]),
    "tee_gewuerze": _cat(
        "Schwarztee Assam|Schwarztee Earl Grey|Schwarztee Darjeeling|Grüntee Sencha|"
        "Grüntee Jasmin|Weißer Tee|Pfefferminztee|Kamillentee|Fencheltee|Rooibos|"
        "Früchtetee|Hagebuttentee|Kräutertee|Matcha|Chai Gewürztee|Salz fein|Meersalz|"
        "Fleur de Sel|Pfeffer schwarz|Pfeffer weiß|Bunter Pfeffer|Paprikapulver edelsüß|"
        "Paprikapulver rosenscharf|Chiliflocken|Cayennepfeffer|Curry|Currypaste|Kurkuma|"
        "Ingwerpulver|Zimt gemahlen|Zimtstangen|Nelken|Kardamom|Muskatnuss|Lorbeerblätter|"
        "Thymian|Rosmarin|Oregano|Basilikum getrocknet|Majoran|Kümmel|Koriander|Senfkörner|"
        "Wacholderbeeren|Safran|Vanilleschoten|Kräuter der Provence|Grillgewürz|"
        "Gemüsebrühe|Rinderfond",
        "gemahlen|ganz|grob|fein|Bio|lose|im Beutel|Pyramidenbeutel|ohne Glutamat|"
        "Nachfüllpack||",
        "Dose 500 g|Dose 1 kg|Btl. 100 g|Btl. 250 g|Kt 6 x 1 kg|Streuer 300 g|Eimer 3 kg",
        [("Stk", 1), ("Dose", 1), ("kg", 1), ("Btl", 1), ("Kt", 6)], (160, 6800),
        ["reduced", "standard"]),
    "kaffee": _cat(
        "Espresso Bohnen|Caffè Crema|Röstkaffee gemahlen|Kaffeebohnen Hausmischung|"
        "Espresso Ristretto|Espresso Forte|Filterkaffee|Cold Brew Konzentrat|Kaffeepads|"
        "Kaffeekapseln|Instantkaffee|Entkoffeiniert Bohnen|Cappuccino Topping|"
        "Milchpulver Automat|Trinkschokolade|Kakaopulver|Chai Latte Pulver|"
        "Matcha Latte Pulver|Barista Sirup Vanille|Barista Sirup Karamell|"
        "Barista Sirup Haselnuss|Kaffeesahne Portion|Zuckersticks|Rührstäbchen Holz|"
        "Kaffeefilter|Reinigungstabletten|Entkalker|Siebträger-Reinigungspulver|"
        "Milchsystemreiniger|Espresso Arabica|Espresso Robusta Blend|Kaffee Bio Fairtrade|"
        "Röstkaffee Kanne|Eiskaffee-Basis|Vollautomatenbohnen",
        "100 % Arabica|80/20|Bar-Mischung|Bio|Fairtrade|entkoffeiniert|dunkle Röstung|"
        "mittlere Röstung|ganze Bohne|gemahlen||",
        "1 kg|500 g|250 g|Kt 6 x 1 kg|Dose 2 kg|Btl. 1 kg|Karton 100 Stk|Beutel 10 kg",
        [("kg", 1), ("Kt", 6), ("Stk", 1), ("Btl", 1), ("Dose", 1)], (620, 4800),
        ["standard", "reduced"]),
    "getraenke": _cat(
        "Mineralwasser|Tafelwasser|Heilwasser|Apfelsaft|Orangensaft|Multivitaminsaft|Cola|"
        "Cola Zero|Zitronenlimonade|Orangenlimonade|Almdudler|Ginger Ale|Tonic Water|"
        "Bitter Lemon|Soda|Eistee Pfirsich|Eistee Zitrone|Holunderblütensirup|"
        "Waldmeistersirup|Grenadine-Sirup|Cola Postmix-Sirup|Orange Postmix-Sirup|"
        "Energy Drink|Isodrink|Kokoswasser|Ingwershot|Rhabarberlimonade|Spezi|Malzgetränk|"
        "Alkoholfreies Bier|Radler alkoholfrei|Apfelschorle|Traubenschorle|"
        "Johannisbeernektar|Bananennektar|Tomatensaft|Gemüsesaft|Latte Macchiato Drink|"
        "Zitronensprudel|Waldfrucht-Limonade",
        "prickelnd|still|medium|zuckerfrei|naturtrüb|klar|Mehrweg|Einweg|Glas|PET|Dose|"
        "Bag-in-Box||",
        "0,33 l|0,5 l|0,75 l|1,0 l|1,5 l|10 l|20 l|30 l|Kiste 24 x 0,33 l|Kiste 12 x 1,0 l",
        [("Kiste", 24), ("Fass", 1), ("Kt", 12), ("Stk", 1), ("l", 1), ("Fl", 1)],
        (45, 14500), ["standard", "reduced"]),
    "saefte_alkoholfrei": _cat(
        "Direktsaft Apfel|Direktsaft Birne|Direktsaft Traube|Orangensaft frisch gepresst|"
        "Grapefruitsaft|Ananassaft|Mangonektar|Maracujanektar|Kirschnektar|Sauerkirschsaft|"
        "Rhabarbersaft|Holundersaft|Sanddornsaft|Aroniasaft|Karottensaft|Rote-Bete-Saft|"
        "Selleriesaft|Ingwer-Kurkuma-Shot|Smoothie Grün|Smoothie Beere|Limettendirektsaft|"
        "Zitronensaft|Bio-Apfelsaft naturtrüb|Streuobstsaft|Traubensaft weiß|"
        "Traubensaft rot|Birnennektar|Pfirsichnektar|Bananensaft|Kokosnusswasser",
        "naturtrüb|klar|Direktsaft|aus Konzentrat|Bio|ohne Zuckerzusatz|Demeter|"
        "kaltgepresst|pasteurisiert||",
        "0,2 l|0,25 l|0,33 l|0,7 l|1,0 l|3,0 l|5,0 l|Bag in Box 10 l|Kt 6 x 1,0 l",
        [("Fl", 1), ("Kt", 6), ("Stk", 1), ("l", 1), ("Kiste", 12)], (79, 4900),
        ["reduced", "standard"]),
    "brauerei": _cat(
        "Helles|Pils|Märzen|Zwickl|Kellerbier|Landbier|Export|Weizenbier hell|"
        "Weizenbier dunkel|Kristallweizen|Hefeweizen alkoholfrei|Dunkles Lager|Bockbier|"
        "Doppelbock|Maibock|Festbier|Rauchbier|Schwarzbier|Altbier|Kölsch|Berliner Weisse|"
        "Gose|India Pale Ale|Pale Ale|Session IPA|Stout|Porter|Craft Lager|Radler|Russ|"
        "Bier Postmix|Fassbier Helles|Fassbier Pils|Fassbier Weizen|Biermischgetränk|"
        "Leichtbier|Zoiglbier|Naturtrübes",
        "naturtrüb|filtriert|unfiltriert|alkoholfrei|4,9 % vol|5,2 % vol|5,4 % vol|"
        "Mehrweg|Bügelflasche|Fass|Keg||",
        "0,33 l|0,5 l|Kiste 20 x 0,5 l|Kiste 24 x 0,33 l|Fass 30 l|Fass 50 l|keg 20 l|"
        "Partyfass 5 l",
        [("Kiste", 20), ("Fass", 1), ("Fl", 1), ("Kt", 24), ("l", 1), ("Stk", 1)],
        (52, 16500), ["standard"]),
    "wein": _cat(
        "Grüner Veltliner|Riesling|Silvaner|Müller-Thurgau|Weißburgunder|Grauburgunder|"
        "Chardonnay|Sauvignon Blanc|Gewürztraminer|Scheurebe|Bacchus|Kerner|Rivaner|"
        "Welschriesling|Neuburger|Roter Veltliner|Muskateller|Zweigelt|Blaufränkisch|"
        "St. Laurent|Spätburgunder|Domina|Dornfelder|Portugieser|Trollinger|Lemberger|"
        "Regent|Merlot|Cabernet Sauvignon|Syrah|Pinot Noir|Primitivo|Montepulciano|"
        "Chianti|Barbera|Tempranillo|Rioja Crianza|Rosé|Weißherbst|Sekt brut|"
        "Sekt extra trocken|Winzersekt|Crémant|Prosecco Frizzante|Prosecco Spumante|"
        "Cava brut|Champagner brut|Federweißer|Glühwein|Perlwein|Secco|Rotwein-Cuvée|"
        "Weißwein-Cuvée|Blanc de Noir|Spätlese|Kabinett|Auslese|Eiswein",
        "trocken|halbtrocken|feinherb|lieblich|edelsüß|Kabinett|Spätlese|Auslese|Reserve|"
        "Klassik|Selection|Alte Reben|DAC|QbA|Prädikatswein|b.A.|brut|extra brut|"
        "12,5 % vol|13,0 % vol||",
        "0,75 l|1,0 l|0,375 l|Bag in Box 10 l|Kt 6 Fl.|Kt 12 Fl.|Karton 6 x 0,75 l",
        [("Fl", 1), ("Kt", 6), ("Stk", 1), ("Karton", 12), ("l", 1)], (280, 9800),
        ["standard"]),
    "spirituosen": _cat(
        "Wodka|Wodka Premium|Gin|Dry Gin|London Dry Gin|Sloe Gin|Weißer Rum|Brauner Rum|"
        "Overproof Rum|Spiced Rum|Blended Whisky|Single Malt Whisky|Bourbon Whiskey|"
        "Rye Whiskey|Irish Whiskey|Tequila Silver|Tequila Reposado|Mezcal|Cachaça|Pisco|"
        "Cognac VS|Cognac VSOP|Armagnac|Brandy|Weinbrand|Grappa|Marc|Obstbrand Marille|"
        "Williamsbirne|Zwetschgenbrand|Kirschwasser|Himbeergeist|Vogelbeerbrand|"
        "Kräuterlikör|Magenbitter|Aperitivo Bitter|Vermouth rosso|Vermouth bianco|Amaretto|"
        "Sambuca|Limoncello|Eierlikör|Sahnelikör|Kaffeelikör|Orangenlikör|Cassis|"
        "Pfefferminzlikör|Ouzo|Raki|Absinth|Portwein|Sherry Fino|Madeira|Likörwein|"
        "Punschsirup|Aquavit",
        "40 % vol|37,5 % vol|38 % vol|43 % vol|50 % vol|Premium|Standard|Barflasche|"
        "im Holzfass gereift|handverlesen|Hausmarke||",
        "0,5 l|0,7 l|1,0 l|3,0 l|0,2 l|0,04 l|Kt 6 x 0,7 l|Kt 12 x 0,7 l|Tray 24 x 0,02 l",
        [("Fl", 1), ("Kt", 6), ("Stk", 1), ("l", 1), ("Kt", 24)], (680, 12800),
        ["standard"]),
    "reinigung_hygiene": _cat(
        "Handspülmittel|Maschinenspülmittel|Klarspüler|Geschirrspültabs|Regeneriersalz|"
        "Glasreiniger|Allzweckreiniger|Fettlöser|Grillreiniger|Backofenreiniger|"
        "Sanitärreiniger|WC-Reiniger|Urinsteinlöser|Kalklöser|Fußbodenreiniger|Wischpflege|"
        "Desinfektionsmittel|Händedesinfektion|Flächendesinfektion|Desinfektionstücher|"
        "Handseife|Seifenspender|Papierhandtücher|Handtuchrolle|Toilettenpapier|Küchenrolle|"
        "Putztücher|Wischmopp|Mopbezug|Bodenwischer|Reinigungseimer|Scheuermilch|"
        "Edelstahlpflege|Entkalker Spülmaschine|Waschpulver|Weichspüler|Fleckensalz|"
        "Müllbeutel|Müllsack|Schwämme|Topfreiniger|Mikrofasertuch|Nitrilhandschuhe|"
        "Latexhandschuhe",
        "unparfümiert|parfümiert|Konzentrat|gebrauchsfertig|chlorfrei|Größe M|Größe L|"
        "Größe XL|2-lagig|3-lagig|weiß|blau||",
        "5 l Kanister|10 l Kanister|1 l|750 ml|Pack 250 Stk|Kt 1000 Stk|Rolle 300 m|"
        "Karton 6 x 1 l|Eimer 10 kg",
        [("Stk", 1), ("Kanister", 1), ("Kt", 6), ("Pack", 1), ("Pkg", 1), ("Rolle", 1),
         ("l", 1)],
        (120, 9800), ["standard"]),
    "verpackung_einweg": _cat(
        "Pizzakarton|Menübox|Faltschachtel|Snackbox|Salatschale|Suppenbecher|Kaffeebecher|"
        "Becherdeckel|Trinkbecher|Trinkhalm Papier|Trinkhalm Bio|Rührstäbchen|"
        "Besteckset Holz|Gabel Holz|Messer Holz|Löffel Holz|Servietten|Cocktailservietten|"
        "Platzset Papier|Tischdecke Papier|Alufolie|Frischhaltefolie|Backpapier|"
        "Butterbrotpapier|Einschlagpapier|Pergamentpapier|Papierbeutel|Papiertragetasche|"
        "Kunststofftragetasche|Vakuumbeutel|Gefrierbeutel|Zip-Beutel|Aluschale|"
        "Menüschale PP|Menüschalendeckel|Eisbecher|Dessertschale|Portionsbecher|"
        "Saucenbecher|Etiketten|Klebeband|Stretchfolie|Kassenrolle|Bonrolle Thermo",
        "weiß|braun|natur|bedruckt|unbedruckt|kompostierbar|recycelbar|24 cm|28 cm|33 cm|"
        "40 cm|2-lagig|3-lagig||",
        "Pack 100 Stk|Pack 250 Stk|Kt 500 Stk|Kt 1000 Stk|Rolle 300 m|Rolle 500 m|"
        "Karton 50 Stk|Btl. 200 Stk",
        [("Stk", 1), ("Pack", 1), ("Pkg", 20), ("Kt", 1), ("Rolle", 1), ("Karton", 1)], (140, 8900),
        ["standard"]),
    "gastrobedarf_technik": _cat(
        "Kochtopf|Bratpfanne|Wok|Gastronormbehälter|GN-Deckel|Backblech|Grillrost|"
        "Schneidebrett|Kochmesser|Ausbeinmesser|Wetzstahl|Schöpflöffel|Schneebesen|"
        "Pfannenwender|Sieb|Salatschleuder|Digitalwaage|Kernthermometer|Küchentimer|"
        "Thermobox|Speisenwärmer|Chafing Dish|Brennpaste|Teller flach|Teller tief|"
        "Suppenteller|Tasse|Untertasse|Longdrinkglas|Weinglas|Sektglas|Bierglas|Besteckset|"
        "Servierplatte|Menage|Salzstreuer|Pfeffermühle|Serviertablett|Kellnermappe|"
        "Barmatte|Cocktailshaker|Jigger|Eiswürfelbehälter|Dunstfilter|Fettabscheiderfilter",
        "Edelstahl|Kunststoff|Porzellan|Glas|beschichtet|stapelbar|spülmaschinenfest|"
        "Ø 20 cm|Ø 26 cm|Ø 32 cm|GN 1/1|GN 1/2|GN 1/3|65 mm|100 mm||",
        "1 Stk|Set 6 Stk|Kt 12 Stk|Kt 6 Stk|Karton 24 Stk|VE 4 Stk",
        [("Stk", 1), ("Stk", 1), ("Kt", 12), ("Karton", 1), ("Pack", 1)], (290, 48000),
        ["standard"]),
    "waesche_service": _cat(
        "Tischwäsche Miete|Tischdecke weiß|Tischdecke bordeaux|Stoffserviette|"
        "Moltonunterlage|Kochjacke|Kochhose|Kochmütze|Schürze lang|Schürze kurz|Vorbinder|"
        "Servicehemd|Servicebluse|Halstuch|Geschirrtuch|Küchenhandtuch|Putzlappen|Wischtuch|"
        "Bettwäschegarnitur|Bettlaken|Frotteetuch|Duschtuch|Badematte|Wäschereipauschale|"
        "Waschposten Mischwäsche|Bügelservice|Mattenservice|Berufskleidung Leasing",
        "weiß|bordeaux|anthrazit|schwarz|Größe S|Größe M|Größe L|Größe XL|Größe XXL|"
        "je Woche|je Monat|gemietet||",
        "1 Stk|10 Stk|Pack 25 Stk|Pack 50 Stk|je Woche|je Monat|kg",
        [("Stk", 1), ("Pack", 1), ("kg", 1), ("Stk", 1)], (65, 4800), ["standard"]),
    "blumen_deko": _cat(
        "Schnittblumenstrauß|Rosen rot|Rosen weiß|Tulpen|Nelken|Gerbera|Lilien|"
        "Chrysanthemen|Sonnenblumen|Eukalyptuszweig|Grünschnitt Deko|Topfpflanze|Orchidee|"
        "Kräutertopf Basilikum|Kräutertopf Rosmarin|Tischgesteck|Adventskranz|"
        "Weihnachtsstern|Stabkerze|Teelichter|Windlicht|Glasvase|Dekoband|Trockenblumen|"
        "Blumenerde|Übertopf|Tischläufer Deko",
        "rot|weiß|gelb|rosa|gemischt|Bund|10er|20er|kurz|lang|Höhe 40 cm|Höhe 60 cm||",
        "Bund 10 Stk|Bund 20 Stk|1 Stk|Kt 12 Stk|Karton 6 Stk|Tray 24 Stk",
        [("Stk", 1), ("Bund", 1), ("Kt", 12), ("Kt", 24)], (95, 6800),
        ["reduced", "standard"]),
    "bio_hof": _cat(
        "Bio-Kartoffel|Bio-Karotte|Bio-Zwiebel|Bio-Apfel|Bio-Salat|Bio-Eier|Bio-Milch|"
        "Bio-Butter|Bio-Joghurt|Bio-Bergkäse|Bio-Rindfleisch|Bio-Schweinefleisch|"
        "Bio-Hühnerbrust|Bio-Dinkelmehl|Bio-Weizenmehl|Bio-Roggenmehl|Bio-Haferflocken|"
        "Bio-Linsen|Bio-Kichererbsen|Bio-Reis|Bio-Nudeln|Bio-Rapsöl|Bio-Olivenöl|Bio-Honig|"
        "Bio-Apfelsaft|Bio-Kräutertee|Bio-Tofu|Bio-Sojadrink|Bio-Haferdrink|Bio-Mandeldrink|"
        "Demeter-Gemüsekiste|Hofkäse|Bio-Rohmilchkäse|Bio-Sauerkraut",
        "Demeter|Bioland|Naturland|EU-Bio|regional|Hofeigen|unbehandelt|Rohmilch|"
        "aus Freilandhaltung||",
        "1 kg|2,5 kg|5 kg|Kiste 10 kg|Btl. 500 g|Glas 500 ml|Kt 6 Stk|Tray 10 Stk",
        [("kg", 1), ("Stk", 1), ("Kiste", 1), ("Kt", 6), ("l", 1)], (110, 3900),
        ["reduced"]),
    "catering": _cat(
        "Buffet Fingerfood|Belegte Brötchen|Sandwichplatte|Wrap-Platte|Käseplatte|"
        "Wurstplatte|Obstplatte|Gemüsesticks mit Dip|Suppentopf|Gulaschkessel|"
        "Chili con Carne|Salatbar-Schüssel|Nudelsalat|Kartoffelsalat|Krautsalat|Quiche|"
        "Blechkuchen|Kuchenplatte|Canapés|Häppchen warm|Spanferkel|Grillplatte|Menü 3-Gang|"
        "Tagesmenü|Personalverpflegung|Servicekraft Stunde|Küchenhilfe Stunde|"
        "Lieferpauschale|Geschirrmiete|Aufbau und Abbau",
        "je Person|kalt|warm|vegetarisch|vegan|glutenfrei|10 Personen|20 Personen|"
        "50 Personen|inkl. Geschirr||",
        "je Person|Platte 10 Stk|Platte 20 Stk|Schüssel 2 kg|Topf 10 l|Stunde|Pauschale",
        [("Stk", 1), ("Stk", 1), ("Stk", 1), ("Stk", 1), ("kg", 1)], (190, 28000),
        ["reduced", "standard"]),
    "tabak": _cat(
        "Zigaretten Schachtel|Zigaretten Stange|Feinschnitt|Drehtabak|Zigarillos|Zigarren|"
        "Pfeifentabak|Zigarettenpapier|Filterhülsen|Feuerzeug|Streichhölzer|"
        "Wasserpfeifentabak|Shisha-Kohle|Snus|E-Liquid|Einweg-Vape|Aschenbecher|"
        "Zigarettenstopfer|Humidor-Befeuchter|Pfeifenreiniger",
        "20 Stk|40 Stk|Beutel|Dose|Stange|Display|Nachfüllpack||",
        "Stange 10 x 20 Stk|Beutel 30 g|Dose 100 g|Display 20 Stk|Kt 50 Stk",
        [("Stk", 1), ("Pack", 10), ("Btl", 1), ("Kt", 50), ("Kt", 20)],
        (390, 12500), ["standard"]),
    "nonfood": _cat(
        "Serviette|Geschirrspültabs|Handtuchrolle|Müllsack|Alufolie|Frischhaltefolie|"
        "Reinigungsmittel|Glasreiniger|Einweghandschuh|Kerzenteelicht|Kassenrolle|"
        "Pizzakarton|Trinkhalm Papier|Batterien|LED-Leuchtmittel|Leuchtstoffröhre|"
        "Verbandkasten|Pflasterset|Erste-Hilfe-Auffüllset|Fußmatte|Besen|Handfeger|"
        "Kehrschaufel|Eimer|Wäschekorb|Fachboden|Transportwagen|Klappbox|Preisauszeichner|"
        "Etikettenrolle|Schreibblock|Kugelschreiber|Ordner|Kopierpapier",
        "weiß|schwarz|bordeaux|2-lagig|3-lagig|unparfümiert|Größe M|Größe L|24 cm|33 cm|"
        "40 cm|A4|A5||",
        "Pack 250 Stk|Kt 1000 Stk|Rolle 300 m|5 l Kanister|Karton 6 x 1 l|VE 12 Stk",
        [("Pack", 1), ("Kt", 1), ("Stk", 1), ("Rolle", 1), ("Pkg", 12)], (120, 9800),
        ["standard"]),
    "cc": _cat(
        "Pommes frites|Kroketten|Backerbsen|Semmelbrösel|Sonnenblumenöl|Rapsöl|Olivenöl|"
        "Frittieröl|Tomatenpaprika|Essiggurken|Mayonnaise|Ketchup|Senf mittelscharf|"
        "Senf scharf|Remoulade|Cocktailsauce|Sojasauce|Worcestersauce|Tabasco|"
        "Reis langkorn|Basmatireis|Risottoreis|Spaghetti|Penne|Fusilli|Bandnudeln|"
        "Eiernudeln|Zucker|Puderzucker|Salz|Mehl Type 405|Mehl Type 550|Grieß|Haferflocken|"
        "Backpulver|Speisestärke|Gelatine|Tomatenmark|Pizzasauce|Passierte Tomaten|"
        "Weiße Bohnen|Kidneybohnen|Mais|Erbsen|Champignons|Ananas|Pfirsiche|Thunfisch|"
        "Sardellen|Kokosmilch|Essig|Balsamico|Zitronensäure",
        "TK|7 mm|10 mm|fein|grob|Eimer|Kanister|Beutel|Dose|Glas|in Öl|in Salzlake|"
        "ohne Zuckerzusatz||",
        "2,5 kg|5 kg|10 kg|10 l|Kt 4 x 2,5 kg|Eimer 5 kg|Dose 850 ml|Kt 6 x 1 l",
        [("Kt", 4), ("Stk", 1), ("kg", 1), ("Eimer", 1), ("Dose", 1), ("l", 1)],
        (95, 5400), ["reduced", "standard"]),
}

# Gewichte der Warengruppen beim Ziehen einer Rechnung. Gleichverteilt über 28 Gruppen
# bekäme `wein` nur noch 3,6 % der Rechnungen — in v11 waren es 10 %, und Fehlerbild 1
# (Jahrgang vorn, jahrgangscodierte Artikelnummer) hängt genau daran. Die Gewichte
# bilden ausserdem den Posteingang eines Gastronomiebetriebs nach: Getränke, Fleisch,
# Molkerei und Großhandel kommen wöchentlich, Blumen und Tabak selten.
CATEGORY_WEIGHTS = {
    "wein": 9.0, "spirituosen": 6.0, "getraenke": 6.0, "brauerei": 4.0,
    "metzgerei": 5.0, "molkerei": 5.0, "gemuese": 4.0, "cc": 5.0,
    "baeckerei": 4.0, "obst": 3.0, "fisch": 3.0, "gefluegel": 2.5,
    "feinkost": 3.0, "tiefkuehl": 3.0, "kaffee": 3.0, "tee_gewuerze": 2.0,
    "saefte_alkoholfrei": 2.0, "eis_dessert": 1.5, "suesswaren": 1.5,
    "reinigung_hygiene": 3.0, "verpackung_einweg": 3.0, "nonfood": 2.5,
    "gastrobedarf_technik": 2.0, "waesche_service": 1.5, "bio_hof": 1.5,
    "catering": 1.5, "blumen_deko": 1.0, "tabak": 1.0,
}


def category_pool():
    """(Namen, Gewichte) für `rng.choices` — sortiert, damit derselbe Seed denselben
    Korpus erzeugt."""
    names = sorted(CATEGORIES)
    return names, [CATEGORY_WEIGHTS.get(n, 1.0) for n in names]


# ------------------------------------------------------------ v12: Lieferantennamen
#
# v11 hatte 24 Nachnamen, 24 Kopfwörter und 9 Rechtsformen — 5 184 mögliche Namen, von
# denen ein 2500-Rechnungs-Korpus jeden zweiten mehrfach druckt. v12 zieht aus 160
# Nachnamen, 70 Kopfwörtern, 40 Handwerksbezeichnungen und 22 Rechtsformen und setzt
# dazu Familienbetriebe mit Inhaberzeile zusammen (`Metzgerei Hofmann Inh. Georg
# Hofmann`) — genau die Form, an der das v11-Modell den Werbesatz an den Namen gehängt
# hat.
SUPPLIER_HEADS = [
    "Gebr.", "Gasthaus-Service", "Delikatessen", "Feinkost", "Landhof", "Alpen", "Donau",
    "Wiener", "Steirische", "Tiroler", "Nordwest", "Süd", "Rhein", "Elbe", "Hanse",
    "Zentral", "Erste", "Regional", "Terra", "Vital", "Prima", "Optima", "Panorama",
    "Kreis", "Main", "Neckar", "Isar", "Spree", "Weser", "Ruhr", "Lahn", "Saale",
    "Mosel", "Ems", "Allgäu", "Schwarzwald", "Odenwald", "Westfalen", "Franken",
    "Oberland", "Unterland", "Bergland", "Seeland", "Marsch", "Heide", "Börde",
    "Mark", "Küsten", "Insel", "Hafen", "Stadt", "Land", "Berg", "Tal", "Au",
    "Sonnen", "Morgen", "Abend", "Nord", "Ost", "West", "Euro", "Inter", "Uni",
    "Pro", "Top", "Fresh", "Direkt", "Express", "Global",
]
SUPPLIER_NAMES = [
    "Ahrweiler", "Birnbacher", "Calmeyer", "Dohrmann", "Eckenstein", "Frühwirth",
    "Gassner", "Hollerbach", "Innerhofer", "Jaklitsch", "Kirchmayr", "Lindtner",
    "Moosbrugger", "Nussbaumer", "Oberleitner", "Pucher", "Quehenberger", "Rauscher",
    "Steinbichler", "Trautmann", "Ulrichs", "Vogelsang", "Wallnöfer", "Zehentner",
    "Achleitner", "Allgeier", "Amberger", "Aschenbrenner", "Bachmeier", "Baumgartner",
    "Beckenbauer", "Bergmüller", "Bichler", "Blaschke", "Bogner", "Brandstetter",
    "Braunegger", "Brückner", "Buchberger", "Danzinger", "Dellinger", "Dietrichs",
    "Doppler", "Draxler", "Ebenhoch", "Egger", "Eibensteiner", "Enzinger", "Fellner",
    "Fenninger", "Feuerstein", "Fleischhacker", "Forstner", "Gebhart", "Geisler",
    "Gigler", "Glaser", "Gollner", "Grabner", "Grasegger", "Greiner", "Grundner",
    "Haberl", "Hackl", "Hainzl", "Hammerschmid", "Harrer", "Hauswirth", "Hehenberger",
    "Heigl", "Hemetsberger", "Hermannsdorfer", "Hilbrand", "Hofmann", "Hörmann",
    "Huemer", "Jandl", "Kainzbauer", "Kaltenbrunner", "Kapeller", "Karrer", "Kehrer",
    "Kellermann", "Kerschbaumer", "Kiesenhofer", "Klausner", "Kneissl", "Knoblauch",
    "Königsberger", "Kraxner", "Krenn", "Kriechbaum", "Kügler", "Lachmayr", "Lackner",
    "Lamprecht", "Landgraf", "Laufenböck", "Lechthaler", "Leitgeb", "Lengauer",
    "Lichtenegger", "Lindenthal", "Loidl", "Mandlberger", "Mauracher", "Meierhofer",
    "Mittermayr", "Mühlbacher", "Nagelschmid", "Neuhauser", "Niederhofer", "Oberhauser",
    "Ofenböck", "Ortner", "Osterberger", "Pachler", "Pallauf", "Parzer", "Perchtold",
    "Pfeiffenberger", "Pichlmaier", "Pilsner", "Plattner", "Pöschl", "Prantl",
    "Rabensteiner", "Radlherr", "Raffeiner", "Reisinger", "Reiterer", "Riedlsperger",
    "Ringhofer", "Rohrmoser", "Sandbichler", "Schachermayr", "Schallmeiner",
    "Scheibenreif", "Schindlauer", "Schlagbauer", "Schmiedbauer", "Schoberleitner",
    "Schreckenfuchs", "Seibold", "Sonnleitner", "Spindelberger", "Staudinger",
    "Steinlechner", "Stierhof", "Stockinger", "Straubinger", "Sulzbacher", "Tannhauser",
    "Thalhammer", "Traxler", "Übleis", "Unterberger", "Veitshans", "Wagenhofer",
    "Waldhäusl", "Weghofer", "Weinberger", "Wiesinger", "Wimmersberger", "Wohlfahrt",
    "Zauner", "Zehetgruber", "Zellhofer", "Zimmermann", "Zottl", "Zwickl",
]
SUPPLIER_TAILS = [
    "GmbH", "GmbH & Co. KG", "KG", "OG", "GesmbH", "e.K.", "AG", "Handels GmbH",
    "Vertriebs GmbH", "eG", "e. K.", "OHG", "GbR", "AG & Co. KG", "SE", "KGaA",
    "GmbH & Co. OHG", "Ges.m.b.H.", "& Söhne", "& Sohn", "Nachf.", "& Partner",
    "Handelsges.m.b.H.", "Gruppe", "Holding GmbH",
]
# Handwerks- und Betriebsbezeichnungen, die *vor* dem Nachnamen stehen. Die Form
# „Metzgerei Hofmann" ist der häufigste Briefkopf auf den echten Belegen und war im
# Korpus bis v11 gar nicht vorhanden: dort hiess jeder Lieferant „<Kopfwort>
# <Nachname> <Rechtsform>".
TRADE_HEADS = {
    "baeckerei": ["Bäckerei", "Backstube", "Konditorei", "Bäckerei & Konditorei",
                  "Landbäckerei", "Hofbäckerei", "Stadtbäckerei"],
    "metzgerei": ["Metzgerei", "Fleischerei", "Fleischwaren", "Landmetzgerei",
                  "Hausmetzgerei", "Wurstmanufaktur", "Schlachthof"],
    "gefluegel": ["Geflügelhof", "Geflügelzentrale", "Geflügelspezialitäten",
                  "Hofgeflügel", "Geflügelgroßhandel"],
    "fisch": ["Fischhandel", "Fischereibetrieb", "Fischgroßhandel", "Seefisch",
              "Fischmanufaktur", "Räucherei"],
    "molkerei": ["Molkerei", "Käserei", "Sennerei", "Landmolkerei", "Käsehandel",
                 "Hofkäserei"],
    "gemuese": ["Gemüsehandel", "Gärtnerei", "Frischdienst", "Obst & Gemüse",
                "Erzeugergemeinschaft", "Gemüsebau"],
    "obst": ["Obsthof", "Obstbau", "Obstgroßhandel", "Obst & Südfrüchte", "Fruchthandel"],
    "feinkost": ["Feinkost", "Delikatessen", "Italienische Spezialitäten",
                 "Feinkostimport", "Gourmet-Service", "Casa"],
    "tiefkuehl": ["Tiefkühldienst", "Frostwaren", "TK-Service", "Kühlhaus",
                  "Tiefkühlkost"],
    "eis_dessert": ["Eismanufaktur", "Eiskrem", "Dessertmanufaktur", "Konditorei",
                    "Patisserie"],
    "suesswaren": ["Süßwaren", "Confiserie", "Schokoladenmanufaktur", "Naschwerk"],
    "tee_gewuerze": ["Gewürzhandel", "Teehaus", "Gewürzmühle", "Kräuterkontor",
                     "Tee & Gewürze"],
    "kaffee": ["Kaffeerösterei", "Rösterei", "Kaffeekontor", "Caffè",
               "Kaffeespezialitäten"],
    "getraenke": ["Getränkehandel", "Getränkefachgroßhandel", "Getränkemarkt",
                  "Getränke", "Getränkevertrieb"],
    "saefte_alkoholfrei": ["Kelterei", "Mosterei", "Fruchtsaftkelterei", "Saftmanufaktur",
                           "Obstpresse"],
    "brauerei": ["Brauerei", "Privatbrauerei", "Braumanufaktur", "Brauhaus",
                 "Bierdepot", "Klosterbrauerei"],
    "wein": ["Weingut", "Weinhandel", "Weinkellerei", "Winzerkeller", "Vinothek",
             "Sektkellerei", "Weinbau", "Weinhandlung"],
    "spirituosen": ["Destillerie", "Brennerei", "Spirituosenhandel", "Barbedarf",
                    "Edelbrennerei", "Likörfabrik"],
    "reinigung_hygiene": ["Hygienebedarf", "Reinigungsbedarf", "Chemie & Hygiene",
                          "Hygieneservice", "Reinigungssysteme"],
    "verpackung_einweg": ["Verpackungen", "Verpackungshandel", "Einwegservice",
                          "Packmittel", "Verpackungstechnik"],
    "gastrobedarf_technik": ["Gastrobedarf", "Großküchentechnik", "Küchentechnik",
                             "Hotelbedarf", "Gastroausstattung"],
    "waesche_service": ["Wäscherei", "Textilservice", "Mietwäsche", "Textilpflege",
                        "Berufskleidung"],
    "blumen_deko": ["Blumenhaus", "Gärtnerei", "Floristik", "Blumengroßmarkt",
                    "Dekoservice"],
    "bio_hof": ["Biohof", "Naturkost", "Bio-Großhandel", "Ökokiste", "Hofladen",
                "Bioland-Hof"],
    "catering": ["Catering", "Partyservice", "Eventcatering", "Menüservice",
                 "Küchenservice"],
    "tabak": ["Tabakwaren", "Tabakhandel", "Zigarrenhaus", "Rauchwaren"],
    "nonfood": ["Hygiene & Bedarf", "Gastrobedarf", "Handelshaus", "Bedarfsartikel"],
    "cc": ["Abholgroßmarkt", "Cash & Carry", "Großhandel", "Zustellgroßhandel",
           "Lebensmittelgroßhandel"],
}
TRADES = {
    "baeckerei": "Backwaren", "metzgerei": "Fleischwaren", "gefluegel": "Geflügel",
    "fisch": "Fisch & Meeresfrüchte", "molkerei": "Molkereiprodukte",
    "gemuese": "Obst & Gemüse", "obst": "Obst & Südfrüchte", "feinkost": "Feinkost",
    "tiefkuehl": "Tiefkühlkost", "eis_dessert": "Eis & Desserts",
    "suesswaren": "Süßwaren", "tee_gewuerze": "Tee & Gewürze",
    "kaffee": "Kaffeerösterei", "getraenke": "Getränkefachhandel",
    "saefte_alkoholfrei": "Fruchtsäfte", "brauerei": "Bier & Braukunst",
    "wein": "Weinhandel", "spirituosen": "Spirituosen",
    "reinigung_hygiene": "Reinigung & Hygiene", "verpackung_einweg": "Verpackung",
    "gastrobedarf_technik": "Gastronomiebedarf", "waesche_service": "Textilservice",
    "blumen_deko": "Blumen & Deko", "bio_hof": "Biolebensmittel",
    "catering": "Catering", "tabak": "Tabakwaren", "nonfood": "Hygiene & Bedarf",
    "cc": "Großhandel",
}

# ------------------------------------------------------------ v12: Orte und Straßen
#
# 16 Orte und 15 Straßen waren memorierbar: `postcode` stand im Korpus immer neben
# einem von sechzehn Ortsnamen. Jetzt 170 Orte mit passend aussehender PLZ (die
# führenden Ziffern stimmen mit dem Leitgebiet überein) und 170 Straßen.
CITIES = [
    ("1010", "Wien"), ("1100", "Wien"), ("1210", "Wien"), ("2000", "Stockerau"),
    ("2100", "Korneuburg"), ("2320", "Schwechat"), ("2340", "Mödling"),
    ("2500", "Baden"), ("2700", "Wiener Neustadt"), ("3100", "St. Pölten"),
    ("3300", "Amstetten"), ("3500", "Krems an der Donau"), ("3830", "Waidhofen"),
    ("4020", "Linz"), ("4030", "Linz"), ("4400", "Steyr"), ("4600", "Wels"),
    ("4780", "Schärding"), ("4810", "Gmunden"), ("5020", "Salzburg"),
    ("5400", "Hallein"), ("5500", "Bischofshofen"), ("5700", "Zell am See"),
    ("6020", "Innsbruck"), ("6060", "Hall in Tirol"), ("6330", "Kufstein"),
    ("6460", "Imst"), ("6700", "Bludenz"), ("6800", "Feldkirch"), ("6900", "Bregenz"),
    ("7000", "Eisenstadt"), ("7100", "Neusiedl am See"), ("7400", "Oberwart"),
    ("8010", "Graz"), ("8020", "Graz"), ("8200", "Gleisdorf"), ("8280", "Fürstenfeld"),
    ("8600", "Bruck an der Mur"), ("8700", "Leoben"), ("8940", "Liezen"),
    ("9020", "Klagenfurt"), ("9300", "St. Veit an der Glan"), ("9500", "Villach"),
    ("9800", "Spittal an der Drau"),
    ("01067", "Dresden"), ("01917", "Kamenz"), ("02625", "Bautzen"),
    ("04109", "Leipzig"), ("04651", "Bad Lausick"), ("06108", "Halle (Saale)"),
    ("07545", "Gera"), ("08056", "Zwickau"), ("09111", "Chemnitz"),
    ("10115", "Berlin"), ("10785", "Berlin"), ("12045", "Berlin"),
    ("14467", "Potsdam"), ("15230", "Frankfurt (Oder)"), ("16225", "Eberswalde"),
    ("17033", "Neubrandenburg"), ("18055", "Rostock"), ("19053", "Schwerin"),
    ("20095", "Hamburg"), ("21073", "Hamburg"), ("21335", "Lüneburg"),
    ("22767", "Hamburg"), ("23552", "Lübeck"), ("24103", "Kiel"),
    ("24937", "Flensburg"), ("25813", "Husum"), ("26122", "Oldenburg"),
    ("26721", "Emden"), ("27568", "Bremerhaven"), ("28195", "Bremen"),
    ("29221", "Celle"), ("30159", "Hannover"), ("30880", "Laatzen"),
    ("31134", "Hildesheim"), ("32052", "Herford"), ("33098", "Paderborn"),
    ("33602", "Bielefeld"), ("34117", "Kassel"), ("35037", "Marburg"),
    ("36037", "Fulda"), ("37073", "Göttingen"), ("38100", "Braunschweig"),
    ("39104", "Magdeburg"), ("40213", "Düsseldorf"), ("41061", "Mönchengladbach"),
    ("42103", "Wuppertal"), ("44135", "Dortmund"), ("45127", "Essen"),
    ("46045", "Oberhausen"), ("47051", "Duisburg"), ("48143", "Münster"),
    ("49074", "Osnabrück"), ("50667", "Köln"), ("50937", "Köln"),
    ("51065", "Köln"), ("52062", "Aachen"), ("53111", "Bonn"), ("54290", "Trier"),
    ("55116", "Mainz"), ("56068", "Koblenz"), ("57072", "Siegen"),
    ("58095", "Hagen"), ("59065", "Hamm"), ("60311", "Frankfurt am Main"),
    ("60528", "Frankfurt am Main"), ("61169", "Friedberg"), ("63065", "Offenbach"),
    ("63739", "Aschaffenburg"), ("64283", "Darmstadt"), ("65183", "Wiesbaden"),
    ("65510", "Idstein"), ("66111", "Saarbrücken"), ("67059", "Ludwigshafen"),
    ("67433", "Neustadt an der Weinstraße"), ("68159", "Mannheim"),
    ("69117", "Heidelberg"), ("70173", "Stuttgart"), ("70565", "Stuttgart"),
    ("71032", "Böblingen"), ("72070", "Tübingen"), ("73033", "Göppingen"),
    ("74072", "Heilbronn"), ("75172", "Pforzheim"), ("76133", "Karlsruhe"),
    ("77652", "Offenburg"), ("78462", "Konstanz"), ("79098", "Freiburg im Breisgau"),
    ("80331", "München"), ("80802", "München"), ("81667", "München"),
    ("82205", "Gilching"), ("83022", "Rosenheim"), ("84028", "Landshut"),
    ("85049", "Ingolstadt"), ("86150", "Augsburg"), ("87435", "Kempten"),
    ("88212", "Ravensburg"), ("89073", "Ulm"), ("90402", "Nürnberg"),
    ("90762", "Fürth"), ("91054", "Erlangen"), ("91522", "Ansbach"),
    ("92224", "Amberg"), ("93047", "Regensburg"), ("94032", "Passau"),
    ("95028", "Hof"), ("96047", "Bamberg"), ("97070", "Würzburg"),
    ("97318", "Kitzingen"), ("98527", "Suhl"), ("99084", "Erfurt"),
    ("99423", "Weimar"),
]
# Orte, die *nicht* im Absender vorkommen — für Kundenanschriften, damit `postcode`
# nicht immer neben demselben Ortsnamen steht.
CUSTOMER_CITIES = [
    ("2130", "Mistelbach"), ("2620", "Neunkirchen"), ("3240", "Mank"),
    ("3910", "Zwettl"), ("4320", "Perg"), ("4840", "Vöcklabruck"),
    ("5230", "Mattighofen"), ("5760", "Saalfelden"), ("6130", "Schwaz"),
    ("6370", "Kitzbühel"), ("6850", "Dornbirn"), ("7210", "Mattersburg"),
    ("8230", "Hartberg"), ("8430", "Leibnitz"), ("8750", "Judenburg"),
    ("9400", "Wolfsberg"), ("9900", "Lienz"), ("3430", "Tulln"),
    ("00000", "Musterstadt"),
    ("03046", "Cottbus"), ("06886", "Lutherstadt Wittenberg"), ("07743", "Jena"),
    ("08523", "Plauen"), ("13467", "Berlin"), ("14776", "Brandenburg an der Havel"),
    ("17489", "Greifswald"), ("18439", "Stralsund"), ("19322", "Wittenberge"),
    ("21614", "Buxtehude"), ("22880", "Wedel"), ("23843", "Bad Oldesloe"),
    ("24534", "Neumünster"), ("25335", "Elmshorn"), ("26382", "Wilhelmshaven"),
    ("27472", "Cuxhaven"), ("28832", "Achim"), ("29525", "Uelzen"),
    ("31785", "Hameln"), ("32423", "Minden"), ("33330", "Gütersloh"),
    ("34346", "Hann. Münden"), ("35390", "Gießen"), ("36251", "Bad Hersfeld"),
    ("37154", "Northeim"), ("38440", "Wolfsburg"), ("39576", "Stendal"),
    ("40878", "Ratingen"), ("41236", "Mönchengladbach"), ("42651", "Solingen"),
    ("44623", "Herne"), ("45657", "Recklinghausen"), ("46483", "Wesel"),
    ("47798", "Krefeld"), ("48431", "Rheine"), ("49477", "Ibbenbüren"),
    ("51373", "Leverkusen"), ("52349", "Düren"), ("53757", "Sankt Augustin"),
    ("54634", "Bitburg"), ("55411", "Bingen am Rhein"), ("56410", "Montabaur"),
    ("57290", "Neunkirchen"), ("58452", "Witten"), ("59227", "Ahlen"),
    ("61350", "Bad Homburg"), ("63450", "Hanau"), ("64625", "Bensheim"),
    ("65428", "Rüsselsheim"), ("66424", "Homburg"), ("67655", "Kaiserslautern"),
    ("68723", "Schwetzingen"), ("69469", "Weinheim"), ("71634", "Ludwigsburg"),
    ("72764", "Reutlingen"), ("73430", "Aalen"), ("74523", "Schwäbisch Hall"),
    ("75417", "Mühlacker"), ("76646", "Bruchsal"), ("77933", "Lahr"),
    ("78628", "Rottweil"), ("79539", "Lörrach"), ("82256", "Fürstenfeldbruck"),
    ("83278", "Traunstein"), ("84503", "Altötting"), ("85354", "Freising"),
    ("86720", "Nördlingen"), ("87700", "Memmingen"), ("88400", "Biberach"),
    ("89231", "Neu-Ulm"), ("91301", "Forchheim"), ("92637", "Weiden"),
    ("93413", "Cham"), ("94315", "Straubing"), ("95444", "Bayreuth"),
    ("96450", "Coburg"), ("97421", "Schweinfurt"), ("97980", "Bad Mergentheim"),
    ("98617", "Meiningen"), ("99734", "Nordhausen"),
]
STREETS = [
    "Bahnhofstraße", "Industriestraße", "Gewerbepark", "Hauptstraße", "Lindenweg",
    "Am Mühlbach", "Feldgasse", "Kirchenplatz", "Ringstraße", "Obere Donaulände",
    "Handelskai", "Sonnenallee", "Im Gewerbegebiet", "Schmiedgasse", "Rosenweg",
    "Ahornweg", "Akazienstraße", "Alte Poststraße", "Am Anger", "Am Bahndamm",
    "Am Fischmarkt", "Am Graben", "Am Hafen", "Am Kirchberg", "Am Markt",
    "Am Sportplatz", "Am Steinbruch", "Am Wasserturm", "An den Eichen",
    "An der Lände", "An der Mühle", "Aspernstraße", "Bachstraße", "Bäckergasse",
    "Beethovenstraße", "Bergstraße", "Birkenallee", "Blumenstraße", "Brauhausgasse",
    "Breslauer Straße", "Brückenstraße", "Buchenweg", "Burggasse", "Carl-Benz-Straße",
    "Daimlerstraße", "Dieselstraße", "Domplatz", "Dorfstraße", "Eichendorffweg",
    "Einsteinstraße", "Erlenweg", "Eschenweg", "Fabrikstraße", "Färbergasse",
    "Fasangasse", "Feldkirchner Straße", "Fichtenweg", "Fliederweg", "Forststraße",
    "Frankfurter Straße", "Franz-Josefs-Kai", "Friedhofweg", "Friedrichstraße",
    "Gartenstraße", "Gaswerkstraße", "Georg-Ohm-Straße", "Gerberstraße",
    "Goethestraße", "Grabenweg", "Gutenbergstraße", "Hafenstraße", "Hainburger Straße",
    "Hans-Sachs-Gasse", "Haydngasse", "Heidestraße", "Hermann-Löns-Weg",
    "Herrengasse", "Hirschgasse", "Hochstraße", "Hofgasse", "Hohenzollernring",
    "Holzmarkt", "Im Bruch", "Im Tal", "Im Winkel", "Industriering",
    "Innstraße", "Jahnstraße", "Josef-Haydn-Gasse", "Kaiserstraße", "Kanalstraße",
    "Karl-Marx-Allee", "Kastanienallee", "Kellergasse", "Keplerstraße", "Klostergasse",
    "Kolpingstraße", "Königsberger Straße", "Kopernikusweg", "Kreuzgasse",
    "Kurfürstendamm", "Landstraße", "Lärchenweg", "Leibnizstraße", "Leopoldstraße",
    "Lerchenfeld", "Liebigstraße", "Lilienweg", "Lindenallee", "Ludwigstraße",
    "Marktplatz", "Maximilianstraße", "Mozartgasse", "Mühlgasse", "Münchner Straße",
    "Nelkenweg", "Neubaugasse", "Neue Heimat", "Nordring", "Obere Hauptstraße",
    "Ostbahnstraße", "Otto-Hahn-Straße", "Parkstraße", "Pestalozzistraße",
    "Pfarrgasse", "Planckstraße", "Poststraße", "Prager Straße", "Querstraße",
    "Rathausplatz", "Raiffeisenstraße", "Regensburger Straße", "Rheinallee",
    "Robert-Bosch-Straße", "Römerstraße", "Rudolf-Diesel-Straße", "Salzburger Straße",
    "Sandgasse", "Schillerstraße", "Schlossallee", "Schulstraße", "Schützenstraße",
    "Seestraße", "Siemensstraße", "Silcherweg", "Sportplatzweg", "Steinfeldgasse",
    "Stiftsgasse", "Südring", "Tannenweg", "Teichgasse", "Theaterplatz",
    "Tulpenweg", "Ulmenweg", "Unterdorf", "Uhlandstraße", "Viehmarktgasse",
    "Volkertplatz", "Waldstraße", "Wasserburger Straße", "Weinberggasse",
    "Werftstraße", "Westring", "Wiener Straße", "Wiesenweg", "Wilhelmstraße",
    "Zeppelinstraße", "Ziegelweg", "Zum Alten Bahnhof", "Zur Schmiede",
]
BANKS = ["Raiffeisenbank", "Sparkasse", "Volksbank", "Hypo Landesbank", "Commerzbank", "Oberbank",
         "Erste Bank", "Kreissparkasse"]
FIRST_NAMES = ["Maria", "Andreas", "Sabine", "Thomas", "Petra", "Michael", "Christine", "Stefan",
               "Barbara", "Johannes", "Elisabeth", "Markus", "Claudia", "Gerhard", "Ursula",
               "Franz", "Birgit", "Wolfgang", "Katharina", "Hannes", "Doris", "Reinhard"]
CUSTOMERS = ["Gasthaus Zur Alten Post", "Restaurant Seeblick", "Café Central", "Bistro Eck", "Hotel Waldhof",
             "Pizzeria Da Vinci", "Wirtshaus Am Anger", "Bar Kontrast", "Braustüberl Riedl"]
PAYMENT = ["Zahlbar innerhalb von 14 Tagen netto.", "Zahlung binnen 30 Tagen ohne Abzug.",
           "Bei Zahlung innerhalb 8 Tagen 2 % Skonto, 30 Tage netto.",
           "Zahlbar sofort nach Erhalt der Rechnung.", "Lastschrifteinzug erfolgt am 15. des Folgemonats.",
           "Es gelten unsere allgemeinen Geschäftsbedingungen."]

RATES = {
    "AT": {"standard": 2000, "reduced": 1000, "special": 1300},
    "DE": {"standard": 1900, "reduced": 700, "special": 700},
    # Schweiz: 8,1 % / 2,6 % / 3,8 % (Beherbergung). Zwei Nachkommastellen im Satz
    # sind hier die Regel, nicht die Ausnahme — "8,10 %" neben Preisen ist genau die
    # Form, die der Tagger auf echten Belegen als unitPrice gelesen hat.
    "CH": {"standard": 810, "reduced": 260, "special": 380},
}

COLOURS = ["schwarz", "weiß", "rot", "blau", "grün", "grau", "bordeaux", "anthrazit",
           "natur", "silber", "beige", "gelb", "braun"]

# Freizeilen: Beigaben ohne Preis. Menge 1, Preis- und Betragszelle bleiben leer.
FREE_ITEMS = ["Herzlichen Dank für Ihren Auftrag", "Flyer", "Paketbeilage", "Werbeartikel",
              "Gratiszugabe", "Produktprobe", "Aufkleber", "Katalog Frühjahr", "Grußkarte",
              "Warenprobe", "Beigabe", "Kundengeschenk", "Preisliste", "Rezeptkarte"]

# Pfand- und Leergutzeilen: negativer Preis, negativer Positionsbetrag.
DEPOSIT_ITEMS = ["Leergut Rücknahme", "Pfand Kiste 0,33 l", "Leergut Fass", "Pfand Rückgabe",
                 "Gebindepfand Gutschrift", "Leergut Flaschen", "Pfand Paletten"]

SHIPPERS = ["DHL", "GLS", "DPD", "UPS", "Post AG", "Spedition", "Selbstabholung", "Eigenlieferung",
            "Hermes", "Nachtexpress"]

# Der Werbesatz über dem Absender: Versalien, Akzentfarbe, lang — und kein Name.
CLAIMS = ["GROSSHANDEL FÜR {trade}", "IHR PARTNER FÜR {trade}", "{trade} AUS EINER HAND",
          "QUALITÄT UND SERVICE SEIT ÜBER 40 JAHREN", "BELIEFERUNG VON GASTRONOMIE UND HANDEL",
          "{trade} — FRISCH GELIEFERT", "DER SPEZIALIST FÜR {trade}"]

# ---------------------------------------------------------------- v11

# Fließtext nach dem Summenblock und in der Fußzeile. Er sieht einer Freizeile zum
# Verwechseln ähnlich (Text ohne Zahlen in einer eigenen Zeile), und genau daraus hat
# v10 Positionen erfunden: `Vielen Dank für Ihren Auftrag!` bekam Rolle `line-item`
# und `name` mit 0,85–0,89 Konfidenz (v10/REPORT.md, Lücke 3). Er ist `O` und trägt
# die Rolle `footer`.
THANKS = ["Vielen Dank für Ihren Auftrag!", "Herzlichen Dank für Ihren Einkauf.",
          "Wir danken für Ihren Auftrag und freuen uns auf die weitere Zusammenarbeit.",
          "Vielen Dank für Ihr Vertrauen.", "Wir bedanken uns für Ihre Bestellung!",
          "Danke für Ihren Einkauf — bis zum nächsten Mal.",
          "Bitte überweisen Sie den Rechnungsbetrag unter Angabe der Rechnungsnummer.",
          "Es gelten unsere allgemeinen Geschäftsbedingungen.",
          "Die Ware bleibt bis zur vollständigen Bezahlung unser Eigentum.",
          "Beanstandungen bitte innerhalb von 8 Tagen schriftlich melden.",
          "Rückfragen richten Sie bitte an Ihren Ansprechpartner.",
          "Diese Rechnung wurde maschinell erstellt und ist ohne Unterschrift gültig."]

# Regionale und zweitsprachige Schlüssel für `spec["key_region"]`. Die Klassen ändern
# sich dadurch nicht: `Invoice No.` ist `numberLabel`, `Amount due` als Beschriftung des
# Bruttobetrags ist `grossLabel`. Rund 7 % der Vorlagen ziehen einen dieser Sätze.
#
# Die *Sprache* einer Vorlage (`spec["lang"]`, en/nl/it/pl) gehört dagegen den Familien
# und wird in `families.apply_lang` gesetzt; `blocks.alt` fasst sie nicht an.
ALT_LABELS = {
    "en": {
        "number": ["Invoice No.", "Invoice no.", "Invoice Number", "Inv. No."],
        "date": ["Invoice date", "Date", "Invoice Date", "Date of invoice"],
        "customer": ["Customer No.", "Customer number", "Account No.", "Client No."],
        "order": ["Order No.", "Your order", "Purchase Order", "PO No."],
        "delivery": ["Delivery date", "Date of supply", "Service date"],
        "due": ["Due date", "Payment due", "Due"],
        "orderdate": ["Order date", "Date of order"],
        "service": ["Service period", "Period of supply"],
        "deliverynote": ["Delivery Note No.", "Despatch note", "DN No."],
        "net": ["Net", "Net amount", "Subtotal", "Total net"],
        "gross": ["Total", "Total amount", "Amount due", "Grand Total"],
        "vat": ["VAT", "VAT %", "Tax", "Sales tax"],
        "duetotal": ["Amount due", "Balance due", "Outstanding"],
        "paid": ["Paid", "Prepayment", "Already paid"],
        "subtotal": ["Subtotal", "Goods value", "Items total"],
    },
    "at": {
        "number": ["Rechnungsnr.", "Re.-Nr.", "Rechnungsnummer"],
        "date": ["Rechnungsdatum", "Belegdatum", "Datum"],
        "customer": ["Kundennr.", "Kd-Nr.", "Debitorennr."],
        "order": ["Bestellnr.", "Auftragsnr.", "Ihre Bestellung"],
        "delivery": ["Leistungsdatum", "Lieferdatum", "Leistungszeitraum"],
        "due": ["Fälligkeitsdatum", "Zahlbar bis", "Fällig am"],
        "orderdate": ["Bestelldatum", "Bestellt am"],
        "service": ["Leistungszeitraum", "Leistungszeit"],
        "deliverynote": ["Lieferscheinnr.", "LS-Nr."],
        "net": ["Nettobetrag", "Summe netto", "Warenwert netto"],
        "gross": ["Rechnungsbetrag", "Gesamtbetrag brutto", "Zu zahlender Betrag"],
        "vat": ["USt", "Umsatzsteuer", "USt."],
        "duetotal": ["Zahlbetrag", "Restbetrag", "Offener Betrag"],
        "paid": ["Anzahlung", "Bereits beglichen", "Akonto"],
        "subtotal": ["Warenwert", "Zwischensumme", "Summe Positionen"],
    },
    "ch": {
        "number": ["Rechnungsnummer", "Rechnungs-Nr.", "Beleg-Nr."],
        "date": ["Rechnungsdatum", "Datum"],
        "customer": ["Kundennummer", "Kunden-Nr."],
        "order": ["Bestellnummer", "Auftrag"],
        "delivery": ["Lieferdatum", "Leistungsdatum"],
        "due": ["Fällig per", "Zahlbar bis", "Zahlungsfrist"],
        "orderdate": ["Bestelldatum"],
        "service": ["Leistungszeitraum"],
        "deliverynote": ["Lieferschein-Nr.", "Lieferscheinnummer"],
        "net": ["Total netto", "Nettobetrag", "Zwischentotal"],
        "gross": ["Total", "Rechnungsbetrag", "Endbetrag"],
        "vat": ["MWST", "MwSt.", "Mehrwertsteuer"],
        "duetotal": ["Restbetrag", "Zahlbetrag"],
        "paid": ["Anzahlung", "Bereits bezahlt"],
        "subtotal": ["Zwischentotal", "Warenwert"],
    },
}

# Lange Lieferantennamen, 4–6 Wörter, die über zwei Zeilen umbrechen — die Form, die
# `Firmenname: Brückner Spirituosen & Barbedarf GmbH` in einem XRechnung-Ausdruck
# annimmt. Jedes Wort davon ist `supplier`, auch das `&`.
TRADE_WORDS = ["Spirituosen", "Barbedarf", "Gastro", "Getränke", "Feinkost", "Fleischwaren",
               "Backwaren", "Molkereiprodukte", "Obst", "Gemüse", "Tiefkühlkost", "Weinhandel",
               "Kaffee", "Hygiene", "Gastronomiebedarf", "Großküchentechnik", "Convenience",
               "Frischdienst", "Zustellservice", "Lebensmittel", "Handel", "Vertrieb",
               "Import", "Export", "Service", "Logistik", "Partyservice", "Catering"]

# Privatkunden: auf Kassenbons und B2C-Rechnungen ist der `buyer` keine Firma.
PRIVATE_CUSTOMERS = ["Herr", "Frau", "Familie"]

# UN/ECE-Codes, wie sie ein E-Rechnungsausdruck *statt* des deutschen Einheitenworts
# druckt. v10 kannte nur deutsche Wörter, also hat das Modell gelernt, dass ein
# vierstelliger Großbuchstabencode keine Einheit ist (v10/REPORT.md, Lücke 6).
UNIT_CODE_TEXT = ["XBO", "XCT", "H87", "KGM", "LTR", "PCE", "C62", "GRM", "MLT", "XPK", "XBX"]


# Schweizer Lieferanten für die Familie `swiss`: vierstellige PLZ wie in Österreich,
# aber CHE-Nummer, CH-IBAN und +41. Ohne einen eigenen Topf wäre jeder Schweizer Beleg
# im Korpus ein österreichischer mit getauschter Währung.
CH_CITIES = [("8001", "Zürich"), ("3011", "Bern"), ("4051", "Basel"), ("6003", "Luzern"),
             ("9000", "St. Gallen"), ("1201", "Genf"), ("6900", "Lugano"),
             ("7000", "Chur"), ("2502", "Biel"), ("5000", "Aarau"), ("8400", "Winterthur"),
             ("1700", "Freiburg")]
CH_STREETS = ["Bahnhofstrasse", "Industriestrasse", "Hauptstrasse", "Seestrasse",
              "Gewerbestrasse", "Dorfstrasse", "Rheinstrasse", "Alte Landstrasse"]
CH_BANKS = ["Kantonalbank", "Raiffeisen", "Migros Bank", "PostFinance", "Valiant"]


# ------------------------------------------------------- v11, Werbesatz (tagline)
# Der Werbesatz in *gemischter* Schreibung, wie ihn ein echter Briefkopf direkt unter
# oder neben den Lieferantennamen setzt:
#
#     METZGEREI HOFMANN
#     Inh. Georg Hofmann   Fleisch und Wurst aus eigener Schlachtung
#
# Bis v11 kannte der Korpus den Werbesatz nur in *einer* Gestalt: VERSALIEN in
# Akzentfarbe oben rechts (`CLAIMS`, `blocks.head_block`, `render.py .claim`). Eine
# gemischt gesetzte Zeile neben dem Namen hat er nie als `O` gezeigt — und v11 hat
# daneben lange, mehrwortige Lieferantennamen beigebracht (`sender_keyed`, 4–6 Wörter).
# Auf 26 von 109 echten Scans hängte das v11-Modell den Werbesatz deshalb mit 0,83–0,91
# Konfidenz an den Namen: `METZGEREI HOFMANN Fleisch und Wurst aus eigener Schlachtung`.
# **Jedes Wort dieser Sätze ist `O`.** Sie stehen immer in einem eigenen Span und in
# einer eigenen Zeile oder mit deutlichem Abstand neben der Inhaberzeile — nie in
# einem `supplier`-Lauf.
#
# `{trade}` wird aus `TRADES[category]` gefüllt, `{year}` mit einer Jahreszahl.
SLOGANS = [
    "Ihr Partner für {trade}",
    "Ihr Partner für Gastronomie und Handel",
    "Ihr Spezialist für {trade}",
    "Fachgroßhandel für {trade}",
    "Kompetenz in {trade}",
    "{trade} seit {year}",
    "{trade} — frisch geliefert",
    "{trade} aus einer Hand",
    "Qualität aus Meisterhand",
    "Qualität, die man schmeckt",
    "Familienbetrieb seit {year}",
    "Seit {year} in Familienbesitz",
    "Tradition und Qualität seit {year}",
    "Zuverlässig liefern seit {year}",
    "Wir beliefern Gastronomie, Hotellerie und Handel",
    "Wir liefern täglich frisch",
    "Frische · Qualität · Service",
    "Regional · Frisch · Zuverlässig",
    "Handel | Import | Zustellung",
    "Beratung · Lieferung · Service",
    "Gute Ware — fairer Preis",
    "Aus der Region — für die Region",
    "Der Großhandel für Gastronomie und Gewerbe",
    "Ihr Lieferant für Küche und Keller",
    "Persönliche Beratung, prompte Lieferung",
    "Bestellungen bis 18 Uhr — Lieferung am Folgetag",
    "Alles für Ihren Betrieb",
    "Partner des Fachhandels seit {year}",
]

# Fachspezifische Sätze. Sie sind der eigentliche Punkt der Achse: `Fleisch und Wurst
# aus eigener Schlachtung` ist genau die Zeile, an der das v11-Modell gescheitert ist.
SLOGAN_TRADE = {
    "metzgerei": ["Fleisch und Wurst aus eigener Schlachtung",
                  "Hausmacher Wurstwaren aus eigener Produktion",
                  "Fleischerei mit eigener Schlachtung",
                  "Wurstspezialitäten nach Hausrezept",
                  "Fleisch · Wurst · Feinkost"],
    "baeckerei": ["Frische Backwaren aus der Region",
                  "Handwerksbäckerei mit eigener Konditorei",
                  "Täglich frisch gebacken",
                  "Brot und Gebäck aus dem Holzofen",
                  "Backwaren · Konditorei · Snacks"],
    "molkerei": ["Molkereiprodukte aus bäuerlicher Erzeugung",
                 "Käse und Molkereiwaren für die Gastronomie",
                 "Frische aus der Molkerei",
                 "Milch · Käse · Feinkost"],
    "gemuese": ["Obst · Gemüse · Feinkost",
                "Frischeservice für Küche und Kantine",
                "Obst und Gemüse vom Erzeugermarkt",
                "Täglich frisch vom Großmarkt"],
    "getraenke": ["Getränke-Fachgroßhandel seit {year}",
                  "Getränke für Gastronomie und Feste",
                  "Der Getränkelieferant Ihrer Region",
                  "Bier, Wein und alkoholfreie Getränke",
                  "Getränke · Verleih · Zustellung"],
    "wein": ["Weine aus Österreich und aller Welt",
             "Weinhandel mit eigener Vinothek",
             "Wein · Sekt · Spirituosen",
             "Weinhandlung seit {year}"],
    "spirituosen": ["Spirituosen und Barbedarf für die Gastronomie",
                    "Alles für die Bar — aus einer Hand",
                    "Barbedarf · Spirituosen · Zubehör",
                    "Der Barausstatter für Profis"],
    "kaffee": ["Kaffeerösterei mit eigener Manufaktur",
               "Kaffee, Tee und Kakao für die Gastronomie",
               "Täglich frisch geröstet",
               "Rösterei seit {year}"],
    "nonfood": ["Hygiene und Bedarf für Großküchen",
                "Reinigung · Hygiene · Verpackung",
                "Alles für Küche und Service",
                "Ihr Ausstatter für Gastronomie und Hotellerie"],
    "cc": ["Der Abholgroßmarkt für Gewerbetreibende",
           "Cash & Carry für Gastronomie und Handel",
           "Großhandel für Profis",
           "Abholen · Sparen · Liefern lassen"],
}


# ================================================================= v12: Namensvielfalt
#
# Der Kern von v12: der gedruckte Artikelname wird aus Bausteinen gesetzt, nicht aus
# einer Liste gezogen. `content.article_name` kombiniert Marke, Güte, Herkunft,
# Zuschnitt, Größe und Schreibform; aus 1162 Basen werden so Zehntausende gedruckter
# Namen, und die Bezeichnungsspalte ist nicht mehr an ihrem Wortschatz erkennbar.

# Erfundene Marken. Keine echte Marke — der Korpus darf keine Firmennamen lernen, die
# auf echten Belegen anders stehen. Sie stehen vor oder hinter der Basis.
BRANDS = [
    "Almgut", "Alpenhof", "Bachtal", "Bergquell", "Burgstein", "Dornhof", "Eichkamp",
    "Felsenau", "Frischtal", "Goldbach", "Grünwiesen", "Hartenberg", "Hofmark",
    "Kirchsee", "Krautheim", "Lindenhof", "Mühlfeld", "Nordsee-Kontor", "Ostried",
    "Prantlhof", "Quellhof", "Rauchbach", "Rosenhain", "Sandgrub", "Steinbrück",
    "Talblick", "Ulmenhof", "Vierlinden", "Waldeck", "Zellberg", "Casa Verdi",
    "Bella Riva", "Tenuta Sole", "Maison Clair", "Le Marché", "La Provence",
    "Don Ramiro", "El Molino", "Alta Vista", "Oakfield", "Highfield", "Silverbrook",
    "PRIMA GUSTO", "TERRA NOVA", "VITAFRESCH", "GASTROLINE", "PROFI-LINE",
    "SELECTA", "CLASSIC LINE", "BASIC", "EXKLUSIV", "PREMIUM SELECTION",
]

# Güteklassen und Qualitätsangaben.
GRADES = ["Klasse I", "Klasse II", "Kl. I", "Kl. II", "Handelsklasse A",
          "Handelsklasse B", "HKL I", "Güteklasse I", "Extra", "Premium", "Select",
          "Standard", "Gastro", "Profi", "1. Wahl", "2. Wahl", "Sortierung 55+",
          "Sortierung 70/80", "Kaliber 3", "Kaliber 5", "Größe 2", "Größe 4",
          "Qualität A", "Auslese"]

# Herkunftsangaben, wie sie hinter dem Namen stehen.
ORIGINS = ["DE", "AT", "IT", "ES", "FR", "NL", "BE", "PL", "GR", "PT", "DK", "CH",
           "Deutschland", "Österreich", "Italien", "Spanien", "Frankreich",
           "Niederlande", "Griechenland", "Polen", "Dänemark", "Schweiz",
           "Bayern", "Tirol", "Steiermark", "Allgäu", "Schwarzwald", "Nordsee",
           "Ostsee", "Alpenraum", "Region", "Herkunft DE", "Herkunft AT",
           "Ursprung ES", "aus Bayern", "aus Tirol", "vom Bodensee", "Import"]

# Zuschnitte und Zurichtungen — Fleisch, Fisch, Geflügel.
# Fisch wird anders zugerichtet als Rind. Ohne die Trennung druckt der Korpus
# "Matjesfilet Nuss" — und auch wenn der Tagger die Bedeutung nicht liest, soll eine
# Musterseite jemandem vorgelegt werden können, ohne dass sie albern aussieht.
FISH_CUTS = ["ohne Haut", "mit Haut", "grätenfrei", "Filet", "Rückenfilet", "Loin",
             "Tranchen", "Scheiben 5 mm", "Würfel 20 mm", "küchenfertig", "ausgenommen",
             "geschuppt", "portioniert 180 g", "Steaks à 200 g", "Seite", "Stücke",
             "handfiletiert"]
CUTS = ["natur", "ausgelöst", "pariert", "ohne Knochen", "mit Knochen", "Rose",
        "Oberschale", "Unterschale", "Kugel", "Nuss", "Hüfte", "Bug", "Keule",
        "Karree", "Filetkopf", "Mittelstück", "Endstück", "Würfel 20 mm",
        "Streifen", "Scheiben 3 mm", "Scheiben 5 mm", "Medaillons", "Steaks à 200 g",
        "Steaks à 250 g", "portioniert 180 g", "geschnitten 4x4", "gewolft",
        "doppelt gewolft", "am Stück", "aufgeschnitten", "Tranchen"]

# Fremdsprachige Namenszusätze — italienische, französische und spanische Deli-Ware
# steht auf echten Lieferantenrechnungen unübersetzt.
FOREIGN_TAILS = ["al naturale", "sott'olio", "in salamoia", "affumicato", "stagionato",
                 "extra vergine", "tipo 00", "al tartufo", "alla contadina",
                 "aux fines herbes", "à l'ancienne", "fumé", "confit", "demi-sec",
                 "sous vide", "au poivre", "de campagne", "al ajillo", "en aceite",
                 "picante", "ahumado", "ibérico de bellota", "cortado a mano",
                 "smoked", "sliced", "ready to bake", "chilled", "frozen"]

# Fettgehalt / Alkoholgehalt / Trockenmasse: Prozentangaben *im Namen*. Eine Zahl mit
# Prozentzeichen mitten in der Bezeichnungsspalte sieht aus wie ein Steuersatz.
# Fettgehalt (Index 0 bis FAT_END) und Alkoholgehalt (ab FAT_END). Ein
# "Chili con Carne 38 % vol" ist keine Position, die es gibt.
PERCENTS = ["0,1 %", "0,3 %", "1,5 %", "1,8 %", "3,5 %", "3,8 %", "10 %", "15 %",
            "20 %", "24 %", "30 %", "32 %", "35 %", "40 %", "45 % F.i.Tr.",
            "48 % F.i.Tr.", "50 % F.i.Tr.", "55 % F.i.Tr.", "60 %", "70 %",
            # ab hier Alkohol (FAT_END)
            "11,5 % vol", "12,0 % vol", "12,5 % vol", "13,5 % vol", "14,0 % vol",
            "4,8 % vol", "5,2 % vol", "37,5 % vol", "38 % vol", "40 % vol",
            "43 % vol", "47,3 % vol", "54,2 % vol"]
FAT_END = PERCENTS.index("11,5 % vol")
FAT_PERCENTS = PERCENTS[:FAT_END]
VOL_PERCENTS = PERCENTS[FAT_END:]

# Mehrstückgebinde, die *vor* dem Namen stehen: "12er Tray Cola 0,33",
# "6er Pack Mineralwasser". Damit beginnt die Bezeichnung mit einer Zahl — und eine
# Zahl am Anfang einer Zelle ist das, was der Tagger als Menge liest.
LEADING_PACKS = ["6er", "8er", "10er", "12er", "20er", "24er", "30er", "4er", "2er",
                 "6er Tray", "12er Tray", "24er Tray", "6er Pack", "12er Pack",
                 "6er Karton", "12er Kiste", "20er Kiste", "24er Kiste", "6er VE",
                 "12 x", "24 x", "6 x", "4 x", "10 x", "20 x"]

# --------------------------------------------------------------- v12: Wein und Sekt
#
# Fehlerbild 1 aus v12/PLAN.md. Der echte Beleg druckt den Jahrgang **vor** dem Namen
# (`2024 Iphöfer Kronsberg Silvaner trocken 0,75 l`), führt deutsche Einzellagen statt
# österreichischer Rieden und codiert die Artikelnummer mit dem Jahrgang (`2024-S-01`).
# v11 kannte nur "Grüner Veltliner Ried Hochrain 2022" — Jahrgang am Ende, Lage
# österreichisch, Artikelnummer ohne Jahr. Auf dem echten Weingut-Beleg hat das Modell
# daraufhin den Jahrgang als Menge und die Artikelnummer als Jahrgang gelesen.
WINE_REGIONS = ["Franken", "Pfalz", "Mosel", "Rheingau", "Rheinhessen", "Baden",
                "Württemberg", "Nahe", "Ahr", "Mittelrhein", "Saale-Unstrut",
                "Sachsen", "Hessische Bergstraße", "Wachau", "Kamptal", "Kremstal",
                "Weinviertel", "Burgenland", "Südsteiermark", "Wagram", "Traisental",
                "Thermenregion", "Neusiedlersee", "Leithaberg", "Carnuntum",
                "Vulkanland Steiermark"]
# Einzellagen. Die Form "<Ort>er <Lage>" ist der Kern des Fehlerbilds: zwei Wörter, die
# wie ein Eigenname aussehen und in keiner Wortliste stehen.
WINE_SITES = [
    "Iphöfer Kronsberg", "Iphöfer Julius-Echter-Berg", "Escherndorfer Lump",
    "Würzburger Stein", "Würzburger Abtsleite", "Randersackerer Pfülben",
    "Randersackerer Teufelskeller", "Casteller Schlossberg", "Rödelseer Küchenmeister",
    "Volkacher Ratsherr", "Sommeracher Katzenkopf", "Thüngersheimer Scharlachberg",
    "Homburger Kallmuth", "Forster Ungeheuer", "Forster Jesuitengarten",
    "Deidesheimer Herrgottsacker", "Deidesheimer Hohenmorgen",
    "Wachenheimer Gerümpel", "Ruppertsberger Reiterpfad", "Ungsteiner Herrenberg",
    "Kallstadter Saumagen", "Bernkasteler Doctor", "Bernkasteler Badstube",
    "Piesporter Goldtröpfchen", "Ürziger Würzgarten", "Erdener Treppchen",
    "Wehlener Sonnenuhr", "Graacher Himmelreich", "Brauneberger Juffer",
    "Trittenheimer Apotheke", "Rüdesheimer Berg Rottland", "Rüdesheimer Berg Schlossberg",
    "Johannisberger Klaus", "Hattenheimer Wisselbrunnen", "Erbacher Marcobrunn",
    "Rauenthaler Baiken", "Kiedricher Gräfenberg", "Niersteiner Pettenthal",
    "Niersteiner Hipping", "Oppenheimer Sackträger", "Westhofener Morstein",
    "Dalsheimer Hubacker", "Ihringer Winklerberg", "Achkarrer Schlossberg",
    "Durbacher Plauelrain", "Oberrotweiler Eichberg", "Untertürkheimer Gips",
    "Stettener Pulvermächer", "Schwaigerner Ruthe", "Fellbacher Lämmler",
    "Schlossböckelheimer Kupfergrube", "Niederhäuser Hermannshöhle",
    "Loibner Steinertal", "Dürnsteiner Kellerberg", "Spitzer Singerriedel",
    "Zöbinger Heiligenstein", "Gaisberg", "Ried Hochrain", "Ried Lamm",
    "Ried Achleiten", "Ried Kirchweingarten",
]
# Betriebsformen im Weinbau — vor dem Nachnamen.
WINE_ESTATE_HEADS = ["Weingut", "Weingut", "Weinbau", "Winzerhof", "Weinhof",
                     "Weingut & Gutsschänke", "Sektgut", "Sektkellerei",
                     "Winzergenossenschaft", "Winzerkeller", "Staatsweingut",
                     "Schlossgut", "Klostergut", "Domäne"]
# Qualitäts- und Geschmacksangaben, die im Weinnamen nach der Rebsorte stehen.
WINE_QUALITY = ["trocken", "trocken", "halbtrocken", "feinherb", "lieblich",
                "edelsüß", "Kabinett trocken", "Spätlese trocken", "Spätlese",
                "Auslese", "QbA trocken", "Prädikatswein", "Erste Lage",
                "Grosses Gewächs", "Alte Reben", "Reserve", "Selection",
                "Classic", "Ortswein", "Gutswein", "Lagenwein"]
# Schaumwein: "Sommer Sekt b.A. brut 0,75 l".
SEKT_FORMS = ["Sekt b.A. brut", "Sekt b.A. extra trocken", "Sekt b.A. trocken",
              "Winzersekt brut", "Winzersekt extra brut", "Sekt trocken",
              "Riesling Sekt brut", "Rosé Sekt brut", "Crémant brut",
              "Blanc de Blancs brut", "Perlwein trocken", "Secco trocken",
              "Frizzante", "Hugo Secco"]
# Rebsorten für die jahrgangsvorangestellte Form. Kürzer als CATEGORIES["wein"]["bases"]:
# hier stehen nur die, die wirklich als Lagenwein auftreten.
WINE_GRAPES = ["Silvaner", "Riesling", "Müller-Thurgau", "Weißburgunder",
               "Grauburgunder", "Chardonnay", "Scheurebe", "Bacchus", "Kerner",
               "Rieslaner", "Traminer", "Sauvignon Blanc", "Spätburgunder",
               "Domina", "Dornfelder", "Portugieser", "Lemberger", "Trollinger",
               "Schwarzriesling", "Regent", "Frühburgunder", "St. Laurent",
               "Zweigelt", "Blaufränkisch", "Grüner Veltliner", "Roter Veltliner",
               "Neuburger", "Rotling", "Blanc de Noir", "Cuvée weiß", "Cuvée rot"]
WINE_VINTAGES = ["2018", "2019", "2020", "2021", "2021", "2022", "2022", "2023",
                 "2023", "2023", "2024", "2024", "2024", "2025"]

# ----------------------------------------------------- v12: Umsatzsteuer im Summenblock
#
# Fehlerbild 4. Der echte Beleg druckt den Satz **in der Beschriftung**:
#
#     USt. gesamt 7%      37,03
#     USt. gesamt 19%      2,83
#
# Die Beschriftungswörter sind `vatLabel`, die Zahl `7` ist `vat`, das `%` und der
# Bemessungsbetrag sind `O` (CONVENTIONS.md §1). Bis v11 druckte der Korpus nur
# `MwSt 19 %` — ein Wort, ein Satz, ein Betrag; die Form mit dem Satz *im*
# Beschriftungslauf und zwei Zeilen übereinander kannte er nicht, und das Modell hat
# den Satz der zweiten Zeile auf echten Belegen gar nicht getaggt.
VAT_TOTAL_LABELS = [
    "USt. gesamt", "USt gesamt", "MwSt. gesamt", "MwSt gesamt", "Umsatzsteuer gesamt",
    "Summe USt.", "Summe MwSt.", "enthaltene USt", "enthaltene MwSt.",
    "darin enthalten USt.", "zzgl. MwSt.", "zzgl. USt.", "zuzüglich MwSt.",
    "Steuerbetrag", "USt.-Betrag", "MwSt.-Betrag", "Umsatzsteuer",
]
# Wie der Bemessungsbetrag hinter dem Satz angeschrieben wird.
VAT_BASE_JOINS = ["auf", "von", "aus", "Bemessung", "BMG", "Netto", "Basis"]

# ------------------------------------------------------------ v12: Mengen-Einheiten
#
# Fehlerbild 6. Einheitenwörter, die in der Mengenzelle direkt neben der Zahl stehen —
# teils mit Leerzeichen, teils verklebt (`17Fl`, `10XBO`).
# Nur Wörter, die `tools/units.py` auflöst — der Korpus darf keinen Code erfinden
# (`units.code` wirft sonst, und das ist die Absicht).
QTY_UNIT_WORDS = ["Stk", "Stk.", "St.", "Stück", "Fl", "Fl.", "Kt", "Kt.", "Karton",
                  "Kiste", "Pkg", "Pack", "Btl", "Btl.", "Dose", "Eimer", "Kanister",
                  "Sack", "Bund", "Rolle", "Fass", "kg", "g", "l", "ml"]


# ------------------------------------------------- v12: der Werbesatz, breiter gefasst
#
# v11 hatte 28 allgemeine und 34 fachliche Sätze. Bei 30 % Vorlagen mit Werbesatz und
# 15 000 Variationen druckt der Korpus damit jeden Satz siebzigmal — das Modell lernt
# die Sätze auswendig statt „mehrwortige Zeile am Briefkopf, nicht der Name". v12 hebt
# den Bestand auf über 200. **Jedes Wort bleibt `O`** (CONVENTIONS.md, v11 §Tagline).
SLOGANS += [
    "Frisch auf den Tisch",
    "Weil Qualität zählt",
    "Ihr Fachgroßhandel in der Region",
    "Kompetenz seit {year}",
    "Meisterbetrieb seit {year}",
    "Inhabergeführt seit {year}",
    "Drei Generationen {trade}",
    "In vierter Generation",
    "Das Beste für Ihre Küche",
    "Wir liefern, was Sie brauchen",
    "Bestellen Sie bequem online",
    "Rund um die Uhr für Sie erreichbar",
    "Täglich frische Ware ab Rampe",
    "Zustellung in ganz Österreich",
    "Zustellung in ganz Deutschland",
    "Liefergebiet Süddeutschland",
    "Über 3.000 Artikel ab Lager",
    "Mehr als 5.000 Artikel für die Gastronomie",
    "Vom Erzeuger direkt zu Ihnen",
    "Kurze Wege, frische Ware",
    "Nachhaltig · Regional · Fair",
    "Klimaneutral beliefert",
    "Geprüfte Qualität nach IFS",
    "HACCP-zertifizierter Betrieb",
    "Bio zertifiziert AT-BIO-301",
    "Mitglied der Erzeugergemeinschaft",
    "Ihr Großhandel mit Herz",
    "Service, der schmeckt",
    "Aus Leidenschaft für gutes Essen",
    "Handwerk, das man schmeckt",
    "Mit Sorgfalt ausgewählt",
    "Erst probieren, dann liefern",
    "Preise, die stimmen",
    "Fair kalkuliert",
    "Immer eine Lieferung voraus",
    "Pünktlich. Frisch. Verlässlich.",
    "Wir sind für Sie da",
    "Persönlich und zuverlässig",
    "Der kurze Draht zu Ihrem Lieferanten",
    "Beratung vom Fachmann",
    "Fragen Sie uns — wir wissen Rat",
    "Alles aus einer Hand für Ihren Betrieb",
    "Vom Frühstück bis zum Dinner",
    "Für Küche, Keller und Service",
    "Gastronomie · Hotellerie · Gemeinschaftsverpflegung",
    "Betriebsverpflegung leicht gemacht",
    "Ihr Partner für Großküchen",
    "Von Profis für Profis",
    "Wir wissen, was Köche brauchen",
    "{trade} in bester Qualität",
    "{trade} für Profis",
    "Spezialist für {trade} seit {year}",
    "{trade} · Beratung · Service",
    "{trade} direkt vom Fachhandel",
    "{trade}: unsere Leidenschaft",
    "Ihr Spezialist rund um {trade}",
    "Wir leben {trade}",
    "{trade} mit Tradition",
    "{trade} neu gedacht",
    "Gutes aus der Nachbarschaft",
    "Heimische Erzeuger, faire Preise",
    "Saisonal einkaufen, gut kochen",
    "Frische ist kein Zufall",
    "Qualität beginnt beim Einkauf",
    "Sorgfalt in jedem Schritt",
    "Ausgezeichnet mit dem Landespreis {year}",
    "Familienunternehmen in {year} gegründet",
    "Seit {year} am Markt",
    "Gegründet {year} — bis heute unabhängig",
    "Ihr Lieferant seit über 50 Jahren",
    "Wir bringen es Ihnen",
    "Lieferung frei Haus ab 250 €",
    "Mindestbestellwert 150 € netto",
    "Nachtanlieferung auf Wunsch",
    "Kühlkette lückenlos",
]
SLOGAN_TRADE.update({
    "gefluegel": ["Geflügel aus kontrollierter Aufzucht",
                  "Frisches Geflügel täglich zerlegt",
                  "Geflügel · Wild · Feinkost",
                  "Vom Hof in Ihre Küche"],
    "fisch": ["Fisch und Meeresfrüchte aus nachhaltigem Fang",
              "Täglich frischer Fisch von der Küste",
              "Eigene Räucherei im Haus",
              "Fisch · Krustentiere · Delikatessen"],
    "obst": ["Obst und Südfrüchte aus aller Welt",
             "Täglich frisch vom Großmarkt",
             "Obsthandel seit {year}",
             "Süßes aus Übersee und der Region"],
    "feinkost": ["Italienische Spezialitäten seit {year}",
                 "Antipasti · Pasta · Olivenöl",
                 "Feinkost aus dem Mittelmeerraum",
                 "Delikatessen für die anspruchsvolle Küche"],
    "tiefkuehl": ["Tiefkühlkost für Küche und Kantine",
                  "Lückenlose Kühlkette bis zur Rampe",
                  "TK-Vollsortiment aus einer Hand",
                  "Frostfrisch geliefert"],
    "eis_dessert": ["Eis aus eigener Manufaktur",
                    "Desserts, die überzeugen",
                    "Speiseeis · Torten · Patisserie",
                    "Süßer Abschluss für Ihr Menü"],
    "suesswaren": ["Süßwaren für Handel und Gastronomie",
                   "Confiserie seit {year}",
                   "Naschen mit Niveau",
                   "Schokolade · Gebäck · Knabberartikel"],
    "tee_gewuerze": ["Gewürze aus aller Welt",
                     "Eigene Gewürzmühle im Haus",
                     "Tee · Gewürze · Kräuter",
                     "Würzen wie die Profis"],
    "saefte_alkoholfrei": ["Fruchtsäfte aus eigener Kelterei",
                           "Direktsäfte vom Streuobst",
                           "Kelterei seit {year}",
                           "Saft · Nektar · Schorle"],
    "brauerei": ["Gebraut nach dem Reinheitsgebot",
                 "Privatbrauerei seit {year}",
                 "Bier aus eigener Braustätte",
                 "Fassbier · Flaschenbier · Zapfanlagen"],
    "reinigung_hygiene": ["Hygiene für Großküchen und Gastronomie",
                          "Reinigung · Desinfektion · Beratung",
                          "Sauberkeit ist Vertrauenssache",
                          "Ihr Hygienepartner seit {year}"],
    "verpackung_einweg": ["Verpackungen für Außer-Haus-Verkauf",
                          "Einweg · Mehrweg · nachhaltig",
                          "Verpackungslösungen aus einer Hand",
                          "Vom Becher bis zur Menübox"],
    "gastrobedarf_technik": ["Großküchentechnik und Ausstattung",
                             "Von der Planung bis zum Service",
                             "Küchentechnik · Geschirr · Besteck",
                             "Ausstatter für Hotel und Gastronomie"],
    "waesche_service": ["Textilservice für Hotellerie und Gastronomie",
                        "Mietwäsche · Berufskleidung · Mattenservice",
                        "Wäscherei seit {year}",
                        "Sauber geliefert, pünktlich geholt"],
    "blumen_deko": ["Floristik für Gastronomie und Event",
                    "Blumen · Deko · Tischgestaltung",
                    "Frische Blumen zweimal wöchentlich",
                    "Gärtnerei mit eigener Produktion"],
    "bio_hof": ["Biolebensmittel aus kontrolliert ökologischem Anbau",
                "Vom Biohof direkt in Ihre Küche",
                "Bioland-Partner seit {year}",
                "Öko · Regional · Fair"],
    "catering": ["Catering für jeden Anlass",
                 "Partyservice seit {year}",
                 "Wir kochen für Ihre Gäste",
                 "Buffet · Menü · Service"],
    "tabak": ["Tabakwaren für Handel und Gastronomie",
              "Zigarren · Pfeifen · Zubehör",
              "Fachhandel seit {year}"],
})
SLOGAN_TRADE["wein"] += ["Weingut in Familienbesitz seit {year}",
                         "Eigene Lagen, eigener Keller",
                         "Wein aus Franken und der Pfalz",
                         "Jahrgang für Jahrgang mit Charakter"]
SLOGAN_TRADE["spirituosen"] += ["Edelbrände aus eigener Destillerie",
                                "Gebrannt, nicht gekauft"]
SLOGAN_TRADE["kaffee"] += ["Langzeitröstung im Trommelröster",
                           "Direkt gehandelter Rohkaffee"]


def slogan_count():
    """Wie viele verschiedene Werbesätze der Bestand hergibt — `coverage.py` berichtet
    es, damit eine schrumpfende Liste auffällt."""
    return len(SLOGANS) + sum(len(v) for v in SLOGAN_TRADE.values()) + len(CLAIMS)


def check():
    """Warngründe, die sonst niemandem auffallen: eine Warengruppe ohne Eintrag in
    `sizes.SIZE_MIX` oder `content.GROUPS` fällt still auf die Vorgabe zurück und
    druckt dann für jede Gruppe dieselben Maße."""
    import sizes
    out = []
    for name in CATEGORIES:
        if name not in TRADES:
            out.append(f"{name}: kein TRADES-Eintrag")
        if name not in sizes.SIZE_MIX:
            out.append(f"{name}: kein sizes.SIZE_MIX-Eintrag")
        if name not in TRADE_HEADS:
            out.append(f"{name}: kein TRADE_HEADS-Eintrag")
    return out
