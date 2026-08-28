"""Lays out Arena01, and refuses to accept a layout a player cannot actually move through.

Three things are checked, and the second is the one the first version of this arena
got wrong. Reaching a ledge is not the same as fitting: a platform hanging over the
one below it leaves nowhere to jump from, however short the climb looks on paper.
"""

FLOOR_ROW = -11               # top solid row of the ground
INNER = (-28, 27)             # playable cells between the walls
WALLS = ((-30, -29), (28, 29))
CEILING_ROW = 16

CHARACTER_TILES = 2           # 0.9u tall, so two rows is the least it fits in
MIN_PASS = 3                  # rows of headroom to run under something without snagging
CLEAR_TO_JUMP = 6             # rows a launch spot needs free above it for a full jump

# rise in tiles -> the empty gap a running jump crosses, measured from PlayerMotor
REACH = {1: 6, 2: 5, 3: 5, 4: 4}
MAX_RISE = 4

# the metal band, read off the sheet: a bar that stretches, a lone block, a 2x2 cube
BAR = {"l": (12, 4), "m": (13, 4), "r": (14, 4)}
SINGLE = (12, 5)
CUBE = {"tl": (13, 5), "tr": (14, 5), "bl": (13, 6), "br": (14, 6)}
GRASS = {"tl": (6, 0), "t": (7, 0), "tr": (8, 0), "l": (6, 1), "f": (7, 1), "r": (8, 1),
         "concave_l": (10, 1), "concave_r": (9, 1)}
WALL_COLUMN = [(15, 0), (15, 1), (15, 2)]


class Piece:
    """A standable surface. `rows` is how far down it is drawn, not how high it sits."""

    def __init__(self, name, x0, x1, above_floor, kind):
        self.name, self.x0, self.x1, self.kind = name, x0, x1, kind
        self.row = FLOOR_ROW + above_floor
        self.rows = 2 if kind == "cube" else 1

    @property
    def height(self):
        return self.row - FLOOR_ROW

    @property
    def width(self):
        return self.x1 - self.x0 + 1

    def covers(self, x):
        return self.x0 <= x <= self.x1

    def __repr__(self):
        return "%-9s x %4d..%-4d  +%2d  ancho %2d  %s" % (
            self.name, self.x0, self.x1, self.height, self.width, self.kind)


def mirror(x):
    return -1 - x


def both(name, x0, x1, above_floor, kind):
    return [Piece(name + "L", x0, x1, above_floor, kind),
            Piece(name + "R", mirror(x1), mirror(x0), above_floor, kind)]


PIECES = [Piece("floor", INNER[0], INNER[1], 0, "ground")]
PIECES += both("step",  -28, -25,  2, "terrain")
PIECES += both("a",     -22, -18,  5, "bar")
PIECES += both("b",     -27, -26,  8, "cube")
PIECES += both("c",     -24, -21, 11, "bar")
PIECES += both("d",     -27, -27, 14, "single")
PIECES += both("e",     -25, -23, 17, "bar")
PIECES += both("f",     -17, -16,  9, "cube")
PIECES += both("g",     -13, -12, 13, "cube")
PIECES.append(Piece("hub",   -4,  3,  4, "bar"))
PIECES += both("h",      -8,  -8, 16, "single")
PIECES += both("i",      -5,  -4, 19, "cube")
PIECES.append(Piece("crown", -2,  1, 22, "bar"))


def overlaps(a, b):
    return a.x0 <= b.x1 and b.x0 <= a.x1


def clearance_above(pieces, x, row):
    """Rows of empty space above `row` at column x, up to the ceiling."""
    best = CEILING_ROW - row
    for p in pieces:
        if p is None or not p.covers(x):
            continue
        bottom = p.row - p.rows + 1
        if bottom > row:
            best = min(best, bottom - row - 1)
    return best


def route(pieces, src, dst):
    """The easiest jump from src onto dst, or None. Launching must happen beside dst,
    never underneath it, and the spot launched from needs headroom to jump in."""
    rise = dst.row - src.row
    if rise < 1 or rise > MAX_RISE:
        return None

    best = None
    for launch, gap in ((min(src.x1, dst.x0 - 1), None), (max(src.x0, dst.x1 + 1), None)):
        if not (src.x0 <= launch <= src.x1):
            continue
        gap = (dst.x0 - launch - 1) if launch < dst.x0 else (launch - dst.x1 - 1)
        if gap > REACH[rise]:
            continue
        head = clearance_above([p for p in pieces if p is not src], launch, src.row)
        if head < min(CLEAR_TO_JUMP, rise + CHARACTER_TILES):
            continue
        if best is None or gap < best[1]:
            best = (launch, gap, head)
    return best


def check():
    problems = []

    for p in PIECES:
        if p.kind == "ground":
            continue
        if p.x0 < INNER[0] or p.x1 > INNER[1]:
            problems.append("%s se sale de la arena" % p.name)

    for i, a in enumerate(PIECES):
        for b in PIECES[i + 1:]:
            # A terrain step is solid ground that grew upward, not something hanging
            # over the floor, so there is no space under it to be trapped in.
            if {a.kind, b.kind} == {"ground", "terrain"}:
                continue
            # Otherwise the floor counts. A low ceiling over the ground is the same trap
            # as one over a ledge, and it is where players spend most of the round.
            lower, upper = (a, b) if a.row < b.row else (b, a)
            free = (upper.row - upper.rows + 1) - lower.row - 1
            if overlaps(a, b) and free < MIN_PASS:
                problems.append("%s y %s se solapan con solo %d filas libres"
                                % (a.name, b.name, free))

    for p in PIECES:
        if p.kind == "ground":
            continue
        found = [(s, route(PIECES, s, p)) for s in PIECES if s is not p]
        found = [(s, r) for s, r in found if r]
        if not found:
            problems.append("%s no se puede alcanzar desde ningun lado" % p.name)
    return problems


if __name__ == "__main__":
    print("PIEZAS")
    for p in PIECES:
        print("   " + repr(p))
    bad = check()
    print()
    if bad:
        print("PROBLEMAS (%d)" % len(bad))
        for b in bad:
            print("   " + b)
    else:
        print("SIN PROBLEMAS: todo alcanzable, nada solapado, nadie sin altura para saltar")
