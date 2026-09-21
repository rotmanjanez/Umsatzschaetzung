"""Shared constants and IO for the model-A pipeline."""
import json
from pathlib import Path

# The value classes come first and keep their ids; the label classes are appended
# so a checkpoint with the old 13-way head still maps onto LABELS[:13].
FIELDS = ["quantity", "unit", "name", "articleId", "unitPrice", "lineNet", "vat",
          "invoiceNumber", "invoiceDate", "supplier", "netTotal", "grossTotal",
          # Header and totals keys as classes of their own: assembly keys the value
          # off its label instead of taking the first tagged run on the page.
          # `otherLabel` is every key that is not ours (Kundennummer, Bestellnummer,
          # Lieferdatum, UID, Tel ...), which is what separates "Rechnungsnummer"
          # from "Kundennummer" for the model.
          "numberLabel", "dateLabel", "netLabel", "grossLabel", "vatLabel", "otherLabel",
          # v11: fine classes for everything that used to be `O` although it looks
          # like one of ours. A customer number that is its own class forces the
          # model to tell it from the invoice number by context; as `O` it could
          # get away with "digits near the top". Assembly ignores all of these;
          # they exist to sharpen the twelve value classes. Appended, so a 19-way
          # head still decodes via LABELS[:19].
          "buyer",             # the customer's name words (counterpart of `supplier`)
          "customerNumber", "orderNumber", "deliveryNoteNumber",  # header numbers
          "taxId",             # USt-IdNr / Steuernummer / UID values
          "bankId",            # IBAN, BIC
          "postcode", "phone", # address and contact digits
          "orderDate", "deliveryDate", "dueDate",  # the dates that are not the invoice date
          "gtin",              # EAN/GTIN in the item table or inline in the name
          "lineDiscount",      # per-line Rabatt (% or amount)
          "lineGross",         # per-line gross amount when net and gross columns coexist
          "priceBasis",        # Preiseinheit / "per 100" cells
          "subtotal",          # Zwischensumme / Warenwert when charge rows follow
          "charge",            # Versand / Verpackung / Pfand amounts in the totals
          "discount",          # Skonto / Rabatt amounts in the totals
          "amountDue"]         # Zahlbetrag / Fälliger Betrag / Restbetrag when it differs from gross
LABEL_OF = {"numberLabel": "invoiceNumber", "dateLabel": "invoiceDate",
            "netLabel": "netTotal", "grossLabel": "grossTotal", "vatLabel": "vat"}
LABELS = ["O"] + FIELDS
LABEL_ID = {n: i for i, n in enumerate(LABELS)}

ROLES = ["header", "column-header", "line-item", "line-wrap", "continuation",
         "total", "footer", "group", "carry"]
ROLE_ID = {n: i for i, n in enumerate(ROLES)}


def variations(root):
    """Yield (truth_path, truth_dict) for every rendered variation under root."""
    for p in sorted(Path(root).glob("*/*/truth.json")):
        yield p, json.loads(p.read_text())


def ocr_pages(variation_dir, page_index):
    """The OCR dump for one page, or None when it was never produced."""
    p = Path(variation_dir) / f"page-{page_index + 1}.ocr.json"
    if not p.exists():
        return None
    return json.loads(p.read_text())["pages"][0]


def write_jsonl(path, rows):
    with open(path, "w") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False, separators=(",", ":")) + "\n")


def read_jsonl(path):
    with open(path) as f:
        for line in f:
            if line.strip():
                yield json.loads(line)

# v13: structure, not types. The word head shrinks to the header/totals metadata plus one
# class `cell` for "this word sits in an item-table cell"; the cell's column comes from the
# column head (index 0 = not in a table, 1..MAX_COLS = column left-to-right) and its start
# from the cell head. Nothing in the table is typed by the model any more.
STRUCT_LABELS = ["O", "cell", "invoiceNumber", "invoiceDate", "supplier", "netTotal", "grossTotal",
                 "vat", "numberLabel", "dateLabel", "netLabel", "grossLabel", "vatLabel", "otherLabel"]
STRUCT_ID = {n: i for i, n in enumerate(STRUCT_LABELS)}
MAX_COLS = 16


def struct_label(field, tbl):
    """The v13 word class of a rows-file word: `cell` inside an item table, else the metadata
    class if it is one of ours, else O (all v11 fine classes fold to O here too)."""
    if tbl is not None and tbl >= 0:
        return STRUCT_ID["cell"]
    return STRUCT_ID.get(field, 0)
