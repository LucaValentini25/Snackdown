#!/usr/bin/env python3
"""Render the project board from its single source of truth.

`docs/board.json` is the only file anyone edits. This script turns it into the two
views nobody should ever hand-write:

    docs/06-board.md    tracked   — what a reader sees on GitHub
    docs/local/board.html  local  — the rich board, gitignored

The reason for a generator instead of two markdown files: a board is edited on every
task, and two hand-kept copies of a thing edited that often will disagree within a
week. One source, two renders, no drift.

Standard library only, by design. A board that needs `pip install` is a board that
stops being updated the first time an environment changes.

Usage:
    python tools/board/build_board.py            # render both views
    python tools/board/build_board.py --check    # exit 1 if the views are stale
"""

from __future__ import annotations

import argparse
import html
import json
import sys
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SOURCE = REPO_ROOT / "docs" / "board.json"
MARKDOWN_OUT = REPO_ROOT / "docs" / "06-board.md"
HTML_OUT = REPO_ROOT / "docs" / "local" / "board.html"

GENERATED_NOTICE = "Generated from `docs/board.json` by `tools/board/build_board.py` — do not edit by hand."

# Order matters: it is the order statuses are counted and displayed in.
STATUSES = {
    "done": ("Done", "ok"),
    "in-progress": ("In progress", "run"),
    "blocked": ("Blocked", "bad"),
    "todo": ("To do", "neutral"),
    "dropped": ("Dropped", "muted"),
}

STATUS_MARK = {
    "done": "x",
    "in-progress": "~",
    "blocked": "!",
    "todo": " ",
    "dropped": "-",
}


class BoardError(Exception):
    """A problem with the board data that a person has to fix."""


# ======================================================================================
#  Loading and validation
# ======================================================================================


@dataclass
class Board:
    data: dict

    @property
    def project(self) -> str:
        return self.data.get("project", "Project")

    @property
    def updated(self) -> str:
        return self.data.get("updated", "")

    @property
    def epics(self) -> list[dict]:
        return self.data.get("epics", [])

    @property
    def decisions(self) -> list[dict]:
        return self.data.get("decisions", [])

    @property
    def risks(self) -> list[dict]:
        return self.data.get("risks", [])

    @property
    def log(self) -> list[dict]:
        return self.data.get("log", [])


def load(path: Path = SOURCE) -> Board:
    if not path.exists():
        raise BoardError(f"No board at {path}. Nothing to render.")

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise BoardError(f"{path.name} is not valid JSON: line {exc.lineno}, {exc.msg}") from exc

    validate(data)
    return Board(data)


def validate(data: dict) -> None:
    """Fail loudly on the mistakes that would otherwise render as a silently wrong board."""
    if not isinstance(data.get("epics"), list):
        raise BoardError("`epics` must be a list.")

    seen_tasks: set[str] = set()

    for epic in data["epics"]:
        for key in ("id", "title", "status"):
            if key not in epic:
                raise BoardError(f"Epic {epic.get('id', '?')} is missing `{key}`.")
        if epic["status"] not in STATUSES:
            raise BoardError(f"Epic {epic['id']}: unknown status {epic['status']!r}.")

        for task in epic.get("tasks", []):
            for key in ("id", "title", "status"):
                if key not in task:
                    raise BoardError(f"A task in epic {epic['id']} is missing `{key}`.")
            if task["status"] not in STATUSES:
                raise BoardError(f"Task {task['id']}: unknown status {task['status']!r}.")
            if task["id"] in seen_tasks:
                raise BoardError(f"Duplicate task id {task['id']!r}.")
            seen_tasks.add(task["id"])

    for decision in data.get("decisions", []):
        for key in ("id", "date", "title", "choice", "why"):
            if key not in decision:
                raise BoardError(f"Decision {decision.get('id', '?')} is missing `{key}`.")


# ======================================================================================
#  Shared computation
# ======================================================================================


def tally(tasks: list[dict]) -> dict[str, int]:
    counts = {key: 0 for key in STATUSES}
    for task in tasks:
        counts[task["status"]] += 1
    return counts


