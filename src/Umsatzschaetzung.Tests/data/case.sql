-- Ein Fall so, wie er in der Falldatei liegt: Zeilen, kein Dokument. Das Schema legt der
-- Fallspeicher an, diese Vorlage trägt nur die Daten.

INSERT OR REPLACE INTO fall(id, label, period_from, period_to, name, tax_number, pab_number, kennzahl, created_at, updated_at, app_version)
VALUES('case.bar.2024', 'Schankwirtschaft Zum Alten Fass, Bp 2024', '2024-01-01', '2024-12-31',
       'Zum Alten Fass Gastronomie GmbH', '214/5711/0832', 'PAB 2024/0417', '',
       '2024-05-02T08:00:00.0000000+00:00', '2024-05-02T08:00:00.0000000+00:00', 'test');

INSERT INTO inventory(ord, ingredient_id, opening, closing, unit) VALUES
    (0, 'ing.bier.fass',     50000, 100000, 'MLT'),
    (1, 'ing.bier.flasche',      0,  39600, 'MLT'),
    (2, 'ing.korn',           1400,   2800, 'MLT');

INSERT INTO invoice(id, ord, source, file_name, supplier_name, number, date, currency,
                    net_total, gross_total, stated_net, stated_gross, verified_at, verified_auto)
VALUES('inv.bar.1', 0, 'zugferd', 'rheinland-2024-04711.pdf', 'Rheinland Getränke Fachgroßhandel GmbH',
       '2024-04711', '2024-03-15', 'EUR', 169000, 201110, NULL, NULL, NULL, NULL);

INSERT INTO invoice_line(invoice_id, ord, no, name, seller_article_id, gtin, quantity, unit_code,
                         unit_price, price_base_qty, line_net, vat, mapping_id) VALUES
    ('inv.bar.1', 0, 1, 'Pils Fass 50 l',           '31090', NULL, 12000, 'XKG', 92500000, 1000, 111000, 1900, 'map.fass50'),
    ('inv.bar.1', 1, 2, 'Pils 24 x 0,33 l Kiste',   '31210', NULL, 30000, 'XCS', 13200000, 1000,  39600, 1900, 'map.kiste24x033'),
    ('inv.bar.1', 2, 3, 'Doppelkorn 0,7 l 38% vol', '55120', NULL, 20000, 'XBO',  9200000, 1000,  18400, 1900, 'map.korn07');

INSERT INTO case_product(product_id, ord, gross_price, vat) VALUES
    ('prod.pils.03',      0, 320, 1900),
    ('prod.pils.05',      1, 450, 1900),
    ('prod.pils.flasche', 2, 350, 1900),
    ('prod.korn.2cl',     3, 180, 1900),
    ('prod.korn.4cl',     4, 320, 1900);
