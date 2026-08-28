"""Lays out Arena01 and checks every surface can actually be reached.

The reach table is not guessed: it comes from simulating PlayerMotor tick by tick
at 30Hz with the shipped MovementConfig, and is expressed as the empty gap between
platform edges, with the character's width already taken out.
"""

FLOOR_ROW = -11          # top solid row of the ground
INNER_X = (-28, 27)      # playable cells between the walls
WALL_TOP_ROW = 16

# rise in tiles -> (max gap that is possible, gap that is comfortable to design for)
REACH = {0: (6, 4), 1: (6, 4), 2: (5, 3), 3: (5, 3), 4: (4, 2)}
MAX_RISE = 4


class Surface:
    def __init__(self, name, x0, x1, row, kind):
        self.name, self.x0, self.x1, self.row, self.kind = name, x0, x1, row, kind

    @property
    def tiles_above_floor(self):
        return self.row - FLOOR_ROW

    def __repr__(self):
        return "%-12s x %4d..%-4d  +%2d tiles  (%s)" % (
            self.name, self.x0, self.x1, self.tiles_above_floor, self.kind)


def mirror(x):
    """Reflects a cell across the arena's centre line."""
    return -1 - x


def pair(name, x0, x1, row, kind):
    """A surface and its mirror image, so neither side of the arena is favoured."""
    return [Surface(name + "_L", x0, x1, row, kind),
            Surface(name + "_R", mirror(x1), mirror(x0), row, kind)]


SURFACES = [Surface("floor", INNER_X[0], INNER_X[1], FLOOR_ROW, "ground")]
SURFACES += pair("step",   -24, -19, FLOOR_ROW + 2,  "terrain")   # +2, grows out of the floor
SURFACES += pair("ledge1", -27, -24, FLOOR_ROW + 5,  "flying")    # +5
SURFACES += pair("ledge2", -20, -16, FLOOR_ROW + 8,  "flying")    # +8
SURFACES += pair("ledge3", -26, -22, FLOOR_ROW + 11, "flying")    # +11
SURFACES += pair("wing",   -13,  -8, FLOOR_ROW + 6,  "flying")    # +6, the middle route
SURFACES += pair("perch",  -15, -11, FLOOR_ROW + 10, "flying")    # +10, the way to the top
SURFACES.append(Surface("mid_low",  -6,  5, FLOOR_ROW + 3,  "flying"))
SURFACES.append(Surface("mid_high", -9,  8, FLOOR_ROW + 13, "flying"))
SURFACES.append(Surface("crown",    -3,  2, FLOOR_ROW + 17, "flying"))


def gap(a, b):
    """Empty cells between two surfaces horizontally. 0 when they overlap."""
    if b.x0 > a.x1:
        return b.x0 - a.x1 - 1
    if a.x0 > b.x1:
        return a.x0 - b.x1 - 1
    return 0


def routes_into(target):
    found = []
    for src in SURFACES:
        if src is target:
            continue
        rise = target.row - src.row
        if rise <= 0 or rise > MAX_RISE:
            continue
        g = gap(src, target)
        possible, comfortable = REACH[rise]
        if g <= possible:
            found.append((src, rise, g, "comoda" if g <= comfortable else "justa"))
    return found


print("ALCANCE  (hueco libre entre bordes, en tiles)")
for rise in sorted(REACH):
    print("   subida +%d: maximo %d, comodo %d" % (rise, REACH[rise][0], REACH[rise][1]))

print("\nSUPERFICIES")
for s in SURFACES:
    print("   " + repr(s))

print("\nRUTAS DE SUBIDA")
orphans = []
for s in SURFACES:
    if s.kind == "ground":
        continue
    routes = routes_into(s)
    if not routes:
        orphans.append(s)
        print("   %-12s SIN RUTA" % s.name)
        continue
    best = ", ".join("%s +%d salto de %d (%s)" % (r[0].name, r[1], r[2], r[3]) for r in routes[:3])
    print("   %-12s <- %s" % (s.name, best))

print("\n%s" % ("TODAS ALCANZABLES" if not orphans else "SIN RUTA: " + ", ".join(o.name for o in orphans)))