def countable(tasks: list[dict]) -> list[dict]:
    """Dropped tasks are history, not scope — they do not drag a percentage down."""
    return [task for task in tasks if task["status"] != "dropped"]


def percent_done(tasks: list[dict]) -> int:
    live = countable(tasks)
    if not live:
        return 0
    return round(100 * sum(1 for t in live if t["status"] == "done") / len(live))


def all_tasks(board: Board) -> list[dict]:
    return [task for epic in board.epics for task in epic.get("tasks", [])]


# ======================================================================================
#  Markdown view
# ======================================================================================


def render_markdown(board: Board) -> str:
    out: list[str] = []
    w = out.append

    w("# 06 — Board")
    w("")
    w(f"> {GENERATED_NOTICE}")
    w("")
    w(f"Live state of the work: what is done, what is being done now, every decision taken and why,")
    w(f"and what is known to be wrong. Updated when a task, an epic or a working session closes.")
    w("")
    w(f"**Last updated:** {board.updated}")
    w("")

    tasks = all_tasks(board)
    counts = tally(tasks)
    w(f"**Overall:** {percent_done(tasks)}% — "
      f"{counts['done']} done, {counts['in-progress']} in progress, "
      f"{counts['blocked']} blocked, {counts['todo']} to do"
      + (f", {counts['dropped']} dropped" if counts["dropped"] else "")
      + f", across {len(board.epics)} epics.")
    w("")

    # --- epics -----------------------------------------------------------------------
    w("## Epics")
    w("")
    w("| Epic | Phase | Status | Progress |")
    w("|---|:---:|---|---:|")
    for epic in board.epics:
        label, _ = STATUSES[epic["status"]]
        epic_tasks = epic.get("tasks", [])
        done = sum(1 for t in countable(epic_tasks) if t["status"] == "done")
        total = len(countable(epic_tasks))
        w(f"| [{epic['title']}](#{slug(epic['title'])}) | {epic.get('phase', '—')} "
          f"| {label} | {done}/{total} |")
    w("")

    for epic in board.epics:
        w(f"### {epic['title']}")
        w("")
        if epic.get("goal"):
            w(epic["goal"])
            w("")
        epic_tasks = epic.get("tasks", [])
        if not epic_tasks:
            w("*No tasks broken out yet.*")
            w("")
            continue

        w("| | Task | Verified by | Notes |")
        w("|:---:|---|---|---|")
        for task in epic_tasks:
            mark = STATUS_MARK[task["status"]]
            note = task.get("notes", "") or ""
            if task.get("pr"):
                note = (note + " " if note else "") + f"(PR #{task['pr']})"
            w(f"| `[{mark}]` | {task['title']} | {task.get('test', '—')} | {note or '—'} |")
        w("")

    # --- decisions -------------------------------------------------------------------
    if board.decisions:
        w("## Decisions")
        w("")
        w("Every choice that closed off an alternative, with the reasoning that closed it. This is")
        w("the part of the board worth reading a year from now.")
        w("")
        for decision in reversed(board.decisions):
            w(f"### {decision['id']} — {decision['title']}")
            w("")
            w(f"*{decision['date']}*"
              + (f" · epic **{decision['epic']}**" if decision.get("epic") else ""))
            w("")
            w(f"**Chosen:** {decision['choice']}")
            w("")
            w(f"**Why:** {decision['why']}")
            w("")
            if decision.get("rejected"):
                w(f"**Rejected:** {decision['rejected']}")
                w("")

    # --- risks -----------------------------------------------------------------------
    if board.risks:
        w("## Known problems")
        w("")
        w("Things that are wrong and are not being fixed yet, recorded so they are a decision")
        w("rather than an oversight.")
        w("")
        w("| | Problem | Impact | Status |")
        w("|:---:|---|---|---|")
        for risk in board.risks:
            state = risk.get("status", "open")
            mark = "x" if state == "closed" else " "
            w(f"| `[{mark}]` | {risk['title']} | {risk.get('impact', '—')} | {state} |")
        w("")

    # --- log -------------------------------------------------------------------------
    if board.log:
        w("## Session log")
        w("")
        for entry in reversed(board.log):
            w(f"- **{entry['date']}** — {entry['entry']}")
        w("")

    return "\n".join(out) + "\n"


