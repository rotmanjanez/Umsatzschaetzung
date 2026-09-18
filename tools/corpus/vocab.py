CATEGORIES = {
    "baeckerei": {
        "bases": ["Brötchen", "Semmel", "Laugenbrezel", "Kaiserbrötchen", "Bauernbrot", "Roggenmischbrot",
                  "Ciabatta", "Baguette", "Croissant", "Nussschnecke", "Apfeltasche", "Kornspitz",
                  "Vollkornbrot", "Dinkelweckerl", "Butterhörnchen", "Mohnflesserl", "Toastbrot"],
        "variants": ["Weizen", "Roggen", "Dinkel", "Vollkorn", "Mehrkorn", "hell", "dunkel", "rustikal",
                     "vorgebacken", "TK", "Mini", ""],
        "sizes": ["40 g", "55 g", "70 g", "90 g", "250 g", "500 g", "750 g", "1 kg", "Btl. 20 Stk"],
        "units": [("Stk", 1), ("Btl", 20), ("Kt", 60)],
        "price": (18, 420),
        "rates": ['reduced'],
    },
    "metzgerei": {
        "bases": ["Schweinerücken", "Schweineschnitzel", "Rinderhüfte", "Rindergulasch", "Hähnchenbrust",
                  "Putenschnitzel", "Bratwurst", "Leberkäse", "Schinkenspeck", "Kalbsrücken", "Faschiertes",
                  "Schweinebauch", "Rinderbeiried", "Hüftsteak", "Grillwürstel"],
        "variants": ["frisch", "TK", "ausgelöst", "pariert", "geschnitten", "mariniert", "grob", "fein", ""],
        "sizes": ["ca. 2 kg", "ca. 5 kg", "1 kg", "2,5 kg", "Vak.", "im Netz", "180 g", "200 g"],
        "units": [("kg", 1), ("Stk", 1), ("Kt", 10)],
        "price": (420, 3800),
        "rates": ['reduced'],
    },
    "molkerei": {
        "bases": ["Vollmilch", "Schlagobers", "Sauerrahm", "Naturjoghurt", "Butter", "Gouda", "Emmentaler",
                  "Bergkäse", "Frischkäse", "Mozzarella", "Parmesan", "Creme fraiche", "Topfen", "Feta"],
        "variants": ["3,5 %", "1,5 %", "36 %", "20 %", "gerieben", "in Scheiben", "am Stück", "laktosefrei", ""],
        "sizes": ["1 l", "250 ml", "500 g", "1 kg", "5 kg", "Becher 180 g", "Block 3 kg"],
        "units": [("l", 1), ("kg", 1), ("Stk", 1), ("Kt", 12)],
        "price": (65, 1900),
        "rates": ['reduced'],
    },
    "gemuese": {
        "bases": ["Speisekartoffel", "Zwiebel", "Karotte", "Eisbergsalat", "Tomate", "Gurke", "Paprika",
                  "Champignon", "Brokkoli", "Zucchini", "Petersilie", "Knoblauch", "Rucola", "Feldsalat", "Apfel"],
        "variants": ["festkochend", "rot", "gelb", "grün", "braun", "Klasse I", "Bio", "gewaschen",
                     "geschnitten", "Rispe", ""],
        "sizes": ["Kiste 10 kg", "Netz 5 kg", "Steige", "Sack 25 kg", "Beutel 1 kg", "500 g", "Kt 6 kg"],
        "units": [("kg", 1), ("Kiste", 1), ("Stk", 1), ("Btl", 1)],
        "price": (55, 890),
        "rates": ['reduced'],
    },
    "getraenke": {
        "bases": ["Mineralwasser", "Apfelsaft", "Orangensaft", "Cola", "Zitronenlimonade", "Almdudler",
                  "Tonic Water", "Eistee", "Holunderblütensirup", "Cola Postmix Sirup", "Bier vom Fass",
                  "Radler", "Weizenbier", "Energy Drink"],
        "variants": ["prickelnd", "still", "zuckerfrei", "naturtrüb", "Mehrweg", "Einweg", "Glas", "PET", ""],
        "sizes": ["0,33 l", "0,5 l", "0,75 l", "1,0 l", "1,5 l", "10 l", "20 l", "30 l", "Kiste 24 x 0,33 l"],
        "units": [("Kiste", 24), ("Fass", 1), ("Kt", 12), ("Stk", 1), ("l", 1)],
        "price": (45, 12500),
        "rates": ['standard', 'reduced'],
    },
    "wein": {
        "bases": ["Grüner Veltliner", "Riesling", "Zweigelt", "Blaufränkisch", "Welschriesling", "Sauvignon Blanc",
                  "Chardonnay", "Weißburgunder", "Merlot", "Prosecco Frizzante", "Sekt brut", "Rosé"],
        "variants": ["Kabinett", "trocken", "halbtrocken", "Reserve", "Klassik", "Ried Hochrain", "DAC", "2022",
                     "2023", "2024", ""],
        "sizes": ["0,75 l", "1,0 l", "Bag in Box 10 l", "Kt 6 Fl.", "Kt 12 Fl."],
        "units": [("Fl", 1), ("Kt", 6), ("Stk", 1)],
        "price": (280, 4800),
        "rates": ['standard'],
    },
    "spirituosen": {
        "bases": ["Wodka", "Gin", "Weißer Rum", "Brauner Rum", "Blended Whisky", "Kräuterlikör", "Aperitivo Bitter",
                  "Obstbrand Marille", "Williamsbirne", "Tequila Silver", "Cognac VS", "Eierlikör"],
        "variants": ["40 % vol", "37,5 % vol", "38 % vol", "Premium", "Standard", "Barflasche", ""],
        "sizes": ["0,5 l", "0,7 l", "1,0 l", "3,0 l", "Kt 6 x 0,7 l"],
        "units": [("Fl", 1), ("Kt", 6), ("Stk", 1)],
        "price": (680, 9800),
        "rates": ['standard'],
    },
    "kaffee": {
        "bases": ["Espresso Bohnen", "Caffè Crema", "Röstkaffee gemahlen", "Kaffeebohnen Hausmischung",
                  "Schwarztee", "Früchtetee", "Trinkschokolade", "Cappuccino Topping"],
        "variants": ["100 % Arabica", "Bar-Mischung", "Bio", "entkoffeiniert", "Beutel", ""],
        "sizes": ["1 kg", "500 g", "250 g", "Kt 6 x 1 kg", "Dose 2 kg"],
        "units": [("kg", 1), ("Kt", 6), ("Stk", 1)],
        "price": (780, 3400),
        "rates": ['standard', 'reduced'],
    },
    "nonfood": {
        "bases": ["Serviette", "Geschirrspültabs", "Handtuchrolle", "Müllsack", "Alufolie", "Frischhaltefolie",
                  "Reinigungsmittel", "Glasreiniger", "Einweghandschuh", "Kerzenteelicht", "Kassenrolle",
                  "Pizzakarton", "Trinkhalm Papier"],
        "variants": ["weiß", "schwarz", "bordeaux", "2-lagig", "3-lagig", "unparfümiert", "Größe M",
                     "Größe L", "24 cm", "33 cm", ""],
        "sizes": ["Pack 250 Stk", "Kt 1000 Stk", "Rolle 300 m", "5 l Kanister", "Karton 6 x 1 l"],
        "units": [("Pack", 1), ("Kt", 1), ("Stk", 1), ("Rolle", 1)],
        "price": (120, 8900),
        "rates": ['standard'],
    },
    "cc": {
        "bases": ["Pommes frites", "Kroketten", "Backerbsen", "Semmelbrösel", "Sonnenblumenöl", "Rapsöl",
                  "Tomatenpaprika", "Essiggurken", "Mayonnaise", "Ketchup", "Senf mittelscharf", "Reis langkorn",
                  "Spaghetti", "Basmatireis", "Zucker", "Salz"],
        "variants": ["TK", "7 mm", "10 mm", "fein", "grob", "Eimer", "Kanister", "Beutel", ""],
        "sizes": ["2,5 kg", "5 kg", "10 kg", "10 l", "Kt 4 x 2,5 kg", "Eimer 5 kg"],
        "units": [("Kt", 4), ("Stk", 1), ("kg", 1), ("Eimer", 1)],
        "price": (95, 4200),
        "rates": ['reduced', 'standard'],
    },
}

