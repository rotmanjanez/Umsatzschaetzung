"""Ask gpt-6-luna for the weight of one piece of every ingredient.

    set -a; . ./.env; python3 tools/pieces/generate.py --cache DIR [--apply]

Writes tools/pieces/pieces.tsv; overrides.tsv wins over the model. --apply inserts
"piece" into seed.json line by line, leaving every other byte as it is.
"""
import argparse
import hashlib
import json
import os
import re
import urllib.request
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SEED = ROOT / "src/Umsatzschaetzung.Core/Rulestore/seed.json"
HERE = Path(__file__).resolve().parent
MODEL = "gpt-6-luna"
RUNS = 2
BATCH = 40
SPREAD = 0.25
DIMENSION = {"GRM": "g", "MLT": "ml", "H87": "piece"}

PROMPT = """Du bist Einkaufsexperte für deutsche Gastronomie und Einzelhandel (Metro, Transgourmet, Chefs Culinar, Supermarkt).
Für eine Umsatzschätzung aus Einkaufsrechnungen brauchen wir je Zutat das Gewicht (oder Volumen) EINES typischen Stücks, so wie es im Großhandel/Handel pro Stück (Stk) berechnet oder in Rezepten gezählt wird.

Je Zutat bekommst du: id, Name, Aliase, Kategorie, Rezeptdimension (g, ml oder piece) und Beispielrezepte.
- Wird die Zutat plausibel pro Stück gekauft oder gezählt (Gurke, Salatkopf, Zitrone, Avocado, Paprika, Ei, Brötchen, Baguette, Burger-Bun, Mozzarella-Kugel, Ananas, Melone, Knoblauchknolle, Tortilla, Hähnchenbrust, Schnitzel, Würstchen, Pizzateig-Kugel ...), gib amount = ganzzahlige Grundeinheiten eines Stücks.
- Unzählbare Ware (Mehl, Öl, Zucker, lose Gewürze, Soßen, Käse am Stück vom Laib, Getränke nach Volumen, Flaschen/Dosen deren Größe ohnehin im Artikelnamen steht) und alles Nicht-Lebensmittel ohne sinnvolles Stückgewicht: amount = null.
- Generische Sammelzutaten ("Blattsalat" = ein Kopf, "Zitrusfrüchte" = eine Zitrone/Orange): das typische Stück der häufigsten Sorte.
- unit: bei Rezeptdimension g -> "g", bei ml -> "ml". Bei piece: "g", für Flüssigkeiten "ml".
- confidence 0..1, basis: kurze deutsche Begründung, z. B. "Salatgurke ca. 350–450 g".
Antworte für jede übergebene id genau einmal."""

SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": ["items"],
    "properties": {"items": {"type": "array", "items": {
        "type": "object",
        "additionalProperties": False,
        "required": ["id", "amount", "unit", "confidence", "basis"],
        "properties": {
            "id": {"type": "string"},
            "amount": {"type": ["integer", "null"]},
            "unit": {"type": "string", "enum": ["g", "ml"]},
            "confidence": {"type": "number"},
            "basis": {"type": "string"},
        },
    }}},
}


def candidates(seed):
    uses = defaultdict(list)
    for p in seed["products"].values():
        for r in p["recipe"]:
            uses[r["ingredientId"]].append((p["name"], r["amount"], r["unit"]))
    used, free = [], []
    for iid, ing in seed["ingredients"].items():
        units = {DIMENSION[u] for _, _, u in uses[iid]}
        (used if units else free).append({
            "id": iid,
            "name": ing["name"],
            "aliases": ing["aliases"][:12],
            "category": seed["categories"][ing["categoryId"]]["name"],
            "dimension": units.pop() if len(units) == 1 else "mixed" if units else "keine",
            "recipes": [f"{n}: {a} {DIMENSION[u]}" for n, a, u in uses[iid][:3]],
        })
    return used, free


