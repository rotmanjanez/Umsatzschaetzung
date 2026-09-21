"""Score a v13 predict.py dump on structure only (no assembly, no invoice values).

The dump must carry the truth alongside (`field_t`, `col_t`, `cell_t`, `role_t`, `tbl_t`),
which predict.py writes whenever the rows file has it (synthetic val, the replica probes).

    structscore.py --dump val-v13.jsonl [--probe wine]

Reports: row-role accuracy, table-row recall, cell-start precision/recall/F1 on table words,
column accuracy on table words, per-table column agreement (share of tables whose columns
are ≥ 90 % consistent between header cell and body cells), metadata word F1, and — with
--probe wine — the column-swap pass rate: pages where every vintage token (19xx/20xx first in
its row) shares its column with the row's other name words and not with the year-coded SKU.
"""
import argparse
import collections
import json
import re

META = ("invoiceNumber", "invoiceDate", "supplier", "netTotal", "grossTotal", "vat",
        "numberLabel", "dateLabel", "netLabel", "grossLabel", "vatLabel", "otherLabel")


def f1(hit, pred, true):
    p = hit / max(1, pred); r = hit / max(1, true)
    return (2 * p * r / max(1e-9, p + r)), p, r


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--probe", default="", help="wine: column-swap pass rate")
    a = ap.parse_args()

    rows_ok = rows_n = 0; table_rows_hit = table_rows_n = 0
    cs_hit = cs_pred = cs_true = 0; col_hit = col_n = 0
    tables_ok = tables_n = 0
    meta = {c: [0, 0, 0] for c in META}  # hit, pred, true
    probe_pass = probe_n = 0
    pages = 0
    for line in open(a.dump):
        pg = json.loads(line); pages += 1
        words = pg["words"]
        if not words or "col_t" not in words[0]:
            continue
        # rows: role accuracy (one vote per row, truth role from the rows file)
        by_row = collections.defaultdict(list)
        for w in words:
            by_row[w["row"]].append(w)
        for r, ws in by_row.items():
            truth = ws[0].get("role_t"); pred = ws[0]["role"]
            rows_n += 1; rows_ok += truth == pred
            if truth in ("line-item", "line-wrap"):
                table_rows_n += 1; table_rows_hit += pred in ("line-item", "line-wrap")
        # table words: cell starts and columns
        cols_by_table = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))
        for w in words:
            if w.get("tbl_t", -1) >= 0:
                col_n += 1; col_hit += (w["col"] == w["col_t"] + 1)
                cs_true += w["cell_t"] == 1; cs_pred += w["cell_start"] == 1
                cs_hit += (w["cell_t"] == 1 and w["cell_start"] == 1)
                cols_by_table[w["tbl_t"]][w["col_t"]][w["col"]] += 1
        for tbl, cols in cols_by_table.items():
            tables_n += 1
            agree = all(c.most_common(1)[0][1] >= 0.9 * sum(c.values()) and c.most_common(1)[0][0] == k + 1
                        for k, c in cols.items())
            tables_ok += agree
        # metadata classes
        for w in words:
            t, p = w.get("field_t", "O"), w["pred"]
            if w.get("tbl_t", -1) >= 0:
                t = "cell"
            for c in META:
                if p == c: meta[c][1] += 1
                if t == c: meta[c][2] += 1
                if p == c and t == c: meta[c][0] += 1
        # column-swap probe (wine): vintage first in a line-item row
        if a.probe == "wine":
            for r, ws in by_row.items():
                if ws[0].get("role_t") != "line-item":
                    continue
                ws = sorted(ws, key=lambda q: q["box"][0])
                vint = [w for w in ws if re.fullmatch(r"(19|20)\d\d", w["t"])]
                sku = [w for w in ws if re.fullmatch(r"(19|20)?\d\d-[A-Z]{1,3}-\d+", w["t"])]
                names = [w for w in ws if w.get("field_t") == "name" and not re.fullmatch(r"(19|20)\d\d", w["t"])]
                if not vint or not sku or not names:
                    continue
                probe_n += 1
                name_col = collections.Counter(w["col"] for w in names).most_common(1)[0][0]
                probe_pass += all(v["col"] == name_col for v in vint) and all(s["col"] != name_col for s in sku)

    print(f"pages {pages}")
    print(f"row role accuracy      {rows_ok / max(1, rows_n):.4f}   (rows {rows_n})")
    print(f"table-row recall       {table_rows_hit / max(1, table_rows_n):.4f}   (line-item + line-wrap rows {table_rows_n})")
    cf, cp, cr = f1(cs_hit, cs_pred, cs_true)
    print(f"cell-start F1 / P / R  {cf:.4f} / {cp:.4f} / {cr:.4f}   (table words {col_n})")
    print(f"column accuracy        {col_hit / max(1, col_n):.4f}")
    print(f"tables with consistent columns  {tables_ok / max(1, tables_n):.4f}   ({tables_n} tables)")
    print("metadata word F1:")
    for c in META:
        h, p, t = meta[c]
        if t:
            print(f"  {c:14s} {f1(h, p, t)[0]:.4f}   (n {t})")
    if a.probe == "wine":
        print(f"column-swap pass rate  {probe_pass / max(1, probe_n):.4f}   ({probe_n} vintage rows)")


if __name__ == "__main__":
    main()
