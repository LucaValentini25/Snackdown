"""Emits the cells, tiles and spawn points for Arena01.

The tile grammar is read off the two examples Luca drew rather than invented:
solid terrain uses the grass band at sheet columns 6-8, and the two cells at
columns 9 and 10 are the concave corners that turn a wall into a floor. A flying
platform is one row of cap-fill-cap from the metal bar at columns 12-14.
"""
import sys
sys.path.insert(0, "tools/arena")
from design_arena01 import SURFACES, FLOOR_ROW, INNER_X

WALL_X = ((-30, -29), (28, 29))
WALL_TOP_ROW, FLOOR_BOTTOM_ROW = 16, -18

GRASS = {"tl": (6, 0), "t": (7, 0), "tr": (8, 0),
         "l": (6, 1), "f": (7, 1), "r": (8, 1),
         "concave_l": (10, 1), "concave_r": (9, 1)}
BAR = {"l": (12, 4), "m": (13, 4), "r": (14, 4)}
WALL_COLUMN = [(15, 0), (15, 1), (15, 2)]

solid, cells = set(), {}

# The ground runs the full width, under the walls: a wall standing on a hole in the
# floor is a hole the players can be knocked into.
for row in range(FLOOR_BOTTOM_ROW, FLOOR_ROW + 1):
    for x in range(WALL_X[0][0], WALL_X[1][1] + 1):
        solid.add((x, row))
for s in SURFACES:
    if s.kind == "terrain":
        for row in range(FLOOR_ROW + 1, s.row + 1):
            for x in range(s.x0, s.x1 + 1):
                solid.add((x, row))

def grass_tile(x, y):
    up, left, right = (x, y+1) in solid, (x-1, y) in solid, (x+1, y) in solid
    if not up:
        if not left:  return GRASS["tl"]
        if not right: return GRASS["tr"]
        return GRASS["t"]
    if left and (x-1, y+1) not in solid:  return GRASS["concave_l"]
    if right and (x+1, y+1) not in solid: return GRASS["concave_r"]
    if not left:  return GRASS["l"]
    if not right: return GRASS["r"]
    return GRASS["f"]

for (x, y) in solid:
    cells[(x, y)] = grass_tile(x, y)

for s in SURFACES:
    if s.kind != "flying":
        continue
    for x in range(s.x0, s.x1 + 1):
        cells[(x, s.row)] = BAR["l"] if x == s.x0 else (BAR["r"] if x == s.x1 else BAR["m"])

for x0, x1 in WALL_X:
    for row in range(FLOOR_ROW + 1, WALL_TOP_ROW + 1):
        for x in (x0, x1):
            cells[(x, row)] = WALL_COLUMN[(WALL_TOP_ROW - row) % 3]

def surface_world_y(row):   return (row + 1) * 0.5
def cell_world_centre(x):   return x * 0.5 + 0.25

players = [(-13.0, surface_world_y(FLOOR_ROW) + 0.5), (-4.5, surface_world_y(FLOOR_ROW) + 0.5),
           (4.5, surface_world_y(FLOOR_ROW) + 0.5), (13.0, surface_world_y(FLOOR_ROW) + 0.5)]

by = {s.name: s for s in SURFACES}
fruit_on = ["mid_low", "wing_L", "wing_R", "ledge2_L", "ledge2_R", "perch_L", "perch_R", "crown"]
fruits = []
for n in fruit_on:
    s = by[n]
    fruits.append((round((cell_world_centre(s.x0) + cell_world_centre(s.x1)) / 2, 2),
                   round(surface_world_y(s.row) + 0.5, 2), n))

if __name__ == "__main__":
    xs = [c[0] for c in cells]; ys = [c[1] for c in cells]
    glyph = {v: k for k, v in enumerate(sorted(set(cells.values())))}
    marks = "#=-|/[m]TvWXYZ+*"
    legend = sorted(set(cells.values()))
    print("celdas pintadas:", len(cells))
    print("\nLEYENDA")
    for i, t in enumerate(legend):
        print("   %s = sheet %-7s x%d" % (marks[i], str(t), sum(1 for v in cells.values() if v == t)))
    print("\nMAPA   (1 caracter = 1 tile = 0.5u).  P = spawn jugador, o = fruta\n")
    pmark = {(int(px * 2), int(py * 2) - 1): "P" for px, py in players}
    fmark = {(int(fx * 2), int(fy * 2) - 1): "o" for fx, fy, _ in fruits}
    for y in range(max(ys), min(ys) - 1, -1):
        row = ""
        for x in range(min(xs), max(xs) + 1):
            if (x, y) in cells:      row += marks[legend.index(cells[(x, y)])]
            elif (x, y) in pmark:    row += "P"
            elif (x, y) in fmark:    row += "o"
            else:                    row += "."
        print("y%4d |%s|" % (y, row))
    print("\nSPAWNS DE JUGADOR (world)")
    for i, (x, y) in enumerate(players, 1): print("   Spawn_%d  (%6.2f, %6.2f)" % (i, x, y))
    print("\nSPAWNS DE FRUTA (world)")
    for i, (x, y, n) in enumerate(fruits, 1): print("   FruitSpawn_%d  (%6.2f, %6.2f)  sobre %s" % (i, x, y, n))
