import argparse
import json
import os
import sys
from collections import Counter

import money

FIELDS = {"quantity", "unit", "name", "articleId", "unitPrice", "lineNet", "vat",
          "invoiceNumber", "invoiceDate", "supplier", "netTotal", "grossTotal"}


def join(words, field, line):
    picked = [w["t"] for w in words if w["f"] == field and w["l"] == line]
    return " ".join(picked)


def check(directory):
    problems = []
    expected = json.load(open(os.path.join(directory, "expected.json"), encoding="utf-8"))
    counts = Counter()
    for entry in sorted(os.listdir(directory)):
        path = os.path.join(directory, entry)
        if not os.path.isdir(path):
            continue
        record = os.path.join(path, "truth.json")
        if not os.path.exists(record):
            problems.append(f"{entry}: no truth.json (incomplete variation)")
            continue
        truth = json.load(open(record, encoding="utf-8"))
        words = [w for page in truth["pages"] for w in page["words"]]
        first = truth["pages"][0]["words"]
        for w in words:
            counts[w["f"]] += 1
            if w["f"] != "O" and w["f"] not in FIELDS:
                problems.append(f"{entry}: unknown field {w['f']}")
        for page in truth["pages"]:
            if not os.path.exists(os.path.join(path, page["image"])):
                problems.append(f"{entry}: missing image {page['image']}")
            for w in page["words"]:
                x, y, bw, bh = w["box"]
                if bw <= 0 or bh <= 0:
                    problems.append(f"{entry}/{page['image']}: empty box for {w['t']!r}")
                if x < -20 or y < -20 or x + bw > page["width"] + 20 or y + bh > page["height"] + 20:
                    problems.append(f"{entry}/{page['image']}: box outside page for {w['t']!r}")
        delivery = not expected["vatBreakdown"] and expected["netTotal"] == 0
        for line in expected["lines"]:
            name = join(words, "name", line["no"])
            if name.replace(" ", "") != line["name"].replace(" ", ""):
                problems.append(f"{entry}: line {line['no']} name {name!r} != {line['name']!r}")
            if delivery:
                continue
            net = join(words, "lineNet", line["no"]).replace(" ", "")
            if net != money.cents(line["lineNet"]):
                problems.append(f"{entry}: line {line['no']} lineNet {net!r} != "
                                f"{money.cents(line['lineNet'])!r}")
        if not delivery:
            last = truth["pages"][-1]["words"]
            for field, value in (("netTotal", expected["netTotal"]),
                                 ("grossTotal", expected["grossTotal"])):
                shown = join(last, field, 0).replace("€", "").replace(" ", "")
                if shown != money.cents(value):
                    problems.append(f"{entry}: {field} {shown!r} != {money.cents(value)!r}")
            shown = join(first, "invoiceNumber", 0)
            if shown != expected["number"]:
                problems.append(f"{entry}: number {shown!r} != {expected['number']!r}")
    return problems, counts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    args = ap.parse_args()
    total = Counter()
    failures = 0
    checked = 0
    for name in sorted(os.listdir(args.root)):
        directory = os.path.join(args.root, name)
        if not os.path.isdir(directory) or not os.path.exists(os.path.join(directory, "expected.json")):
            continue
        checked += 1
        problems, counts = check(directory)
        total.update(counts)
        for p in problems:
            print(f"{name}/{p}")
        failures += len(problems)
    print(f"\n{checked} invoices checked, {failures} problems")
    for field, n in total.most_common():
        print(f"  {field:<14} {n:>7}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