SUPPLIER_HEADS = ["Gebr.", "Gasthaus-Service", "Delikatessen", "Feinkost", "Landhof", "Alpen", "Donau", "Wiener",
                  "Steirische", "Tiroler", "Nordwest", "Süd", "Rhein", "Elbe", "Hanse", "Zentral", "Erste",
                  "Regional", "Terra", "Vital", "Prima", "Optima", "Panorama", "Kreis"]
SUPPLIER_NAMES = ["Ahrweiler", "Birnbacher", "Calmeyer", "Dohrmann", "Eckenstein", "Frühwirth", "Gassner",
                  "Hollerbach", "Innerhofer", "Jaklitsch", "Kirchmayr", "Lindtner", "Moosbrugger", "Nussbaumer",
                  "Oberleitner", "Pucher", "Quehenberger", "Rauscher", "Steinbichler", "Trautmann", "Ulrichs",
                  "Vogelsang", "Wallnöfer", "Zehentner"]
SUPPLIER_TAILS = ["GmbH", "GmbH & Co. KG", "KG", "OG", "GesmbH", "e.K.", "AG", "Handels GmbH", "Vertriebs GmbH"]
TRADES = {"baeckerei": "Backwaren", "metzgerei": "Fleischwaren", "molkerei": "Molkereiprodukte",
          "gemuese": "Obst & Gemüse", "getraenke": "Getränkefachhandel", "wein": "Weinhandel",
          "spirituosen": "Spirituosen", "kaffee": "Kaffeerösterei", "nonfood": "Hygiene & Bedarf",
          "cc": "Großhandel"}

