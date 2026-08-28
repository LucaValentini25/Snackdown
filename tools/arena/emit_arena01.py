"""Turns the validated layout into cells, tiles and spawn positions."""
import sys, io, os
sys.path.insert(0, os.path.dirname(__file__))
from arena01 import (PIECES, FLOOR_ROW, INNER, WALLS, CEILING_ROW,
                     BAR, SINGLE, CUBE, GRASS, WALL_COLUMN, check)

solid, cells = set(), {}

for row in range(-18, FLOOR_ROW + 1):
    for x in range(WALLS[0][0], WALLS[1][1] + 1):
        solid.add((x, row))
for p in PIECES:
    if p.kind == "terrain":
        for row in range(FLOOR_ROW + 1, p.row + 1):
            for x in range(p.x0, p.x1 + 1):
                solid.add((x, row))


def grass(x, y):
    up, left, right = (x, y + 1) in solid, (x - 1, y) in solid, (x + 1, y) in solid
    if not up:
        return GRASS["tl"] if not left else (GRASS["tr"] if not right else GRASS["t"])
    if left and (x - 1, y + 1) not in solid:
        return GRASS["concave_l"]
    if right and (x + 1, y + 1) not in solid:
        return GRASS["concave_r"]
    return GRASS["l"] if not left else (GRASS["r"] if not right else GRASS["f"])


for (x, y) in solid:
    cells[(x, y)] = grass(x, y)

for p in PIECES:
    if p.kind == "single":
        cells[(p.x0, p.row)] = SINGLE
    elif p.kind == "cube":
        cells[(p.x0, p.row)] = CUBE["tl"]; cells[(p.x1, p.row)] = CUBE["tr"]
        cells[(p.x0, p.row - 1)] = CUBE["bl"]; cells[(p.x1, p.row - 1)] = CUBE["br"]
    elif p.kind == "bar":
        for x in range(p.x0, p.x1 + 1):
            cells[(x, p.row)] = BAR["l"] if x == p.x0 else (BAR["r"] if x == p.x1 else BAR["m"])

for x0, x1 in WALLS:
    for row in range(FLOOR_ROW + 1, CEILING_ROW + 1):
        for x in (x0, x1):
            cells[(x, row)] = WALL_COLUMN[(CEILING_ROW - row) % 3]


def surface_y(row):
    return (row + 1) * 0.5


def centre_x(x0, x1):
    return (x0 * 0.5 + 0.25 + x1 * 0.5 + 0.25) / 2


by = {p.name: p for p in PIECES}
players = [(c * 0.5 + 0.25, surface_y(FLOOR_ROW) + 0.5) for c in (-15, -10, 9, 14)]
fruits = [(centre_x(by[n].x0, by[n].x1), surface_y(by[n].row) + 0.5, n)
          for n in ("hub", "aL", "aR", "cL", "cR", "fL", "fR", "crown")]

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else None
    bad = check()
    print("validacion:", "OK" if not bad else bad)
    legend = sorted(set(cells.values()))
    marks = "#=-|/[m]TvW1QqEeXYZ"
    xs = [c[0] for c in cells]; ys = [c[1] for c in cells]
    print("celdas:", len(cells))
    for i, t in enumerate(legend):
        print("   %s = sheet %-8s x%d" % (marks[i], str(t), sum(1 for v in cells.values() if v == t)))
    pm = {(int(px * 2), int(py * 2) - 1): "P" for px, py in players}
    fm = {(int(fx * 2), int(fy * 2) - 1): "o" for fx, fy, _ in fruits}
    print()
    for y in range(max(ys), min(ys) - 1, -1):
        row = ""
        for x in range(min(xs), max(xs) + 1):
            if (x, y) in cells: row += marks[legend.index(cells[(x, y)])]
            elif (x, y) in pm:  row += "P"
            elif (x, y) in fm:  row += "o"
            else:               row += "."
        print("y%4d |%s|" % (y, row))
    if out:
        with io.open(os.path.join(out, "arena01_cells.txt"), "w", encoding="utf-8", newline="\n") as f:
            for (x, y), (c, r) in sorted(cells.items()):
                f.write("%d %d %d %d\n" % (x, y, c, r))
        with io.open(os.path.join(out, "arena01_spawns.txt"), "w", encoding="utf-8", newline="\n") as f:
            for i, (x, y) in enumerate(players, 1): f.write("P %d %.4f %.4f\n" % (i, x, y))
            for i, (x, y, n) in enumerate(fruits, 1): f.write("F %d %.4f %.4f\n" % (i, x, y))
        # Written here rather than by hand: a verification file one layout behind
        # reports faults in a scene that is correct. It did, once.
        with io.open(os.path.join(out, "arena01_pieces.txt"), "w", encoding="utf-8", newline="\n") as f:
            for p in PIECES:
                f.write("%s %.4f %.4f %.4f\n" % (p.name, p.x0 * 0.5, (p.x1 + 1) * 0.5, surface_y(p.row)))
        print("escritos cells, spawns y piezas en", out)
