"""The rules an arena layout has to satisfy, and the tiles it turns into.

Shared by every arena because every arena is walked by the same character. The
reach table comes from simulating PlayerMotor tick by tick at 30Hz against the
shipped MovementConfig; the clearance rules come from that character being 0.9
units tall, which is what the first Arena01 layout ignored - it produced ledges
with a platform two rows above them and nothing could get in.
"""

CHARACTER_TILES = 2      # 0.9u tall, so two rows is the least it fits in
MIN_PASS = 3             # rows of headroom to run under something without snagging
CLEAR_TO_JUMP = 6        # rows a launch spot needs free above it for a full jump

# rise in tiles -> the empty gap a running jump crosses
REACH = {1: 6, 2: 5, 3: 5, 4: 4}
MAX_RISE = 4


class Theme:
    """Which cells of the sprite sheet an arena is drawn from.

    The sheet lays every terrain out the same way, so a theme is three coordinates:
    where its ground block starts, where its props start, and where its wall column
    is. The two cells to the right of a ground block are its concave corners, the
    pieces that turn a wall into a floor.
    """

    def __init__(self, ground_col, ground_row, prop_col, prop_row, wall_col, wall_row):
        g, r = ground_col, ground_row
        self.ground = {
            "tl": (g, r), "t": (g + 1, r), "tr": (g + 2, r),
            "l": (g, r + 1), "f": (g + 1, r + 1), "r": (g + 2, r + 1),
            "concave_r": (g + 3, r + 1), "concave_l": (g + 4, r + 1),
        }
        p, q = prop_col, prop_row
        self.bar = {"l": (p, q), "m": (p + 1, q), "r": (p + 2, q)}
        self.single = (p, q + 1)
        self.cube = {"tl": (p + 1, q + 1), "tr": (p + 2, q + 1),
                     "bl": (p + 1, q + 2), "br": (p + 2, q + 2)}
        self.wall = [(wall_col, wall_row), (wall_col, wall_row + 1), (wall_col, wall_row + 2)]


class Piece:
    """A standable surface. `rows` is how thick it is drawn, not how high it sits."""

    def __init__(self, name, x0, x1, row, kind):
        self.name, self.x0, self.x1, self.row, self.kind = name, x0, x1, row, kind
        self.rows = 2 if kind == "cube" else 1

    @property
    def width(self):
        return self.x1 - self.x0 + 1

    def covers(self, x):
        return self.x0 <= x <= self.x1

    def __repr__(self):
        return "%-9s x %4d..%-4d  fila %3d  ancho %2d  %s" % (
            self.name, self.x0, self.x1, self.row, self.width, self.kind)


class Arena:
    def __init__(self, floor_row, floor_bottom_row, inner, walls, ceiling_row, theme):
        self.floor_row, self.floor_bottom_row = floor_row, floor_bottom_row
        self.inner, self.walls, self.ceiling_row = inner, walls, ceiling_row
        self.theme = theme
        self.pieces = []

    def mirror(self, x):
        """Reflects a cell across the arena's centre line."""
        return (self.walls[0][0] + self.walls[1][1]) - x

    def add(self, name, x0, x1, above_floor, kind):
        self.pieces.append(Piece(name, x0, x1, self.floor_row + above_floor, kind))

    def add_pair(self, name, x0, x1, above_floor, kind):
        """A piece and its mirror image, so neither side of the arena is favoured."""
        self.add(name + "L", x0, x1, above_floor, kind)
        self.add(name + "R", self.mirror(x1), self.mirror(x0), above_floor, kind)

    def floor_piece(self):
        return Piece("floor", self.inner[0], self.inner[1], self.floor_row, "ground")

    def surface_y(self, row):
        return (row + 1) * 0.5

    def centre_x(self, x0, x1):
        return (x0 * 0.5 + 0.25 + x1 * 0.5 + 0.25) / 2


def overlaps(a, b):
    return a.x0 <= b.x1 and b.x0 <= a.x1


def clearance_above(arena, pieces, x, row):
    """Rows of empty space above `row` at column x, up to the ceiling."""
    best = arena.ceiling_row - row
    for p in pieces:
        if not p.covers(x):
            continue
        bottom = p.row - p.rows + 1
        if bottom > row:
            best = min(best, bottom - row - 1)
    return best


def route(arena, pieces, src, dst):
    """The easiest jump from src onto dst, or None.

    Launching happens beside the target, never underneath it. A horizontal gap of
    zero can mean two ledges touching, and it can equally mean one hanging over the
    other with nowhere to jump from; only the first of those is a jump. The launch
    spot also has to have the headroom to make the jump at all.
    """
    rise = dst.row - src.row
    if rise < 1 or rise > MAX_RISE:
        return None

    others = [p for p in pieces if p is not src]
    best = None
    for launch in (min(src.x1, dst.x0 - 1), max(src.x0, dst.x1 + 1)):
        if not (src.x0 <= launch <= src.x1):
            continue
        gap = (dst.x0 - launch - 1) if launch < dst.x0 else (launch - dst.x1 - 1)
        if gap > REACH[rise]:
            continue
        if clearance_above(arena, others, launch, src.row) < min(CLEAR_TO_JUMP, rise + CHARACTER_TILES):
            continue
        if best is None or gap < best[1]:
            best = (launch, gap, rise)
    return best


