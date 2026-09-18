"""Port of Rows.GroupRows from Umsatzschätzung.Core/Extract/Rows.cs.

Training and inference must group identically; a divergence here is invisible
and poisons every geometric feature. Keep this in step with the C#.
"""


def _overlaps(anchor, box):
    ca = anchor[1] + anchor[3] / 2
    cb = box[1] + box[3] / 2
    return (anchor[1] <= cb < anchor[1] + anchor[3]) or (box[1] <= ca < box[1] + box[3])


def group_rows(words):
    """words: [{'t':..., 'box':[x,y,w,h], ...}] -> list of rows, each a word list.

    Rows come back ordered by y, words within a row ordered by x, matching the
    C# exactly: the anchor is the first word assigned to the row and never
    widens, so the first vertically-overlapping row wins.
    """
    rows = []
    for w in sorted((w for w in words if w["t"].strip()), key=lambda w: w["box"][1]):
        hit = next((r for r in rows if _overlaps(r["anchor"], w["box"])), None)
        if hit is None:
            rows.append({"anchor": w["box"], "box": list(w["box"]), "words": [w]})
            continue
        hit["words"].append(w)
        hit["box"] = union(hit["box"], w["box"])
    for r in rows:
        r["words"].sort(key=lambda w: w["box"][0])
    rows.sort(key=lambda r: r["box"][1])
    return rows


def union(a, b):
    if a[2] == 0 and a[3] == 0:
        return list(b)
    x = min(a[0], b[0])
    y = min(a[1], b[1])
    return [x, y, max(a[0] + a[2], b[0] + b[2]) - x, max(a[1] + a[3], b[1] + b[3]) - y]
