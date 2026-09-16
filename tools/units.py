"""data/units.json, the one copy. Umsatzschätzung.Core/Model/Units.cs reads the same file.

Codes are UN/ECE Rec 20; package types from Rec 21 carry the X prefix there.
"""
import json
from pathlib import Path

_PATH = Path(__file__).resolve().parent.parent / "data" / "units.json"
UNITS = json.loads(_PATH.read_text(encoding="utf-8"))["units"]

TABLE = {u["code"]: u for u in UNITS}
ALIAS = {a.lower(): u["code"] for u in UNITS for a in u["aliases"]}


def resolve(text):
    """Printed unit text -> code, or None. Mirrors Units.Resolve exactly."""
    return ALIAS.get(text.lower())


def code(text):
    """Like resolve, but raises: the corpus must never invent a code."""
    c = resolve(text.strip().strip("."))
    if c is None:
        raise KeyError(f"keine Einheit für {text!r} in {_PATH}")
    return c