STREETS = ["Bahnhofstraße", "Industriestraße", "Gewerbepark", "Hauptstraße", "Lindenweg", "Am Mühlbach",
           "Feldgasse", "Kirchenplatz", "Ringstraße", "Obere Donaulände", "Handelskai", "Sonnenallee",
           "Im Gewerbegebiet", "Schmiedgasse", "Rosenweg"]
CITIES = [("1100", "Wien"), ("4020", "Linz"), ("5020", "Salzburg"), ("8010", "Graz"), ("6020", "Innsbruck"),
          ("9020", "Klagenfurt"), ("80331", "München"), ("70173", "Stuttgart"), ("90402", "Nürnberg"),
          ("60311", "Frankfurt am Main"), ("20095", "Hamburg"), ("50667", "Köln"), ("04109", "Leipzig"),
          ("01067", "Dresden"), ("3100", "St. Pölten"), ("4600", "Wels")]
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

# Postleitzahlen und Orte, die *nicht* aus CITIES kommen — für Kundenanschriften, damit
# `postcode` nicht immer neben demselben Ortsnamen steht.
CUSTOMER_CITIES = [("2500", "Baden"), ("3500", "Krems"), ("4400", "Steyr"), ("6900", "Bregenz"),
                   ("8600", "Bruck an der Mur"), ("9500", "Villach"), ("2340", "Mödling"),
                   ("5400", "Hallein"), ("6330", "Kufstein"), ("7000", "Eisenstadt"),
                   ("10115", "Berlin"), ("30159", "Hannover"), ("40213", "Düsseldorf"),
                   ("45127", "Essen"), ("55116", "Mainz"), ("67059", "Ludwigshafen"),
                   ("76133", "Karlsruhe"), ("86150", "Augsburg"), ("93047", "Regensburg"),
                   ("99084", "Erfurt"), ("28195", "Bremen"), ("34117", "Kassel")]

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