def slug(title: str) -> str:
    keep = [c.lower() if c.isalnum() else "-" for c in title]
    out = "".join(keep)
    while "--" in out:
        out = out.replace("--", "-")
    return out.strip("-")


# ======================================================================================
#  HTML view
# ======================================================================================

CSS = """
:root {
  --bg:#fbfaf8; --surface:#fff; --surface-2:#f4f2ee; --border:#e2ded6;
  --text:#1e1c19; --muted:#6b665e;
  --accent:#c2410c; --accent-soft:#fff1e9;
  --ok:#15803d; --ok-soft:#eaf6ee;
  --run:#1d4ed8; --run-soft:#e8effd;
  --warn:#a16207; --warn-soft:#fdf6e3;
  --bad:#b91c1c; --bad-soft:#fdeceb;
  --mono:ui-monospace,"Cascadia Code","JetBrains Mono",Consolas,monospace;
}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){
  --bg:#171614; --surface:#201e1b; --surface-2:#262421; --border:#35322d;
  --text:#ece8e1; --muted:#a09a90;
  --accent:#fb923c; --accent-soft:#2e1d12;
  --ok:#4ade80; --ok-soft:#17251b;
  --run:#7dabff; --run-soft:#141d2e;
  --warn:#fbbf24; --warn-soft:#2a2313;
  --bad:#f87171; --bad-soft:#2b1717;
}}
:root[data-theme="dark"]{
  --bg:#171614; --surface:#201e1b; --surface-2:#262421; --border:#35322d;
  --text:#ece8e1; --muted:#a09a90;
  --accent:#fb923c; --accent-soft:#2e1d12;
  --ok:#4ade80; --ok-soft:#17251b;
  --run:#7dabff; --run-soft:#141d2e;
  --warn:#fbbf24; --warn-soft:#2a2313;
  --bad:#f87171; --bad-soft:#2b1717;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);
  font:16px/1.6 -apple-system,BlinkMacSystemFont,"Segoe UI",Inter,system-ui,sans-serif;
  -webkit-font-smoothing:antialiased}
.wrap{max-width:1000px;margin:0 auto;padding:3rem 1.25rem 6rem}
.eyebrow{font:600 .72rem/1 var(--mono);letter-spacing:.12em;text-transform:uppercase;
  color:var(--accent);margin-bottom:.7rem}
h1{font-size:clamp(1.8rem,5vw,2.5rem);line-height:1.15;margin:0 0 .5rem;letter-spacing:-.02em}
.sub{color:var(--muted);margin:0 0 2rem;max-width:62ch}
h2{font-size:1.3rem;margin:3rem 0 1rem;padding-bottom:.5rem;border-bottom:2px solid var(--border)}
h2 .num{color:var(--accent);font:600 .9rem/1 var(--mono);margin-right:.6rem;vertical-align:.12em}
p{margin:0 0 1rem}
code{font:.875em var(--mono);background:var(--surface-2);padding:.12em .38em;border-radius:4px}
a{color:var(--accent)}

.summary{display:grid;gap:.9rem;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));margin:0 0 1.5rem}
.stat{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:.9rem 1.1rem}
.stat .n{font:700 1.7rem/1.1 var(--mono);letter-spacing:-.02em}
.stat .k{font-size:.78rem;color:var(--muted);text-transform:uppercase;letter-spacing:.05em;margin-top:.25rem}
.stat.ok .n{color:var(--ok)} .stat.run .n{color:var(--run)}
.stat.bad .n{color:var(--bad)} .stat.accent .n{color:var(--accent)}

.bar{height:8px;border-radius:999px;background:var(--surface-2);overflow:hidden;display:flex}
.bar i{display:block;height:100%}
.bar i.done{background:var(--ok)} .bar i.run{background:var(--run)}
.bar i.bad{background:var(--bad)}

.epic{background:var(--surface);border:1px solid var(--border);border-radius:14px;
  padding:1.2rem 1.35rem;margin:1rem 0}
.epic.current{border-color:var(--accent);box-shadow:0 0 0 1px var(--accent)}
.epic header{display:flex;flex-wrap:wrap;align-items:baseline;gap:.6rem;margin-bottom:.5rem}
.epic h3{margin:0;font-size:1.1rem;flex:1;min-width:12rem}
.epic .goal{color:var(--muted);font-size:.92rem;margin:.3rem 0 .9rem}
.epic .meta{font:.78rem var(--mono);color:var(--muted)}

table{border-collapse:collapse;width:100%;font-size:.9rem}
.scroller{overflow-x:auto;margin:1rem 0;border:1px solid var(--border);border-radius:12px}
th,td{text-align:left;padding:.6rem .8rem;border-bottom:1px solid var(--border);vertical-align:top}
th{background:var(--surface-2);font-weight:600;font-size:.75rem;text-transform:uppercase;
  letter-spacing:.04em;color:var(--muted)}
tr:last-child td{border-bottom:none}
.epic table{margin-top:.9rem}
.epic th{background:transparent;border-bottom:1px solid var(--border)}
td.tick{width:1.6rem;text-align:center;font:700 .85rem var(--mono)}
tr.is-done td:not(.tick){color:var(--muted)}
tr.is-done td.name{text-decoration:line-through;text-decoration-color:var(--border)}
tr.is-dropped td{color:var(--muted);opacity:.65}
tr.is-dropped td.name{text-decoration:line-through}
td.name{font-weight:500}
td.test{color:var(--muted);font-size:.85rem}

.pill{display:inline-block;font:600 .7rem/1.5 var(--mono);padding:.1rem .55rem;border-radius:999px;white-space:nowrap}
.pill.ok{background:var(--ok-soft);color:var(--ok)}
.pill.run{background:var(--run-soft);color:var(--run)}
.pill.bad{background:var(--bad-soft);color:var(--bad)}
.pill.warn{background:var(--warn-soft);color:var(--warn)}
.pill.neutral{background:var(--surface-2);color:var(--muted)}
.pill.muted{background:transparent;color:var(--muted);border:1px solid var(--border)}

.decision{background:var(--surface);border:1px solid var(--border);border-left:3px solid var(--accent);
  border-radius:12px;padding:1rem 1.2rem;margin:.9rem 0}
.decision h4{margin:0 0 .2rem;font-size:1rem}
.decision .when{font:.75rem var(--mono);color:var(--muted);margin-bottom:.7rem}
.decision dl{margin:0;display:grid;grid-template-columns:auto 1fr;gap:.35rem .8rem;font-size:.92rem}
.decision dt{font:600 .72rem/1.6 var(--mono);text-transform:uppercase;letter-spacing:.05em;color:var(--muted);white-space:nowrap}
.decision dd{margin:0}

.timeline{list-style:none;padding:0;margin:1rem 0}
.timeline li{position:relative;padding:0 0 1rem 1.6rem;border-left:2px solid var(--border);margin-left:.4rem}
.timeline li:last-child{border-left-color:transparent;padding-bottom:0}
.timeline li::before{content:"";position:absolute;left:-5px;top:.45rem;width:8px;height:8px;
  border-radius:50%;background:var(--accent)}
.timeline .when{font:600 .75rem var(--mono);color:var(--accent);display:block}

footer{color:var(--muted);font-size:.85rem;border-top:1px solid var(--border);
  padding-top:1.2rem;margin-top:3.5rem}
"""


