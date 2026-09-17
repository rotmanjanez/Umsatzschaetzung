import datetime as dt
import random
import sys
from pathlib import Path

import money
import sizes
import vocab

sys.path[:0] = [str(Path(__file__).resolve().parent.parent)]
import units  # noqa: E402


CONSONANTS = "bcdfghjklmnpqrstvwxyz"
VOWELS = "aeiou"


def opaque_name(rng):
    style = rng.random()
    if style < 0.22:
        return " ".join(str(rng.randint(100, 999999)) for _ in range(rng.randint(1, 3)))
    if style < 0.4:
        return f"{rng.randint(1000, 99999)}-{rng.randint(10, 999)}"
    if style < 0.55:
        letters = "".join(rng.choice(CONSONANTS.upper()) for _ in range(rng.randint(2, 4)))
        return f"{letters}{rng.randint(100, 99999)}{rng.choice(['', '/A', '-02', 'RT', '.5'])}"
    if style < 0.7:
        word = "".join(rng.choice(CONSONANTS) + rng.choice(VOWELS) for _ in range(rng.randint(2, 5)))
        return word.upper() + " " + str(rng.randint(10, 9999))
    if style < 0.84:
        return " ".join("".join(rng.choice(CONSONANTS + VOWELS + "0123456789")
                                for _ in range(rng.randint(3, 9)))
                        for _ in range(rng.randint(1, 3))).upper()
    return rng.choice(["POS", "ART", "SON", "DIV", "NN", "X"]) + " " + \
        "".join(rng.choice("0123456789") for _ in range(rng.randint(4, 10)))


def abbreviate(rng, name):
    parts = name.split()
    out = []
    for p in parts:
        if len(p) > 5 and rng.random() < 0.6:
            p = p[:rng.randint(3, 5)] + "."
        out.append(p)
    text = " ".join(out)
    return text.upper() if rng.random() < 0.6 else text


def article_name(rng, cat, opaque_share=0.0, category=None):
    if rng.random() < opaque_share:
        return opaque_name(rng)
    base = rng.choice(cat["bases"])
    parts = [base]
    if rng.random() < 0.75:
        v = rng.choice(cat["variants"])
        if v:
            parts.append(v)
    if rng.random() < 0.7:
        parts.append(sizes.phrase(rng, cat, category))
    name = " ".join(parts)
    return abbreviate(rng, name) if rng.random() < 0.18 else name


def article_id(rng):
    style = rng.random()
    if style < 0.3:
        return str(rng.randint(100, 9999))
    if style < 0.55:
        return f"{rng.randint(1, 99):02d}-{rng.randint(1000, 9999)}"
    if style < 0.75:
        return f"A{rng.randint(10000, 999999)}"
    if style < 0.9:
        return f"{rng.randint(100000, 999999)}"
    return f"{rng.choice('KLMSTZ')}{rng.randint(100, 999)}.{rng.randint(10, 99)}"


def gtin(rng):
    return f"{rng.randint(4000000000000, 9999999999999)}"


def invoice_number(rng, date, seq):
    y = date.year
    style = rng.randint(0, 6)
    return [
        f"RE-{y}{seq:05d}",
        f"RG {seq:06d}",
        f"{y}/{seq:04d}",
        f"AR{y % 100}{seq:05d}",
        f"{seq:08d}",
        f"F-{y}-{seq:04d}",
        f"RE{y % 100}{seq:04d}/{rng.randint(1, 9)}",
    ][style]


def company_name(rng, category):
    """Firmenname im Stil eines Lieferanten. Auch der Kunde zieht daraus: eine
    Rechnung geht selten an "Gasthaus Zur Alten Post", sondern meist an eine GmbH,
    die genauso heißt wie der Absender — und genau diese Verwechslung kostete den
    Kopf bisher den `supplier`."""
    return " ".join(x for x in [
        rng.choice(vocab.SUPPLIER_HEADS) if rng.random() < 0.45 else "",
        rng.choice(vocab.SUPPLIER_NAMES),
        vocab.TRADES[category] if rng.random() < 0.4 else "",
        rng.choice(vocab.SUPPLIER_TAILS),
    ] if x)


def person(rng):
    return f"{rng.choice(vocab.FIRST_NAMES)} {rng.choice(vocab.SUPPLIER_NAMES)}"


