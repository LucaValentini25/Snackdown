"""Arena02: the small one. Pink terrain, orange walls, gold props.

Half the world size of Arena01 and drawn at twice the zoom, so the sprites are
the same size on screen while the fight happens in a quarter of the space. The
zoom lives on the arena's own PixelPerfectCamera, as a 480x270 reference instead
of 960x540: still an exact x4 at 1080p, so nothing stops being pixel perfect.
"""
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from layout import Arena, Theme, build_cells, check, margins, render, write

PINK_ORANGE_GOLD = Theme(ground_col=6, ground_row=8, prop_col=17, prop_row=8,
                         wall_col=15, wall_row=8)

# 30 cells across and 17 up: fifteen world units by eight and a half, which is
# exactly what a 480x270 reference shows at 1080p.
arena = Arena(floor_row=-7, floor_bottom_row=-9, inner=(-13, 12),
              walls=((-15, -14), (13, 14)), ceiling_row=7, theme=PINK_ORANGE_GOLD)

arena.add_pair("step", -13, -11, 2, "terrain")
arena.add_pair("a", -9, -6, 5, "bar")
arena.add_pair("b", -12, -11, 8, "cube")
arena.add_pair("c", -10, -10, 11, "single")
arena.add("mid", -3, 2, 4, "bar")
arena.add("top", -2, 1, 8, "bar")
arena.add_pair("peak", -5, -4, 12, "cube")

by = dict((p.name, p) for p in arena.pieces)
players = [(c * 0.5 + 0.25, arena.surface_y(arena.floor_row) + 0.5) for c in (-9, -5, 4, 8)]
fruits = [(arena.centre_x(by[n].x0, by[n].x1), arena.surface_y(by[n].row) + 0.5, n)
          for n in ("mid", "top", "peakL", "peakR", "aL", "aR")]

if __name__ == "__main__":
    cells = build_cells(arena)
    bad = check(arena)
    print("validacion:", "OK" if not bad else bad)
    tight = [(n, m) for n, m in margins(arena) if m and m[0] < 2]
    print("rutas con margen < 2:", tight if tight else "ninguna")
    print(render(arena, cells, players, fruits))
    if len(sys.argv) > 1:
        write(sys.argv[1], "arena02", cells, players, fruits, arena.pieces + [arena.floor_piece()])
        print("\nescritos en", sys.argv[1])
