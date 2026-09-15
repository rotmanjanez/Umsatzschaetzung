QTY = 1000
PRICE = 1_000_000
CENT = 100
BP = 10_000


def round_div(num, den):
    return -((-num + den // 2) // den) if num < 0 else (num + den // 2) // den


def line_net(quantity, unit_price, base_qty):
    return round_div(quantity * unit_price, (base_qty or QTY) * 10_000)


def de(value, scale, decimals=2):
    sign = "-" if value < 0 else ""
    value = abs(value)
    whole, frac = divmod(round_div(value * 10**decimals, scale), 10**decimals)
    groups = f"{whole:,}".replace(",", ".")
    return f"{sign}{groups},{frac:0{decimals}d}" if decimals else f"{sign}{groups}"


def cents(value):
    return de(value, CENT)


def price(value, decimals=2):
    return de(value, PRICE, decimals)


def qty(value):
    text = de(value, QTY, 3).rstrip("0").rstrip(",")
    return text if "," in text or value % QTY else de(value, QTY, 0)


def pct(value):
    text = de(value, BP // 100, 2)
    return text[:-3] if text.endswith(",00") else text
