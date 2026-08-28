"""Arena01: the wide one. Green terrain, brick walls, metal props."""
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from layout import Arena, Theme, build_cells, check, margins, render, write, REACH

GREEN_BRICK_METAL = Theme(ground_col=6, ground_row=0, prop_col=12, prop_row=4,
                          wall_col=15, wall_row=0)

arena = Arena(floor_row=-11, floor_bottom_row=-18, inner=(-28, 27),
              walls=((-30, -29), (28, 29)), ceiling_row=16, theme=GREEN_BRICK_METAL)

arena.add_pair("step", -28, -25, 2, "terrain")
arena.add_pair("a", -22, -18, 5, "bar")
arena.add_pair("b", -27, -26, 8, "cube")
arena.add_pair("c", -24, -21, 11, "bar")
arena.add_pair("d", -27, -27, 14, "single")
arena.add_pair("e", -25, -23, 17, "bar")
arena.add_pair("f", -17, -16, 9, "cube")
arena.add_pair("g", -13, -12, 13, "cube")
arena.add("hub", -4, 3, 4, "bar")
arena.add_pair("h", -8, -8, 16, "single")
arena.add_pair("i", -5, -4, 19, "cube")
arena.add("crown", -2, 1, 22, "bar")

by = dict((p.name, p) for p in arena.pieces)
players = [(c * 0.5 + 0.25, arena.surface_y(arena.floor_row) + 0.5) for c in (-15, -10, 9, 14)]
fruits = [(arena.centre_x(by[n].x0, by[n].x1), arena.surface_y(by[n].row) + 0.5, n)
          for n in ("hub", "aL", "aR", "cL", "cR", "fL", "fR", "crown")]

if __name__ == "__main__":
    cells = build_cells(arena)
    bad = check(arena)
    print("validacion:", "OK" if not bad else bad)
    tight = [(n, m) for n, m in margins(arena) if m and m[0] < 2]
    print("rutas con margen < 2:", tight if tight else "ninguna")
    print(render(arena, cells, players, fruits))
    if len(sys.argv) > 1:
        write(sys.argv[1], "arena01", cells, players, fruits, arena.pieces + [arena.floor_piece()])
        print("\nescritos en", sys.argv[1])
