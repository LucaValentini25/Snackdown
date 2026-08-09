# 04 — Git Workflow

How work moves through this repository. It's a simplified Git Flow — the branching model most
game teams converge on — kept as small as it can be while still being the real thing.

## Branches

| Branch | Role | Rules |
|---|---|---|
| `main` | **Release.** Every commit is a version someone could download and play. | Never committed to directly. Only receives merges from `dev` at release time, and each one gets a tag. Currently still at the Phase 0 scaffold — see [Releases](#releases) for why that is deliberate. |
| `dev` | **Integration.** The current state of development; always compiles, always runs. | Only receives merges from PRs. |
| `feature/*` | New work. Branches off `dev`, merges back into `dev` via PR. | Short-lived — days, not weeks. |
| `bugfix/*` | A fix for something broken in `dev`. Same lifecycle as a feature. | |
| `hotfix/*` | A fix for something broken in a **release**. Branches off `main`. | Merges into **both** `main` (tagged as a patch) and `dev`, or the fix is lost on the next release. |

```
main     A───────────────────────────M─────▶  v0.1.0
          \                         /
dev        B───C─────E─────────────G────────▶
            \     /   \           /
feature/x    C───┘     \         /
feature/y               F───────┘
```

### Naming

`<type>/<short-kebab-description>` — lowercase, no ticket numbers (there's no tracker).

```
feature/netcode-core
feature/relay-connection
bugfix/reconciliation-overshoot
hotfix/crash-on-late-join
```

## Commits

Imperative mood, a subject line under ~72 characters, and a body that explains **why** whenever the
reason isn't obvious from the diff. On a project whose whole point is demonstrating engineering
judgement, the commit log is part of the deliverable — "fix stuff" throws that away.

```
Phase 1: predicted character over a fixed 30 Hz tick

PlayerMotor.Simulate is a pure function so a reconciliation can replay it.
A dynamic Rigidbody2D could not: Physics2D.Simulate steps the entire world
and does not reproduce identically across machines.
```

## Pull requests

Every change reaches `dev` through a PR, including solo work. It's what makes the history
reviewable later, and it's the habit that transfers to a team.

1. Branch off the latest `dev`.
2. Commit, push, open the PR against `dev`.
3. Fill in the template: what changed, why, and **how it was verified**.
4. Merge with **Squash and merge** when the branch is a messy work-in-progress; use a **merge
   commit** when the commits are individually meaningful and worth keeping (the Phase commits are).
5. Delete the branch after merging.

## Releases

**Releases start at Phase 5, not at each phase.** `main` holds the Phase 0 scaffold and stays there
until there is something worth downloading; `dev` is where the project lives in the meantime.

This is a correction, and the reason is worth recording rather than quietly rewriting. The original
plan was `dev` → `main` with a tag at the end of every phase, and `1.0.0` at Phase 3. Three phases
landed and none of the four steps happened once — no merge, no tag, no release, `bundleVersion` still
at its initial value. A process with a 0% execution rate is not a process, and the honest fix was
either to start executing it or to describe what is actually being done. Cutting a `1.0.0` off a build
that has never been produced would have been the worse of the two.

So: no tags yet, deliberately. When Phase 5 closes and a build exists:

- **Versioning:** [SemVer](https://semver.org). While pre-1.0, `0.MINOR.PATCH` — each completed
  phase bumps MINOR, fixes bump PATCH. `1.0.0` is the first release with a build someone can run
  without installing Unity.
- Tag on `main`: `git tag -a v1.0.0 -m "..."` then `git push --tags`.
- Cut a GitHub Release from the tag, with notes and a playable build attached.
- Keep `bundleVersion` in `ProjectSettings` in sync with the tag.

Note what this does *not* change: the branch and PR half of this document is followed exactly — every
commit on `dev` arrived through a PR, and `hotfix/*` keeps its meaning for the day a release exists.

## One-time local setup

Unity's YAML doesn't survive a normal text merge. `.gitattributes` already routes scenes, prefabs
and assets to Unity's own merge tool, but **that only names a driver — it doesn't define one.** The
definition is Git config, which lives on the machine and never travels with a clone. Set it once per
machine, with `--global` so a re-clone doesn't lose it:

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '"D:/Unity editors/6000.3.14f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config --global merge.unityyamlmerge.recursive binary
```

Adjust the path to your Unity install. The third line matters as much as the other two: when a merge
has more than one common ancestor, Git first merges the ancestors together, and `recursive binary`
stops it from text-merging Unity YAML behind the driver's back.

Verify before the first merge on a new machine — empty output means it is **not** set up:

```bash
git config --get merge.unityyamlmerge.driver
```

Without this, Git falls back to a plain text merge and reports success. The result is a scene file
Unity refuses to open, and the only way out is picking one side wholesale.

## Notes

- **No Git LFS.** The art here is pixel art in the kilobyte range; LFS would add quotas and a
  smudge/clean step for nothing. Revisit if large audio or textures ever arrive.
- **`Library/`, `Temp/`, `Logs/` are not tracked** — they're regenerated by the editor. Everything
  needed to open the project cleanly *is* tracked, including `Packages/packages-lock.json`, which
  pins the exact package versions.
