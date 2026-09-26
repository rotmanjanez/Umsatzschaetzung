CREATE TABLE case_mapping(id TEXT PRIMARY KEY, supplier_name TEXT, supplier_article_id TEXT, gtin TEXT,
    name TEXT, observed TEXT, unit_code TEXT, ingredient_id TEXT NOT NULL, factor INTEGER) WITHOUT ROWID;
CREATE TABLE case_product(product_id TEXT PRIMARY KEY, ord INTEGER NOT NULL, gross_price INTEGER NOT NULL, vat INTEGER NOT NULL,
    recipe_basis INTEGER) WITHOUT ROWID;
CREATE TABLE case_recipe(product_id TEXT NOT NULL, ord INTEGER NOT NULL, ingredient_id TEXT NOT NULL,
    amount INTEGER NOT NULL, unit TEXT NOT NULL, PRIMARY KEY(product_id, ord)) WITHOUT ROWID;
CREATE TABLE declared(vat INTEGER PRIMARY KEY, ord INTEGER NOT NULL, net INTEGER NOT NULL) WITHOUT ROWID;
CREATE TABLE document(invoice_id TEXT PRIMARY KEY, name TEXT NOT NULL, data BLOB NOT NULL) WITHOUT ROWID;
CREATE TABLE fall(
    id TEXT PRIMARY KEY, label TEXT NOT NULL,
    period_from TEXT NOT NULL, period_to TEXT NOT NULL,
    name TEXT NOT NULL, tax_number TEXT NOT NULL, pab_number TEXT NOT NULL, kennzahl TEXT NOT NULL,
    created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
    -- Stand der Regeln, gegen den die offenen Positionen zuletzt geprüft wurden: der Zähler
    -- gilt nur in der Regel-Datenbank, die mapped_store nennt.
    mapped_store TEXT, mapped_at INTEGER NOT NULL DEFAULT 0,
    template_id TEXT,
    -- Die Programmversion, die den Fall zuletzt geschrieben hat.
    app_version TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE inventory(ord INTEGER PRIMARY KEY, ingredient_id TEXT NOT NULL, opening INTEGER NOT NULL, closing INTEGER NOT NULL, unit TEXT NOT NULL);
CREATE TABLE invoice(
    id TEXT PRIMARY KEY, ord INTEGER NOT NULL,
    source TEXT NOT NULL, file_name TEXT NOT NULL, supplier_name TEXT NOT NULL, number TEXT NOT NULL,
    date TEXT, currency TEXT NOT NULL, net_total INTEGER NOT NULL, gross_total INTEGER NOT NULL,
    stated_net INTEGER, stated_gross INTEGER, verified_at TEXT, verified_auto INTEGER) WITHOUT ROWID;
CREATE TABLE invoice_line(
    invoice_id TEXT NOT NULL, ord INTEGER NOT NULL, no INTEGER NOT NULL, name TEXT NOT NULL,
    seller_article_id TEXT, gtin TEXT, quantity INTEGER NOT NULL, unit_code TEXT NOT NULL,
    unit_price INTEGER NOT NULL, price_base_qty INTEGER NOT NULL, line_net INTEGER NOT NULL,
    vat INTEGER NOT NULL, mapping_id TEXT,
    PRIMARY KEY(invoice_id, ord)) WITHOUT ROWID;
CREATE TABLE no_revenue(ingredient_id TEXT PRIMARY KEY) WITHOUT ROWID;
CREATE TABLE pinned(ord INTEGER PRIMARY KEY, product_id TEXT NOT NULL, portions INTEGER NOT NULL, reason TEXT NOT NULL);
CREATE TABLE reading_cell(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, line INTEGER NOT NULL,
    field TEXT NOT NULL, text TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL,
    h INTEGER NOT NULL, confidence REAL NOT NULL,
    PRIMARY KEY(invoice_id, page, line, field)) WITHOUT ROWID;
CREATE TABLE reading_header(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, field TEXT NOT NULL,
    text TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
    confidence REAL NOT NULL, PRIMARY KEY(invoice_id, page, field)) WITHOUT ROWID;
CREATE TABLE reading_line(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, ord INTEGER NOT NULL,
    no INTEGER NOT NULL, name TEXT NOT NULL, seller_article_id TEXT, gtin TEXT, quantity INTEGER NOT NULL,
    unit_code TEXT NOT NULL, unit_price INTEGER NOT NULL, price_base_qty INTEGER NOT NULL,
    line_net INTEGER NOT NULL, vat INTEGER NOT NULL,
    PRIMARY KEY(invoice_id, page, ord)) WITHOUT ROWID;
CREATE TABLE reading_line_flag(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, line INTEGER NOT NULL,
    ord INTEGER NOT NULL, code TEXT NOT NULL, message TEXT NOT NULL, line_no INTEGER NOT NULL, field TEXT,
    PRIMARY KEY(invoice_id, page, line, ord)) WITHOUT ROWID;
CREATE TABLE reading_page(invoice_id TEXT NOT NULL, ord INTEGER NOT NULL,
    width INTEGER NOT NULL, height INTEGER NOT NULL,
    scale REAL NOT NULL DEFAULT 1, skew REAL NOT NULL DEFAULT 0,
    turn INTEGER NOT NULL DEFAULT 0, settle REAL NOT NULL DEFAULT 0,
    PRIMARY KEY(invoice_id, ord)) WITHOUT ROWID;
CREATE TABLE reading_page_flag(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, ord INTEGER NOT NULL,
    code TEXT NOT NULL, message TEXT NOT NULL, line_no INTEGER NOT NULL, field TEXT,
    PRIMARY KEY(invoice_id, page, ord)) WITHOUT ROWID;
CREATE TABLE reading_word(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, ord INTEGER NOT NULL,
    text TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
    confidence REAL NOT NULL, PRIMARY KEY(invoice_id, page, ord)) WITHOUT ROWID;
CREATE TABLE yield_choice(ord INTEGER PRIMARY KEY, ingredient_id TEXT, category_id TEXT, yield_rule_id TEXT);