def e(text) -> str:
    return html.escape(str(text), quote=True)


def render_html(board: Board) -> str:
    tasks = all_tasks(board)
    counts = tally(tasks)
    overall = percent_done(tasks)
    current = board.data.get("currentEpic")

    parts: list[str] = []
    w = parts.append

    w("<!doctype html>")
    w('<html lang="en"><head><meta charset="utf-8">')
    w('<meta name="viewport" content="width=device-width, initial-scale=1">')
    w(f"<title>{e(board.project)} — Board</title>")
    w(f"<style>{CSS}</style></head><body><div class=\"wrap\">")

    w(f'<div class="eyebrow">{e(board.project)} · project board · updated {e(board.updated)}</div>')
    w("<h1>What is done, what is next, and why</h1>")
    w('<p class="sub">Generated from <code>docs/board.json</code>. Every task, every decision with '
      "its reasoning, and every known problem that has not been fixed yet.</p>")

    # --- summary ---------------------------------------------------------------------
    live = len(countable(tasks))
    w('<div class="summary">')
    w(f'<div class="stat accent"><div class="n">{overall}%</div><div class="k">Complete</div></div>')
    w(f'<div class="stat ok"><div class="n">{counts["done"]}</div><div class="k">Done</div></div>')
    w(f'<div class="stat run"><div class="n">{counts["in-progress"]}</div><div class="k">In progress</div></div>')
    w(f'<div class="stat bad"><div class="n">{counts["blocked"]}</div><div class="k">Blocked</div></div>')
    w(f'<div class="stat"><div class="n">{counts["todo"]}</div><div class="k">To do</div></div>')
    w("</div>")
    w(progress_bar(tasks, live))

    # --- epics -----------------------------------------------------------------------
    w('<h2><span class="num">01</span>Epics</h2>')
    for epic in board.epics:
        w(render_epic(epic, is_current=epic["id"] == current))

    # --- decisions -------------------------------------------------------------------
    if board.decisions:
        w('<h2><span class="num">02</span>Decisions</h2>')
        w("<p>Every choice that closed off an alternative, with the reasoning that closed it — newest "
          "first. This is the part worth reading a year from now.</p>")
        for decision in reversed(board.decisions):
            w(render_decision(decision))

    # --- risks -----------------------------------------------------------------------
    if board.risks:
        w('<h2><span class="num">03</span>Known problems</h2>')
        w("<p>Wrong and not being fixed yet — recorded so it stays a decision rather than becoming "
          "an oversight.</p>")
        w('<div class="scroller"><table><thead><tr>'
          "<th>Problem</th><th>Impact</th><th>Status</th></tr></thead><tbody>")
        for risk in board.risks:
            state = risk.get("status", "open")
            pill = "ok" if state == "closed" else ("bad" if risk.get("severity") == "high" else "warn")
            w(f"<tr><td>{e(risk['title'])}</td><td>{e(risk.get('impact', '—'))}</td>"
              f'<td><span class="pill {pill}">{e(state)}</span></td></tr>')
        w("</tbody></table></div>")

    # --- log -------------------------------------------------------------------------
    if board.log:
        w('<h2><span class="num">04</span>Session log</h2>')
        w('<ul class="timeline">')
        for entry in reversed(board.log):
            w(f'<li><span class="when">{e(entry["date"])}</span>{e(entry["entry"])}</li>')
        w("</ul>")

    w("<footer>Rendered by <code>tools/board/build_board.py</code>. This file is gitignored — "
      "edit <code>docs/board.json</code> and re-run the script, never this page.</footer>")
    w("</div></body></html>")

    return "\n".join(parts) + "\n"


