CREATE TABLE category(
    id TEXT PRIMARY KEY, name TEXT NOT NULL, sparte TEXT,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT) WITHOUT ROWID;
CREATE TABLE category_gebinde(category_id TEXT NOT NULL, ord INTEGER NOT NULL, unit_code TEXT NOT NULL,
    PRIMARY KEY(category_id, ord)) WITHOUT ROWID;
CREATE TABLE category_gewerbe(category_id TEXT NOT NULL, ord INTEGER NOT NULL, kennzahl TEXT NOT NULL,
    PRIMARY KEY(category_id, ord)) WITHOUT ROWID;
CREATE TABLE gewerbe(
    id TEXT PRIMARY KEY, kennzahl TEXT NOT NULL, name TEXT NOT NULL,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT) WITHOUT ROWID;
CREATE TABLE ingredient(
    id TEXT PRIMARY KEY, name TEXT NOT NULL, category_id TEXT NOT NULL,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT, piece_amount INTEGER, piece_unit TEXT) WITHOUT ROWID;
CREATE TABLE ingredient_alias(ingredient_id TEXT NOT NULL, ord INTEGER NOT NULL, alias TEXT NOT NULL,
    PRIMARY KEY(ingredient_id, ord)) WITHOUT ROWID;
CREATE TABLE klasse(year INTEGER NOT NULL, ord INTEGER NOT NULL, name TEXT NOT NULL, zusatz TEXT,
    bemerkung TEXT, seite INTEGER NOT NULL, PRIMARY KEY(year, ord)) WITHOUT ROWID;
CREATE TABLE klasse_kennzahl(year INTEGER NOT NULL, klasse INTEGER NOT NULL, ord INTEGER NOT NULL,
    kennzahl TEXT NOT NULL, PRIMARY KEY(year, klasse, ord)) WITHOUT ROWID;
CREATE INDEX klasse_kennzahl_wert ON klasse_kennzahl(kennzahl);
CREATE TABLE mapping(
    id TEXT PRIMARY KEY, supplier_name TEXT, supplier_article_id TEXT, gtin TEXT, name TEXT,
    observed TEXT, unit_code TEXT, ingredient_id TEXT NOT NULL, factor INTEGER,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT) WITHOUT ROWID;
CREATE TABLE meta(store TEXT NOT NULL, version INTEGER NOT NULL, created_at TEXT NOT NULL, app TEXT NOT NULL);
CREATE TABLE pauschbetrag(year INTEGER NOT NULL, ord INTEGER NOT NULL, von TEXT NOT NULL, bis TEXT NOT NULL,
    gewerbezweig TEXT NOT NULL, ermaessigt INTEGER NOT NULL, voll INTEGER NOT NULL, gesamt INTEGER NOT NULL,
    PRIMARY KEY(year, ord)) WITHOUT ROWID;
CREATE TABLE product(
    id TEXT PRIMARY KEY, name TEXT NOT NULL,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT) WITHOUT ROWID;
CREATE TABLE recipe_line(product_id TEXT NOT NULL, ord INTEGER NOT NULL, ingredient_id TEXT NOT NULL,
    sub_product_id TEXT, amount INTEGER NOT NULL, unit TEXT NOT NULL, PRIMARY KEY(product_id, ord)) WITHOUT ROWID;
CREATE TABLE sammlung(year INTEGER PRIMARY KEY, quelle TEXT NOT NULL, imported_at TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE satz(year INTEGER NOT NULL, klasse INTEGER NOT NULL, staffel INTEGER NOT NULL,
    art TEXT NOT NULL, von INTEGER, bis INTEGER, mittel INTEGER NOT NULL,
    PRIMARY KEY(year, klasse, staffel, art)) WITHOUT ROWID;
CREATE TABLE staffel(year INTEGER NOT NULL, klasse INTEGER NOT NULL, ord INTEGER NOT NULL,
    stufe TEXT, von INTEGER, bis INTEGER, PRIMARY KEY(year, klasse, ord)) WITHOUT ROWID;
CREATE TABLE synonym(year INTEGER NOT NULL, ord INTEGER NOT NULL, begriff TEXT NOT NULL, klasse TEXT NOT NULL,
    PRIMARY KEY(year, ord)) WITHOUT ROWID;
CREATE INDEX synonym_begriff ON synonym(begriff);
CREATE TABLE template(
    id TEXT PRIMARY KEY, name TEXT NOT NULL, source TEXT NOT NULL, is_default INTEGER NOT NULL,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT) WITHOUT ROWID;
CREATE TABLE yield_rule(
    id TEXT PRIMARY KEY, name TEXT NOT NULL, category_id TEXT, ingredient_id TEXT,
    deduction INTEGER NOT NULL, is_default INTEGER NOT NULL,
    valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, changed_by TEXT, rev INTEGER NOT NULL,
    deleted_at TEXT) WITHOUT ROWID;