def ask(batch, run, cache):
    body = {
        "model": MODEL,
        "reasoning": {"effort": "medium"},
        "input": [
            {"role": "system", "content": PROMPT},
            {"role": "user", "content": json.dumps(batch, ensure_ascii=False)},
        ],
        "text": {"format": {"type": "json_schema", "name": "pieces", "strict": True, "schema": SCHEMA}},
    }
    key = hashlib.sha256(json.dumps([body, run], sort_keys=True).encode()).hexdigest()[:16]
    path = cache / f"{key}.json"
    if not path.exists():
        req = urllib.request.Request(
            "https://api.openai.com/v1/responses",
            data=json.dumps(body).encode(),
            headers={"Authorization": f"Bearer {os.environ['OPENAI_API_KEY']}", "Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=600) as r:
            path.write_bytes(r.read())
    raw = json.loads(path.read_text())
    text = next(c["text"] for o in raw["output"] if o["type"] == "message" for c in o["content"] if c["type"] == "output_text")
    return {i["id"]: i for i in json.loads(text)["items"]}


def overrides():
    path = HERE / "overrides.tsv"
    if not path.exists():
        return {}
    rows = [l.split("\t") for l in path.read_text(encoding="utf-8").splitlines()[1:] if l.strip()]
    return {r[0]: {"amount": int(r[1]) if r[1] else None, "unit": r[2], "confidence": 1.0, "basis": r[3]} for r in rows}


def problems(c, answer, runs):
    a = answer["amount"]
    found = []
    if a is not None and not 1 <= a <= 5000:
        found.append("range")
    if a is not None and c["dimension"] in ("g", "ml") and answer["unit"] != c["dimension"]:
        found.append("unit")
    amounts = [r[c["id"]]["amount"] for r in runs if c["id"] in r]
    if len(amounts) < len(runs):
        found.append("missing")
    elif any(x is None for x in amounts) != all(x is None for x in amounts):
        found.append("null-split")
    elif amounts[0] is not None and max(amounts) > (1 + SPREAD) * min(amounts):
        found.append("spread " + "/".join(map(str, amounts)))
    return found


def apply(pieces):
    lines = SEED.read_text(encoding="utf-8").split("\n")
    pattern = re.compile(r'^"(ing\.[^"]+)": \{.*\]\}(,?)$')
    for i, line in enumerate(lines):
        m = pattern.match(line)
        if m and m[1] in pieces and '"piece":' not in line:
            p = pieces[m[1]]
            cut = len(line) - 1 - len(m[2])
            lines[i] = line[:cut] + f',"piece":{{"amount":{p["amount"]},"unit":"{p["unit"]}"}}' + line[cut:]
    SEED.write_text("\n".join(lines), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", type=Path, required=True)
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)

    seed = json.loads(SEED.read_text(encoding="utf-8"))
    used, free = candidates(seed)
    cands = used + free
    batches = [g[i:i + BATCH] for g in (used, free) for i in range(0, len(g), BATCH)]
    runs = [{k: v for b in batches for k, v in ask(b, run, args.cache).items()} for run in range(RUNS)]
    fixed = overrides()

    rows, pieces = [], {}
    for c in cands:
        answer = fixed.get(c["id"]) or runs[0].get(c["id"])
        if answer is None:
            continue
        flags = [] if c["id"] in fixed else problems(c, answer, runs)
        if c["id"] in fixed:
            flags.append("override")
        if answer["amount"] is not None and not {"range", "unit"} & set(flags):
            pieces[c["id"]] = answer
        rows.append([c["id"], c["name"], c["dimension"], "" if answer["amount"] is None else str(answer["amount"]),
                     answer["unit"] if answer["amount"] is not None else "", f'{answer["confidence"]:.2f}',
                     answer["basis"], ", ".join(flags)])

    head = ["id", "name", "dimension", "amount", "unit", "confidence", "basis", "flags"]
    (HERE / "pieces.tsv").write_text("\n".join("\t".join(r) for r in [head] + rows) + "\n", encoding="utf-8")
    print(f"{len(cands)} candidates, {len(pieces)} with a piece, {len(rows) - len(pieces)} null")
    print(Counter(f.split()[0] for r in rows for f in r[7].split(", ") if f))
    if args.apply:
        apply(pieces)


if __name__ == "__main__":
    main()