def supplier(rng, category):
    name = company_name(rng, category)
    zipc, city = rng.choice(vocab.CITIES)
    at = len(zipc) == 4
    return {
        "name": name,
        "street": f"{rng.choice(vocab.STREETS)} {rng.randint(1, 180)}{rng.choice(['', '', '', 'a', 'b', '/2'])}",
        "zip": zipc,
        "city": city,
        "country": "AT" if at else "DE",
        "vatId": (f"ATU{rng.randint(10000000, 99999999)}" if at
                  else f"DE{rng.randint(100000000, 999999999)}"),
        "phone": f"+{'43' if at else '49'} {rng.randint(100, 999)} {rng.randint(100000, 9999999)}",
        "fax": f"+{'43' if at else '49'} {rng.randint(100, 999)} {rng.randint(100000, 9999999)}-{rng.randint(10, 99)}",
        "mail": "office@" + "".join(c for c in name.split()[0].lower() if c.isalpha()) + (".at" if at else ".de"),
        "web": "www." + "".join(c for c in name.split()[0].lower() if c.isalpha()) + (".at" if at else ".de"),
        "taxNumber": (f"{rng.randint(10, 99)}-{rng.randint(100, 999)}/{rng.randint(1000, 9999)}" if at
                      else f"{rng.randint(10, 99)}/{rng.randint(100, 999)}/{rng.randint(10000, 99999)}"),
        "bank": f"{rng.choice(vocab.BANKS)} {city}",
        "iban": ("AT" if at else "DE") + f"{rng.randint(10, 99)} {rng.randint(1000, 9999)} "
                f"{rng.randint(1000, 9999)} {rng.randint(1000, 9999)} {rng.randint(1000, 9999)}",
        "bic": "".join(rng.choice("ABCDEFGHIKLMNOPRSTUVWZ") for _ in range(4)) + ("ATWW" if at else "DEFF"),
        "register": f"FN {rng.randint(10000, 999999)}{rng.choice('abdfghikmnpstvwxyz')}" if at
                    else f"HRB {rng.randint(1000, 99999)}",
    }


def price_base(rng, unit_code):
    if unit_code in ("KGM", "LTR") and rng.random() < 0.25:
        return 100 * money.QTY, "100 " + ("g" if unit_code == "KGM" else "ml")
    if rng.random() < 0.08:
        return 10 * money.QTY, "10 Stk"
    return money.QTY, ""


GROUPS = {
    "baeckerei": ["Kleingebäck", "Brote", "Feine Backwaren", "Tiefkühl"],
    "metzgerei": ["Rind", "Schwein", "Geflügel", "Wurstwaren"],
    "molkerei": ["Frischmilch", "Käse", "Joghurt & Desserts", "Butter & Fette"],
    "gemuese": ["Gemüse", "Salate", "Obst", "Kräuter"],
    "getraenke": ["Alkoholfrei", "Bier", "Sirup & Postmix", "Leergut"],
    "wein": ["Weißwein", "Rotwein", "Schaumwein"],
    "spirituosen": ["Weiße Spirituosen", "Braune Spirituosen", "Liköre"],
    "kaffee": ["Kaffee", "Tee", "Schokolade"],
    "nonfood": ["Hygiene", "Reinigung", "Verpackung"],
    "cc": ["Tiefkühl", "Konserven", "Öle & Fette", "Trockensortiment"],
}


def detail(rng):
    if rng.random() > 0.35:
        return ""
    return rng.choice([
        f"Charge {rng.randint(100000, 999999)}",
        f"MHD {rng.randint(1, 12):02d}/{rng.randint(25, 27)}",
        f"Herkunft {rng.choice(['AT', 'DE', 'IT', 'ES', 'NL', 'FR'])}",
        f"Lieferschein {rng.randint(10000, 99999)}",
        f"Charge {rng.randint(1000, 9999)} / MHD {rng.randint(1, 12):02d}.{rng.randint(2025, 2027)}",
    ])