def check(arena):
    """Every reason this layout would be unplayable, or an empty list."""
    problems = []
    everything = arena.pieces + [arena.floor_piece()]

    for p in arena.pieces:
        if p.x0 < arena.inner[0] or p.x1 > arena.inner[1]:
            problems.append("%s se sale de la arena" % p.name)

    for i, a in enumerate(everything):
        for b in everything[i + 1:]:
            # A terrain step is solid ground that grew upward, not something hanging
            # over the floor, so there is no space under it to be trapped in.
            if set([a.kind, b.kind]) == set(["ground", "terrain"]):
                continue
            lower, upper = (a, b) if a.row < b.row else (b, a)
            free = (upper.row - upper.rows + 1) - lower.row - 1
            if overlaps(a, b) and free < MIN_PASS:
                problems.append("%s y %s se solapan con solo %d filas libres" % (a.name, b.name, free))

    for p in everything:
        if p.kind == "ground":
            continue
        if not any(route(arena, everything, s, p) for s in everything if s is not p):
            problems.append("%s no se puede alcanzar desde ningun lado" % p.name)
    return problems


def margins(arena):
    """The slack each piece's easiest route has against the measured reach."""
    everything = arena.pieces + [arena.floor_piece()]
    out = []
    for p in everything:
        if p.kind == "ground":
            continue
        best = None
        for s in everything:
            if s is p:
                continue
            r = route(arena, everything, s, p)
            if r and (best is None or REACH[r[2]] - r[1] > best[0]):
                best = (REACH[r[2]] - r[1], s.name, r[2], r[1])
        out.append((p.name, best))
    return out


def build_cells(arena):
    """Every painted cell, as coordinates into the sprite sheet."""
    t = arena.theme
    solid, cells = set(), {}

    # The ground runs the full width, under the walls: a wall standing on a hole in
    # the floor is a hole the players can be knocked into.
    for row in range(arena.floor_bottom_row, arena.floor_row + 1):
        for x in range(arena.walls[0][0], arena.walls[1][1] + 1):
            solid.add((x, row))
    for p in arena.pieces:
        if p.kind == "terrain":
            for row in range(arena.floor_row + 1, p.row + 1):
                for x in range(p.x0, p.x1 + 1):
                    solid.add((x, row))

    for (x, y) in solid:
        up, left, right = (x, y + 1) in solid, (x - 1, y) in solid, (x + 1, y) in solid
        if not up:
            cells[(x, y)] = t.ground["tl"] if not left else (t.ground["tr"] if not right else t.ground["t"])
        elif left and (x - 1, y + 1) not in solid:
            cells[(x, y)] = t.ground["concave_l"]
        elif right and (x + 1, y + 1) not in solid:
            cells[(x, y)] = t.ground["concave_r"]
        else:
            cells[(x, y)] = t.ground["l"] if not left else (t.ground["r"] if not right else t.ground["f"])

    for p in arena.pieces:
        if p.kind == "single":
            cells[(p.x0, p.row)] = t.single
        elif p.kind == "cube":
            cells[(p.x0, p.row)] = t.cube["tl"]
            cells[(p.x1, p.row)] = t.cube["tr"]
            cells[(p.x0, p.row - 1)] = t.cube["bl"]
            cells[(p.x1, p.row - 1)] = t.cube["br"]
        elif p.kind == "bar":
            for x in range(p.x0, p.x1 + 1):
                cells[(x, p.row)] = t.bar["l"] if x == p.x0 else (t.bar["r"] if x == p.x1 else t.bar["m"])

    for x0, x1 in arena.walls:
        for row in range(arena.floor_row + 1, arena.ceiling_row + 1):
            for x in (x0, x1):
                cells[(x, row)] = t.wall[(arena.ceiling_row - row) % 3]
    return cells


def render(arena, cells, players=(), fruits=()):
    """An ASCII picture of the layout, for reading a change before painting it."""
    legend = sorted(set(cells.values()))
    marks = "#=-|/[m]TvW1QqEeXYZoabcdef"
    xs = [c[0] for c in cells]
    ys = [c[1] for c in cells]
    pm = dict(((int(px * 2), int(py * 2) - 1), "P") for px, py in players)
    fm = dict(((int(fx * 2), int(fy * 2) - 1), "o") for fx, fy, _ in fruits)
    lines = ["celdas: %d" % len(cells)]
    for i, t in enumerate(legend):
        lines.append("   %s = sheet %-8s x%d" % (marks[i], str(t), sum(1 for v in cells.values() if v == t)))
    lines.append("")
    for y in range(max(ys), min(ys) - 1, -1):
        row = ""
        for x in range(min(xs), max(xs) + 1):
            if (x, y) in cells:
                row += marks[legend.index(cells[(x, y)])]
            elif (x, y) in pm:
                row += "P"
            elif (x, y) in fm:
                row += "o"
            else:
                row += "."
        lines.append("y%4d |%s|" % (y, row))
    return "\n".join(lines)


def write(out_dir, name, cells, players, fruits, pieces):
    """Writes what Unity reads: the cells, the spawns, and the piece list the
    editor-side verification casts against. The last one is emitted here rather
    than by hand because a verification file one layout behind reports faults in a
    scene that is correct - it did, once."""
    import io
    import os
    with io.open(os.path.join(out_dir, name + "_cells.txt"), "w", encoding="utf-8", newline="\n") as f:
        for (x, y), (c, r) in sorted(cells.items()):
            f.write("%d %d %d %d\n" % (x, y, c, r))
    with io.open(os.path.join(out_dir, name + "_spawns.txt"), "w", encoding="utf-8", newline="\n") as f:
        for i, (x, y) in enumerate(players, 1):
            f.write("P %d %.4f %.4f\n" % (i, x, y))
        for i, (x, y, on) in enumerate(fruits, 1):
            f.write("F %d %.4f %.4f\n" % (i, x, y))
    with io.open(os.path.join(out_dir, name + "_pieces.txt"), "w", encoding="utf-8", newline="\n") as f:
        for p in pieces:
            f.write("%s %.4f %.4f %.4f\n" % (p.name, p.x0 * 0.5, (p.x1 + 1) * 0.5, (p.row + 1) * 0.5))