def progress_bar(tasks: list[dict], live: int) -> str:
    if live == 0:
        return ""
    counts = tally(tasks)
    segments = []
    for key, css in (("done", "done"), ("in-progress", "run"), ("blocked", "bad")):
        pct = 100 * counts[key] / live
        if pct > 0:
            segments.append(f'<i class="{css}" style="width:{pct:.4g}%"></i>')
    return '<div class="bar">' + "".join(segments) + "</div>"


def render_epic(epic: dict, is_current: bool) -> str:
    label, css = STATUSES[epic["status"]]
    epic_tasks = epic.get("tasks", [])
    live = countable(epic_tasks)
    done = sum(1 for t in live if t["status"] == "done")

    out = [f'<div class="epic{" current" if is_current else ""}">']
    out.append("<header>")
    out.append(f"<h3>{e(epic['title'])}</h3>")
    if is_current:
        out.append('<span class="pill accent" style="background:var(--accent-soft);color:var(--accent)">current</span>')
    out.append(f'<span class="pill {css}">{e(label)}</span>')
    out.append(f'<span class="meta">phase {e(epic.get("phase", "—"))} · {done}/{len(live)}</span>')
    out.append("</header>")

    if epic.get("goal"):
        out.append(f'<p class="goal">{e(epic["goal"])}</p>')

    out.append(progress_bar(epic_tasks, len(live)))

    if epic_tasks:
        out.append("<table><thead><tr><th></th><th>Task</th><th>Verified by</th><th>Notes</th>"
                   "</tr></thead><tbody>")
        for task in epic_tasks:
            status = task["status"]
            _, tcss = STATUSES[status]
            tick = {"done": "✓", "in-progress": "▸", "blocked": "!", "todo": "", "dropped": "×"}[status]
            note = task.get("notes", "") or ""
            if task.get("pr"):
                note = (note + " " if note else "") + f"(PR #{task['pr']})"
            out.append(
                f'<tr class="is-{status}">'
                f'<td class="tick" style="color:var(--{tcss if tcss != "neutral" else "muted"})">{tick}</td>'
                f'<td class="name">{e(task["title"])}</td>'
                f'<td class="test">{e(task.get("test", "—"))}</td>'
                f'<td class="test">{e(note or "—")}</td></tr>'
            )
        out.append("</tbody></table>")

    out.append("</div>")
    return "".join(out)