def sample_lines(rng, category, count, vat_rates, with_discounts, opaque_share):
    cat = vocab.CATEGORIES[category]
    # Steuerschlüssel und Warengruppe sind die beiden unbeschrifteten Codes, die
    # neben Preis und Artikelnummer stehen und wie eine Zahl von uns aussehen.
    scheme = rng.choice([["A", "B", "C"], ["1", "2", "3"], ["N", "E", "H"], ["V", "R", "S"]])
    tax_codes = {r: scheme[i % len(scheme)] for i, r in enumerate(sorted(set(vat_rates)))}
    wg_codes = {g: f"{rng.randint(1, 99):02d}" for g in GROUPS.get(category, ["Sortiment"])}
    lines = []
    for no in range(1, count + 1):
        unit_text, pack = rng.choice(cat["units"])
        unit_code = units.code(unit_text)
        quantity = rng.choice([1, 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 24, 30, 48, 60, 100, 120])
        if unit_code in ("KGM", "LTR") and rng.random() < 0.5:
            quantity = quantity * money.QTY + rng.choice([0, 250, 500, 750, 100, 400]) * (money.QTY // 1000)
        else:
            quantity *= money.QTY
        lo, hi = cat["price"]
        unit_price = rng.randint(lo, hi) * (money.PRICE // money.CENT)
        base_qty, base_text = price_base(rng, unit_code)
        if base_qty > money.QTY:
            unit_price = max(money.PRICE // 100, unit_price * base_qty // (money.QTY * 4))
        vat = rng.choice(vat_rates)
        net = money.line_net(quantity, unit_price, base_qty)
        lines.append({
            "no": no,
            "name": article_name(rng, cat, opaque_share, category),
            "sellerArticleId": article_id(rng) if rng.random() < 0.85 else None,
            "gtin": gtin(rng) if rng.random() < 0.2 else None,
            "quantity": quantity,
            "unitText": unit_text,
            "unitCode": unit_code,
            "unitPrice": unit_price,
            "priceBaseQty": base_qty,
            "priceBaseText": base_text,
            "lineNet": net,
            "vat": vat,
            "discount": rng.choice([300, 500, 1000, 250, 150]) if with_discounts and rng.random() < 0.45 else 0,
            "pack": pack,
            "group": rng.choice(GROUPS.get(category, ["Sortiment"])),
            "detail": detail(rng),
        })
        lines[-1]["taxCode"] = tax_codes[vat]
        lines[-1]["wg"] = wg_codes[lines[-1]["group"]]
    return lines


def apply_discounts(lines):
    for l in lines:
        if l["discount"]:
            gross = money.line_net(l["quantity"], l["unitPrice"], l["priceBaseQty"])
            l["lineNet"] = gross - money.round_div(gross * l["discount"], money.BP)


def totals(lines):
    by_rate = {}
    for l in lines:
        by_rate[l["vat"]] = by_rate.get(l["vat"], 0) + l["lineNet"]
    breakdown = []
    net = gross = 0
    for rate in sorted(by_rate):
        base = by_rate[rate]
        tax = money.round_div(base * rate, money.BP)
        breakdown.append({"vat": rate, "net": base, "tax": tax})
        net += base
        gross += base + tax
    return net, gross, breakdown


FLAWS = ["rounding", "missing_line", "extra_charge", "cent_drift"]


def flaw(rng, lines, net, gross, breakdown):
    kind = rng.choice(FLAWS)
    if kind == "rounding":
        gross += rng.choice([-2, -1, 1, 2])
    elif kind == "cent_drift":
        net += rng.choice([-1, 1])
    elif kind == "extra_charge":
        amount = rng.randint(250, 1500)
        net += amount
        gross += amount + money.round_div(amount * breakdown[-1]["vat"], money.BP)
        breakdown[-1]["net"] += amount
        breakdown[-1]["tax"] = money.round_div(breakdown[-1]["net"] * breakdown[-1]["vat"], money.BP)
        return net, gross, breakdown, {"kind": kind, "amount": amount}
    else:
        victim = rng.choice(lines)
        net -= victim["lineNet"]
        gross -= victim["lineNet"] + money.round_div(victim["lineNet"] * victim["vat"], money.BP)
    return net, gross, breakdown, {"kind": kind}


def make(seed, index):
    rng = random.Random(seed)
    category = rng.choice(sorted(vocab.CATEGORIES))
    sup = supplier(rng, category)
    date = dt.date(2025, 1, 1) + dt.timedelta(days=rng.randint(0, 364))
    count = rng.choice([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 22, 26, 31, 38, 45, 60])
    table = vocab.RATES[sup["country"]]
    kinds = vocab.CATEGORIES[category]["rates"]
    if sup["country"] == "AT" and category == "wein" and rng.random() < 0.25:
        kinds = ["special"]
    vat_rates = sorted({table[k] for k in (kinds if rng.random() < 0.45 else kinds[:1])})
    with_discounts = rng.random() < 0.25
    roll = rng.random()
    opaque_share = 0.9 if roll < 0.08 else (rng.uniform(0.15, 0.5) if roll < 0.28 else 0.05)
    lines = sample_lines(rng, category, count, vat_rates, with_discounts, opaque_share)
    apply_discounts(lines)
    net, gross, breakdown = totals(lines)
    defect = None
    if rng.random() < 0.15:
        net, gross, breakdown, defect = flaw(rng, lines, net, gross, breakdown)
    kind = "delivery_note" if rng.random() < 0.12 else "invoice"
    number = invoice_number(rng, date, index % 100000 + rng.randint(1, 900))
    if kind == "delivery_note":
        for l in lines:
            l["unitPrice"] = l["lineNet"] = l["discount"] = 0
    invoice = {
        "source": "scan",
        "supplierName": sup["name"],
        "supplierVatId": sup["vatId"],
        "number": number,
        "date": date.isoformat(),
        "currency": "EUR",
        "netTotal": 0 if kind == "delivery_note" else net,
        "grossTotal": 0 if kind == "delivery_note" else gross,
        "vatBreakdown": [] if kind == "delivery_note" else breakdown,
        "lines": [{k: l[k] for k in ("no", "name", "sellerArticleId", "gtin", "quantity", "unitText",
                                     "unitCode", "unitPrice", "priceBaseQty", "lineNet", "vat")}
                  for l in lines],
    }
    skonto_pct = rng.choice([2, 2, 3])
    meta = {
        "category": category,
        "kind": kind,
        "supplier": sup,
        "customer": {
            "name": company_name(rng, rng.choice(sorted(vocab.CATEGORIES)))
                    if rng.random() < 0.4 else rng.choice(vocab.CUSTOMERS),
            "street": f"{rng.choice(vocab.STREETS)} {rng.randint(1, 90)}",
            "zip": rng.choice(vocab.CITIES)[0],
            "city": rng.choice(vocab.CITIES)[1],
            "number": f"{rng.randint(10000, 99999)}",
        },
        "payment": rng.choice(vocab.PAYMENT),
        "due": (date + dt.timedelta(days=rng.choice([8, 14, 21, 30]))).isoformat(),
        "delivery": (date - dt.timedelta(days=rng.randint(0, 3))).isoformat(),
        "order": f"B{rng.randint(10000, 999999)}" if rng.random() < 0.5 else None,
        "owner_line": f"Inh. {person(rng)}",
        "tagline": rng.choice([vocab.TRADES[category], vocab.TRADES[category],
                               f"{vocab.TRADES[category]} seit {rng.randint(1890, 1995)}",
                               "Großhandel", "Zustellservice"]),
        # Ablenker im Kopf: eine Lieferschein-Nummer ist eine Nummer und kein
        # Datum, ein Sachbearbeiter ist ein Name und kein Lieferant.
        "extras": {
            "delivery_note": f"LS-{rng.randint(10000, 999999)}",
            "order2": f"A{date.year % 100}-{rng.randint(1000, 99999)}",
            "taxno": sup["taxNumber"] if rng.random() < 0.5 else sup["vatId"],
            "clerk": person(rng),
            "ref": rng.choice([f"P-{date.year}-{rng.randint(10, 99)}",
                               f"{rng.choice('ABCDEFGHKLMRSTW')}{rng.choice('ABCDEFGHKLMRSTW')}/"
                               f"{rng.randint(100, 999)}",
                               person(rng)]),
        },
        "skonto_pct": skonto_pct,
        "skonto": money.round_div(gross * skonto_pct * 100, money.BP),
        "skonto_date": (date + dt.timedelta(days=rng.choice([7, 8, 10, 14]))).isoformat(),
        "paid": (gross // 2 // 100) * 100,
        "defect": defect,
        "opaque_share": round(opaque_share, 2),
        "needs_discount_column": any(l["discount"] for l in lines),
        "render_lines": lines,
    }
    return invoice, meta
