# CLAUDE.md — Snackdown

Working rules for this repository. They exist because this is a **portfolio project**: the commit
log, the code style and the docs are part of what's being judged, not scaffolding around it.

## Language

- Code, comments, XML docs, commit messages, PR descriptions and everything under `docs/`: **English**.
- Conversation with Luca: **Spanish**.

## Reporting back

When a block, a phase, or any piece of work is finished, report it **short**. Three things, plain
sentences, nothing else:

1. **What was done** — a few lines, not a tour.
2. **Why** — one or two sentences. The long reasoning belongs in the commit message and the XML
   docs, which is where it is already being written.
3. **QA** — the exact steps to test it, or "nothing to test" when there is nothing.

No essays, no repeating what the commit already says, no narrating the debugging journey. If
something genuinely needs a longer explanation, ask first instead of assuming.

The detail is not lost by doing this — it lives in the commit log and the code comments, where it
can be read on demand instead of arriving unrequested.

## Ask before deciding

Do not assume. Stop and ask when a change would:

- touch the network model, tick rate, or where authority lives;
- add a package, dependency, or third-party asset;
- contradict something already settled in `README.md` or `docs/00`–`docs/04`;
- create, delete, or rename Unity assets (scenes, prefabs, `.asset`, `.inputactions`);
- pick between two reasonable designs where the wrong one means redoing the work.

Mechanical steps inside an already-agreed direction don't need a question — do them and report.

## Code conventions

Standard C# as the industry writes it (Microsoft C# coding conventions + Unity's style guide),
which is what this codebase already follows:

| Element | Convention |
|---|---|
| Types, methods, properties, events, consts | `PascalCase` |
| Local variables, parameters | `camelCase` |
| Private/protected fields | `_camelCase` (including `[SerializeField]` ones) |
| Interfaces | `IPascalCase` |
| Namespaces | `Snackdown.<Layer>.<Area>`, matching the folder path |
| Braces | Allman — opening brace on its own line |
| Indentation | 4 spaces, no tabs |
| Namespace style | Block-scoped (`namespace X { }`), everything inside it |
| Access modifiers | **Always explicit** (`private readonly`, not `readonly`) |
| `var` | Only when the right-hand side already states the type |
| One file | One top-level type, file named after it |

Unity specifics:

- `[SerializeField] private` over `public` fields — never expose state just to reach it from the Inspector.
- Cache component lookups in `Awake`; no `GetComponent` in `Update` or in simulation code.
- Nothing in the simulation path reads `Time`, `Transform` or `Rigidbody2D` state directly — it
  arrives as an argument, or reconciliation replay stops being reproducible.

Known debt: the existing scripts omit the implicit `private` modifier. Normalizing them is a
separate, isolated commit — ask before doing it.

## Comments and XML docs

Two different things, two different rules.

**Required:** an XML `<summary>` on every public type and public member of the netcode layer, and a
`<remarks>` on the non-obvious ones explaining **why the design is this way** — the tradeoff, the
alternative that was rejected, the failure it prevents. That reasoning is the deliverable of this
project; it stays.

**Forbidden:** comments that restate the code (`// increment the tick`), filler `<summary>` on
self-evident members (`GetX` → "Gets X"), commented-out code, and TODOs without an owner or a phase.

If a comment is needed to explain *what* the code does, the code needs renaming, not a comment.

## Documentation

- `docs/*.md` is the **single source of truth**, tracked in git.
- `docs/audit/` holds a dated, ten-domain audit of the project against its own claims. It is a
  **snapshot of one commit**, not a living document: do not edit its findings to match later fixes.
  What was remediated is recorded in its synthesis under *Remediation status*, and the fixes
  themselves belong in the docs they correct.
- **HTML pages live in `docs/local/` and are gitignored.** They are local views for reading and
  reviewing the work — never new content, never a deliverable. Anything worth keeping goes in the
  markdown first; two hand-written copies drift and end up contradicting each other.
- Document a system **when the phase that builds it closes**, not before. Documenting the auth/Relay
  flow while it's still a plan produces docs that lie by Phase 3.
- When a decision changes, `README.md` and the affected `docs/` file are updated **in the same commit**
  as the code. A doc that disagrees with the code is worse than no doc.

## Git

- **Never add Claude as a co-author or as a commit trailer.** The history is Luca's.
- The remote is `origin` on GitHub, and the flow described in
  [docs/04-workflow.md](docs/04-workflow.md) — feature branches, PRs into `dev`, tags on `main` —
  **is in effect.** Branch naming, commit message shape and the release model live there.
- Commit messages explain **why**, in imperative mood, whenever the diff doesn't make it obvious.

### What Claude does alone, and what needs Luca

| Action | |
|---|---|
| Commit and push on `feature/*` and `bugfix/*` | **On its own** |
| Open a pull request against `dev` | **On its own** |
| Merge any pull request | **Needs Luca's ok** |
| Commit or push directly to `dev` or `main` | **Needs Luca's ok** |
| Create a tag, cut a release | **Needs Luca's ok** |
| Force-push anything, delete a remote branch | **Needs Luca's ok** |

Opening a PR is not merging it. Review is the point of the flow; a PR Claude both writes and merges
is a commit with extra steps.

### The Unity YAML merge driver is per machine

`.gitattributes` routes 18 Unity file types to `merge=unityyamlmerge`, but the driver itself is
**local Git config — it does not travel with a clone.** Where it is missing, Git silently falls back
to a plain text merge, and a scene or prefab conflict produces a file Unity refuses to open.

Check before the first merge on any new clone or machine; empty output means it is not set up:

```bash
git config --get merge.unityyamlmerge.driver
```

The setup command (and the path to adjust) is in
[docs/04-workflow.md](docs/04-workflow.md#one-time-local-setup).

## Before every commit or PR

Run through this and report the result — no silent skips:

1. The project **compiles**: no errors, no new warnings.
2. The **EditMode tests pass.** They run in under a second and nothing else in this repository runs
   them, so skipping this means nobody does. If a change makes a test fail, that is the finding —
   report it, don't adjust the test to match.
3. The Unity **console is clean** in Play mode (use the Unity MCP; if the editor isn't open, say so
   instead of assuming). Note that Unity **defers script compilation while a session is live**, so a
   change made during Play mode has not been compiled and cannot have been verified — see
   [docs/05](docs/05-validation.md#pitfalls-that-invalidate-a-run).
4. **No leftovers:** debug `Debug.Log`, dead code, unused fields, orphaned files, unowned TODOs,
   code made obsolete by this change and left behind.
5. **No contradictions:** the change doesn't conflict with `README.md`, `docs/`, or another system's
   assumptions. If it does, resolve it here — not later. This includes numbers and file paths in
   comments and XML docs, not just prose in `docs/` — a `<remarks>` block claiming a packet is 120
   bytes when it is 176 is the same failure as a stale README.
6. The change stays **inside the current phase** of `docs/03-roadmap.md`. No drive-by refactors.

## Off-limits

- `d:\Unity Projects\Final-Redes` — the original university project. **Read-only reference**, never
  a destination for changes.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` — regenerated by the editor, never edited or committed.
- Unity YAML (scenes, prefabs, `.asset`) is not hand-edited. Go through the Unity MCP, or ask.