def render_decision(decision: dict) -> str:
    out = ['<div class="decision">']
    out.append(f"<h4>{e(decision['id'])} — {e(decision['title'])}</h4>")
    when = e(decision["date"])
    if decision.get("epic"):
        when += f" · {e(decision['epic'])}"
    out.append(f'<div class="when">{when}</div>')
    out.append("<dl>")
    out.append(f"<dt>Chosen</dt><dd>{e(decision['choice'])}</dd>")
    out.append(f"<dt>Why</dt><dd>{e(decision['why'])}</dd>")
    if decision.get("rejected"):
        out.append(f"<dt>Rejected</dt><dd>{e(decision['rejected'])}</dd>")
    out.append("</dl></div>")
    return "".join(out)


# ======================================================================================
#  Entry point
# ======================================================================================


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Render the project board.")
    parser.add_argument("--check", action="store_true",
                        help="exit 1 if the rendered views are out of date, and write nothing")
    args = parser.parse_args(argv)

    try:
        board = load()
    except BoardError as exc:
        print(f"board: {exc}", file=sys.stderr)
        return 1

    markdown = render_markdown(board)
    page = render_html(board)

    if args.check:
        stale = []
        if not MARKDOWN_OUT.exists() or MARKDOWN_OUT.read_text(encoding="utf-8") != markdown:
            stale.append(MARKDOWN_OUT)
        if not HTML_OUT.exists() or HTML_OUT.read_text(encoding="utf-8") != page:
            stale.append(HTML_OUT)
        if stale:
            for path in stale:
                print(f"board: stale — {path.relative_to(REPO_ROOT).as_posix()}", file=sys.stderr)
            print("board: run `python tools/board/build_board.py`", file=sys.stderr)
            return 1
        print("board: up to date")
        return 0

    MARKDOWN_OUT.write_text(markdown, encoding="utf-8")
    HTML_OUT.parent.mkdir(parents=True, exist_ok=True)
    HTML_OUT.write_text(page, encoding="utf-8")

    tasks = all_tasks(board)
    counts = tally(tasks)
    print(f"board: {percent_done(tasks)}% - {counts['done']} done, "
          f"{counts['in-progress']} in progress, {counts['todo']} to do")
    print(f"board: wrote {MARKDOWN_OUT.relative_to(REPO_ROOT).as_posix()}")
    print(f"board: wrote {HTML_OUT.relative_to(REPO_ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
