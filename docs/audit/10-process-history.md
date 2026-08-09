# Process & Git History Audit

**Auditor:** A10 · **Range:** `main..dev` (55 commits, `8074fe6`..`10a2a13`, 2026-07-25 → 2026-08-08)
· **Author:** 1 (`LucaValentini25`) · **Method:** read-only `git log/show/diff/ls-tree/config`, plus
`docs/04-workflow.md`, `CLAUDE.md`, `.gitattributes`, `.gitignore`, `.github/`.

## Verdict

The commit log holds up under inspection, and it is the strongest artifact in the repository. All 36
non-merge commits reached `dev` through one of 19 PRs — **zero direct commits to `dev`** — every merge
is a clean, content-free merge commit, there are **no reverts, no conflict markers, no rebase scars and
no evil merges**, and the bodies carry 55,847 bytes of genuine reasoning (median 24 lines/commit) that
records rejected alternatives and measured experiments rather than restating diffs. I read six bodies in
full (`e99a6fb`, `154dbec`, `fa2a289`, `d7019ed`, `8a44c2e`, `c9cd571`) and every one explains *why*.
`d7019ed` — designing an abstraction, costing it, and then deliberately not building it — is the single
best piece of evidence in the repo **against** the over-engineering hypothesis.

What does not hold up is the half of the process that lives outside the commit itself. The **release
model in `docs/04-workflow.md` has never been exercised once**: `main` is 55 commits behind and still
sits on the Phase 0 scaffold, there are **zero tags**, `bundleVersion` never moved off `0.1.0`, and the
project's own definition of "portfolio-ready" (Phases 0–3) is met on `dev` and invisible on `main`. The
**PR gate is ceremonial** — median 16 minutes from last commit to merge, 14 of 19 branches are a single
commit, three PRs were merged inside 60 seconds — and it demonstrably caught nothing: a join-breaking
regression the author introduced in `32bb167` survived **six consecutive merged PRs over ~47 hours**
before `c9cd571` found it by play-testing. There is no CI to compensate: `.github/` contains a PR
template and nothing else, so the 36 unit tests that run in 0.46 s are never run by anything but a human.
Finally, `CLAUDE.md`'s same-commit docs rule held for `docs/` (13 of 17 code-bearing PRs touched a doc)
but failed completely for `README.md`, untouched for the last **14 PRs** and now instructing the reader
to open a scene deleted in `97e7e3f`.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Commit message quality | **5** | 55,847 B of body prose over 36 commits; median 24 lines; 33/36 explain *why*; 1 outlier (`c913be7`) |
| Branch & merge structure | **5** | 19/19 PRs, 0 direct-to-`dev`, 0 evil merges, 0 conflicts, 0 reverts, max branch life 5 days |
| Review effectiveness | **2** | Median 16 min last-commit→merge; 14/19 single-commit PRs; 6 PRs merged over a broken join |
| Release & tagging discipline | **1** | 0 tags, `main` 55 behind at Phase 0, `bundleVersion` still `0.1.0`, no release ever cut |
| Automation / CI | **1** | No `.github/workflows`; 36 tests exist and nothing runs them |
| Docs-with-code rule (`CLAUDE.md`) | **3** | `docs/` 13/17 code PRs; `README.md` 0/14 since PR #5 and now factually wrong |
| Repo hygiene (binaries, LFS, ignores) | **4** | 78 bytes of binary added in the whole range; no-LFS call correct and documented; `Snackdown.slnx` tracked and permanently dirty |
| Convergence vs thrash | **4** | `PredictedPlayer.cs` 11 touches, +713/−22 — pure accretion, zero rewrites; roadmap churn is checkbox-ticking |
| Justification of complexity | **5** | Every large abstraction carries a written trigger; one was designed and rejected on cost |

## Findings

### F-A10-1 — The documented release model has never been executed: zero tags, `main` 55 commits behind at the Phase 0 scaffold

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `git tag -l` → empty. `main` = `8074fe6` "Environment: disable the legacy Input Manager"
  (2026-07-26). `git rev-list --count main..dev` = 55, `dev..main` = 0.
  `ProjectSettings/ProjectSettings.asset:147` → `bundleVersion: 0.1.0`.
  `docs/04-workflow.md:10` ("`main` … Every commit is a version someone could download and play"),
  `:64-70` (SemVer, `v0.1.0` per phase, tag + GitHub Release + `bundleVersion` in sync),
  `docs/03-roadmap.md:105-106` ("portfolio-ready = Phases 0–3 complete"), `84d91de` marks Phases 1–3
  verified. Local `origin/HEAD → origin/main`.
- **What it is**: `docs/04-workflow.md` defines a release model — `dev` → `main` when a phase lands,
  an annotated tag per phase, a GitHub Release, `bundleVersion` kept in sync. Three phases have landed
  on `dev` and none of the four steps has happened once. `main` contains the empty project scaffold
  plus documentation; it contains no netcode, no connection layer and no gameplay.
- **Why it matters**: This is a portfolio project whose stated success condition is being legible to a
  reviewer. The clone's `origin/HEAD` resolves to `main`, which is what GitHub serves as the default
  landing view — a reviewer following a link would see a scaffold and a README that describes work
  they cannot see in the tree. It is also the one section of the project's own documented process with
  a 0% execution rate, which is exactly the kind of gap an interviewer probes. *(Confidence caveat: the
  local `origin/HEAD` symref is set at clone time and could be stale relative to the GitHub setting;
  the tag count, the `main` position and the `bundleVersion` are certain regardless.)*
- **Recommendation**: Merge `dev` → `main`, tag `v1.0.0` per the project's own scheme
  (`docs/04-workflow.md:67`), bump `bundleVersion` in the same commit, and confirm GitHub's default
  branch. Do this *after* F-A10-2, so the README the reviewer lands on is true.
- **Effort**: S

### F-A10-2 — `README.md` went 14 PRs without an update and now documents a deleted scene; the same-commit docs rule was not applied to it

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `README.md` touched in exactly 2 of 55 commits — `965d0f8` (2026-07-25) and `2bcd7b5`
  (2026-08-06, inside PR #5). Zero touches across PRs #6–#19. `README.md:48` → "Open
  `Assets/_Project/Scenes/NetTest.unity` and press Play"; that scene was deleted in `97e7e3f`
  (2026-08-08 14:18), which did not touch `README.md`. `README.md:46` "Phase 1 ships a bare test arena,
  not a game yet"; `README.md:78-80` "Phase 1 (netcode core) is in … pending validation" vs
  `docs/03-roadmap.md:9-11` marking Phases 1–3 ✅ (`84d91de`). `CLAUDE.md` §Documentation: "`README.md`
  and the affected `docs/` file are updated **in the same commit** as the code."
- **What it is**: The rule is binding in this repo and it was followed for `docs/` — 13 of the 17
  code-bearing PRs also touched a `docs/` file, and `docs/03-roadmap.md` was updated in-commit 12 times.
  It was not followed for `README.md` a single time after PR #5. Two commits were the natural carriers
  and skipped it: `8a44c2e` (deleted `NetTestBootstrap.cs`) and `97e7e3f` (deleted `NetTest.unity`).
  `docs/03-roadmap.md:35` has the same scar — "Host session in `NetTest.unity`" — and its summary table
  at `:13` still lists "tests, assembly split" under Phase 5 while its own Phase 5 section (`:88-92`)
  correctly marks both done early.
- **Why it matters**: The README is the first and often only document a reviewer reads, and its "Running
  it" section is now a set of instructions that cannot be followed. The failure mode is worse than
  absent docs because the reader has no way to know which parts are stale. It also undercuts the
  project's own claim to process discipline, since the rule is written down in `CLAUDE.md`.
- **Recommendation**: One commit that rewrites "Running it" against `Bootstrap.unity`, rewrites
  "Status" against the roadmap, fixes `docs/03-roadmap.md:13` and `:35`. Then, so it does not recur,
  add "README still true?" as a line item to `.github/PULL_REQUEST_TEMPLATE.md` next to the existing
  three checkboxes.
- **Effort**: S

### F-A10-3 — The PR gate is ceremonial: median 16 min to merge, and a join-breaking regression survived six consecutive PRs

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: Time from a branch's last commit to its merge, all 19 PRs (minutes): 0, 1, 2, 2, 2, 3,
  13, 14, 14, 16, 17, 17, 18, 21, 40, 48, 140, 1349, 2365 — **median 16 min**, 14 of 19 under 21 min.
  PRs #16 and #17 both merged at 21:48 and #18 at 21:49 (`13fd57b`, `2f29060`, `51b95b8`). 14 of 19
  branches contain exactly one commit. The regression: `c9cd571` body — *"Joining always failed with
  'Unexpected exception processing network metadata' … I broke that in the approval commit and never saw
  it, because the last real two-peer join was the commit before. Since then approval was 'verified' by
  invoking its callback through reflection — which skips the handshake, which is where the bug was. A
  test that exercised the part that worked."* Introduced `32bb167`, merged to `dev` in `b31d485`
  (08-06 21:38); fixed in `c9cd571`, merged in `3cf25f6` (08-08 20:36) — **~47 h**, during which PRs
  **#9, #10, #11, #12, #13, #14** were merged onto a `dev` on which a client could not join.
  `docs/04-workflow.md:11` — "`dev` … always compiles, always runs". `CLAUDE.md` — "Review is the point
  of the flow; a PR Claude both writes and merges is a commit with extra steps."
- **What it is**: The PR structure is present and correctly shaped, but no review interval exists and
  the verification checkbox in `.github/PULL_REQUEST_TEMPLATE.md` ("Tested with more than one peer (if
  it touches netcode)") was satisfied by a reflection-driven unit test that bypassed the handshake — on
  six netcode-touching PRs in a row.
- **Why it matters**: On a solo project a fast merge is not itself a defect, but here it means the gate
  provides zero defect-catching, and the one thing that could have caught this — an actual two-peer
  join — is the exact step the checklist asks for and the exact step that was skipped. `CLAUDE.md`'s
  pre-commit checklist is entirely self-attested; nothing in the repo verifies any of its five items.
- **Recommendation**: Do not lengthen the PR cycle. Instead make one checklist item mechanical: a
  smoke check that a client actually completes approval + connect against a host, run before merge. The
  cheapest honest version is a PlayMode/`NetcodeIntegrationTest` that starts a host and a client through
  the real `NetworkManager` handshake rather than invoking the approval callback directly. Pair with
  F-A10-4.
- **Effort**: M

### F-A10-4 — No CI: 36 tests exist that nothing runs automatically

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `git ls-files .github` returns exactly one path —
  `.github/PULL_REQUEST_TEMPLATE.md`. No `.github/workflows/` directory exists. `e99a6fb` body: *"36 unit
  tests … They run in 0.46 seconds with no scene, no NetworkManager and no Play mode."*
- **What it is**: The test suite was deliberately built to be fast and headless-friendly (EditMode only,
  no scene, no `NetworkManager`), which is precisely the shape that makes CI trivial — and no CI exists.
  Nothing checks compilation, warnings, or tests on push or on PR.
- **Why it matters**: It is the missing half of F-A10-3 — the reason a broken `dev` could persist for 47
  hours is that the only verification gate was the author's memory of what to re-test. It is also
  cheap-to-fix credibility: a green check on every PR is visible evidence of the engineering discipline
  the rest of the repo argues for in prose.
- **Recommendation**: One `.github/workflows/ci.yml` using `game-ci/unity-test-runner` with the EditMode
  test mode, gated on PRs into `dev`. The suite's 0.46 s runtime means the job is dominated by editor
  activation, not by tests. A Unity licence secret is required — that is the only real cost.
- **Effort**: S

### F-A10-5 — `Snackdown.slnx` is a Unity-regenerated artifact tracked in git while every sibling generated file is ignored

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `git ls-files | grep -E '\.(csproj|sln|slnx)$'` → `Snackdown.slnx` only.
  `.gitignore` lines for `*.csproj` and `*.sln` exist; `.slnx` is not covered (it is a different
  extension). `git status --porcelain` → ` M Snackdown.slnx`; the working diff is 4 insertions / 4
  deletions and is purely a reordering of the eight `<Project Path=…/>` entries. History: added in
  `965d0f8`, rewritten in `154dbec` (+8/−1) when the Connection assembly landed.
- **What it is**: The eight `*.csproj` files and `Assembly-CSharp.csproj` sit untracked in the working
  tree because `.gitignore` covers them; the solution file that indexes them is tracked because the new
  `.slnx` extension slipped past the same rule. Unity rewrites it in a non-deterministic order every
  time it regenerates project files, so the repo is dirty on a clean checkout the moment the editor
  opens.
- **Why it matters**: A permanently dirty working tree trains the author to ignore `git status`, which
  is how genuinely unintended changes get committed. It also means every asmdef change produces a
  meaningless diff to review. It is the only untidy thing about this repository's state.
- **Recommendation**: Add `*.slnx` to `.gitignore` next to `*.sln` and `git rm --cached Snackdown.slnx`
  in one commit. (Mutating command — needs Luca; A10 did not run it.)
- **Effort**: S

### F-A10-6 — PR #15 is named for one feature and contains five, including a critical bugfix

- **Severity**: Minor
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `3cf25f6` "Merge pull request #15 from LucaValentini25/feature/fruit-spawner" —
  33 files changed, +1673/−46, 6 commits: `0607fdc` (fruit spawner), `3b78866` (menu reachability),
  `c9cd571` (the join-flow fix + loading screen, 14 files), `6f4c863` (arena spawn points),
  `4751d02` + `1ef757b` (loading-screen phase reconciliation). Branch lifetime 14:42 → 20:36.
  Compare the median PR in this range: 5 `.cs` files. `docs/04-workflow.md:12` — feature branches are
  "short-lived", one feature each.
- **What it is**: The only branch in the range that accumulated unrelated work. Notably it is the branch
  that carried the fix for the 47-hour join regression (F-A10-3), so the fix is buried in a PR whose
  title says "fruit spawner" — it is not findable by anyone scanning the merge log.
- **Why it matters**: For a history that is explicitly part of the deliverable, a merge title that
  conceals the most interesting bugfix in the range is a lost opportunity, and it is the one place where
  the otherwise excellent log misleads a scanner. The individual commit messages inside are still
  precise, so the damage is limited to the first-parent view.
- **Recommendation**: None retroactively — rewriting merged history is worse than the problem. Going
  forward: when a branch grows a fix unrelated to its name, split it out to `bugfix/*` so the merge log
  keeps naming what happened.
- **Effort**: S

### F-A10-7 — 17 merged branches were never deleted on `origin`, contradicting the documented flow

- **Severity**: Nit
- **Type**: Process
- **Confidence**: High
- **Evidence**: `git branch -a` lists `remotes/origin/` copies of `bugfix/manifest-cleanup`,
  `docs/adr-netcode-decoupling`, `docs/reporting-rule`, `feature/assembly-definitions`,
  `feature/character-select`, `feature/connection-abstraction`, `feature/connection-approval`,
  `feature/fruit-spawner`, `feature/head-bounce`, `feature/life-timer`, `feature/match-state`,
  `feature/menu-and-lobby`, `feature/netcode-metrics`, `feature/peer-collision`,
  `feature/relay-sessions`, `feature/session-roster`, `feature/win-conditions` — all fully merged into
  `dev`. `docs/04-workflow.md:60` — "Delete the branch after merging."
- **What it is**: Step 5 of the project's own PR procedure, skipped 17 times out of 19 (only
  `feature/netcode-core` and `bugfix/repo-truth` were cleaned up). `feature/netcode-metrics` is a
  particularly clear leftover: it points at `2bcd7b5`, an interior commit of PR #5's branch, and never
  had a PR of its own.
- **Why it matters**: Cosmetic, but it is on the branch list a reviewer sees first, and it makes the
  branch page read as abandoned work rather than as completed work.
- **Recommendation**: Delete the 17 merged remote branches. (Mutating — needs Luca.)
- **Effort**: S

### F-A10-8 — One commit sits outside the standard the other 35 hold to

- **Severity**: Nit
- **Type**: Process
- **Confidence**: High
- **Evidence**: `c913be7` — subject "Claude and git ignore udpate": typo (`udpate`), noun phrase rather
  than imperative mood, and zero body lines. `CLAUDE.md` §Git and `docs/04-workflow.md:38` both require
  imperative mood and a body explaining why. Two other bodyless commits, `84d91de` and `ce83cd3`, are
  three-line roadmap checkbox flips where the subject genuinely is the whole change — those are fine.
- **What it is**: One of 36 non-merge commits, and the only one that would look careless to a reviewer
  scanning subjects. It is also the oldest, from before the process was tightened in `114aec2`/`b514182`
  (PR #2, "Correct the git rules Claude operates on").
- **Why it matters**: On a log this consistent, the single outlier is the one a reviewer notices. Low
  consequence, but this is a repository where the log is the deliverable.
- **Recommendation**: Leave it. Rewriting a merged commit to fix a typo costs more history integrity
  than it buys. Worth knowing about only in case it comes up.
- **Effort**: S

### F-A10-9 — The `unityyamlmerge` risk `CLAUDE.md` warns about has never actually been exercised

- **Severity**: Nit
- **Type**: Process
- **Confidence**: High
- **Evidence**: `.gitattributes` routes 18 Unity types to `merge=unityyamlmerge`.
  `git config --get merge.unityyamlmerge.driver` on this machine returns
  `"D:/Unity editors/6000.3.14f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"`
  — configured. `git grep` for `<<<<<<<`/`>>>>>>>`/`=======` across `*.unity *.prefab *.asset *.cs
  *.meta *.uxml *.uss` returns nothing. `git show --cc` over all 19 merge commits produces an **empty
  combined diff on every one** — no merge in the range required any conflict resolution, because the
  workflow is strictly serial (one branch open at a time, rebuilt off the current `dev` tip; branch
  bases confirm this: every PR's merge-base is the previous PR's merge commit).
- **What it is**: There is no merge damage to find. The corollary is that the driver has never been
  invoked in anger, so its configuration is verified to be *present* but not verified to *work*. The
  three near-simultaneous merges (#16/#17/#18) came closest — they are the only criss-cross in the
  range — and even those touched disjoint files.
- **Why it matters**: Nothing today. It matters the first time two scene-touching branches are open at
  once, or the first time this is cloned to a second machine, where the driver will silently not exist.
  `docs/04-workflow.md:72-96` already documents this correctly and completely.
- **Recommendation**: None. The documentation is right and the config is in place. Noted so the clean
  result is not mistaken for "the driver was tested".
- **Effort**: S

### F-A10-10 — A plan reversal inside 48 minutes, recorded honestly

- **Severity**: Nit
- **Type**: Process
- **Confidence**: Medium
- **Evidence**: `fa2a289` (2026-08-06 17:14) proposes decoupling the netcode layer; `d7019ed` (17:30)
  rejects it and states *"Assembly definitions stay in Phase 5 but change justification: compile times
  and test isolation, not proof of a decoupling nobody needs"*; `e99a6fb` (18:02) opens with *"Assembly
  definitions were parked in Phase 5 as a polish item. That was wrong."* Three positions on the same
  question in 48 minutes.
- **What it is**: The one place in the range where the plan moved under the work rather than the work
  following the plan. Each move is documented with its reason, and the total cost was 234 lines of
  markdown and zero code churn before the final direction was taken.
- **Why it matters**: Read charitably — and the evidence supports this reading — the analysis was cheap,
  a bad idea was killed in 16 minutes and a deferred good idea was pulled forward 30 minutes later once
  the compiler proved its value. Read uncharitably, an ADR that is rejected 16 minutes after being
  written may have been documenting a decision already made. I cannot distinguish these from the history
  alone; see Open questions.
- **Recommendation**: None. If anything, `d7019ed` should be cited in an interview rather than hidden.
- **Effort**: S

## Quantified Estimates

| Metric | Value | Formula / inputs | Tag |
|---|---:|---|---|
| Commits `main..dev` | 55 | `git rev-list --count main..dev` | MEASURED |
| Merge / non-merge split | 19 / 36 | `--first-parent` count vs `--no-merges` count | MEASURED |
| Direct commits to `dev` (outside a PR) | **0** | `--first-parent main..dev` yields 19 entries, all merge commits | MEASURED |
| Distinct commit days | 6 of 15 calendar days | 07-25 (1), 07-30 (1), 07-31 (3), 08-01 (6), 08-06 (15), 08-08 (29) | MEASURED |
| Commits on the final day | 29 (53%) | 29 / 55, spanning 13:11→21:59 ≈ 18 min/commit | MEASURED |
| Commit-body prose | 55,847 bytes | `git log --no-merges --format='%b' \| wc -c`; ÷36 = 1,551 B ≈ 250 words per commit | MEASURED |
| Commits with a body ≥7 lines | 33 of 36 (92%) | per-commit `%b` non-blank line count | MEASURED |
| Longest subject | 71 chars | max over 36 subjects; `docs/04-workflow.md:38` budget is ~72 | MEASURED |
| Reverts | 0 | `git log -i --grep=revert main..dev` → empty | MEASURED |
| Merges needing conflict resolution | 0 of 19 | `git show --cc` empty combined diff on all 19 | MEASURED |
| Conflict markers in tracked files | 0 | `git grep -E '^(<<<<<<<\|>>>>>>>\|=======$)'` over `*.unity *.prefab *.asset *.cs *.meta *.uxml *.uss` | MEASURED |
| Max branch lifetime | 5 days (PR #1, PR #5) | merge-base timestamp → merge timestamp, all 19 | MEASURED |
| Median last-commit → merge | **16 min** | 19 values: 0,1,2,2,2,3,13,14,14,**16**,17,17,18,21,40,48,140,1349,2365 | MEASURED |
| Single-commit PRs | 14 of 19 (74%) | `git rev-list --count base..^2` per merge | MEASURED |
| `dev` broken-join window | ~47 h / 6 PRs | merge `b31d485` 08-06 21:38 → merge `3cf25f6` 08-08 20:36; PRs #9–#14 merged inside it | MEASURED |
| Code-bearing PRs also touching `docs/` | 13 of 17 (76%) | per-merge `git diff --name-only`; misses are PRs #4, #7, #8, #9 | MEASURED |
| PRs touching `README.md` | 2 of 19 (11%) | `965d0f8` (PR #1) and `2bcd7b5` (PR #5); 0 of the last 14 | MEASURED |
| `PredictedPlayer.cs` lifetime churn | +713 / −22 (32:1) | 11 touches, each net-positive: 339→435→454→489→548→556→557→585→609→650→**691** | MEASURED |
| `docs/03-roadmap.md` churn | +72 / −38 over 13 touches | 1 restructure (`965d0f8`, +28/−15); other 12 average +3.7/−1.9 | MEASURED |
| Binary bytes added in `main..dev` | **78 bytes** | one file, `Assets/_Project/Art/WhiteSquare.png` (`965d0f8`) | MEASURED |
| Tracked tree size | 7.93 MB / 991 files | `git ls-tree -r -l HEAD` summed | MEASURED |
| Third-party art tracked as plain binaries | 3.42 MB | Pixel Adventure 1 = 1.94 MB (636 files) + DEVNIK 2D = 1.48 MB (5 files) | MEASURED |
| TextMesh Pro (largest single block) | 3.84 MB / 81 files | includes `LiberationSans SDF.asset` at 2.26 MB, the largest tracked blob | MEASURED |
| Whole `.git` directory | 6.9 MB | `du -sh .git` | MEASURED |
| Commits that modified a third-party art blob | 1, on `main` | all of it added in `c9164d4` (Phase 0), never touched since — so it costs 3.42 MB of pack **once**, not per commit | MEASURED |
| Tags | 0 | `git tag -l` → empty | MEASURED |
| CI workflows | 0 | `git ls-files .github` → `PULL_REQUEST_TEMPLATE.md` only | MEASURED |
| Stale merged remote branches | 17 | `git branch -a`, cross-checked against `main..dev` merge list | MEASURED |

### On LFS, specifically

The brief asked whether LFS is configured and whether the 4.8 MB of third-party art is tracked as plain
binaries. **No LFS** (`grep -c filter=lfs .gitattributes` → 0; `git lfs ls-files` → empty), and yes,
the art is plain binaries. **This is the correct call and it is documented rather than accidental** —
`.gitattributes` carries an explicit rationale block ("Not tracked with Git LFS on purpose: this
project's art is pixel art measured in kilobytes, and LFS would add bandwidth quotas and a smudge/clean
step for no benefit. Revisit if audio or large textures ever land here"), matched by
`docs/04-workflow.md:100-101`. The numbers back it: the entire `.git` directory is 6.9 MB, every art
blob entered in a single Phase 0 commit and has never been modified, and only 78 bytes of binary were
added across all 55 audited commits. LFS pays off when large binaries *churn*; here they do not churn at
all. No finding.

## What is genuinely good here

**The commit bodies are the best artifact in the repository, and they survive close reading.** I read
six in full rather than sampling subjects, and none of them is an attractive restatement of the diff.
`154dbec` (41 body lines) documents two architectural decisions *and* three bugs found by running the
code, including a subtle one where `IPAddress.TryParse` accepts `"10.0.01"` as abbreviated
`10.0.0.1` while `NetworkEndpoint.TryParse` rejects it, and the DNS fallback then silently "repaired"
the typo into a connection the player never asked for. `fa2a289` records an NGO 2.11 constraint
*established by experiment* — an `[Rpc]` whose parameter closes over its declaring class's generic
parameter crashes the IL post-processor with a `NullReferenceException` out of Mono.Cecil instead of
emitting a diagnostic — and preserves it even after the surrounding decision was reversed. That is not
commit-message theatre; a reviewer cannot fake that content.

**`d7019ed` is the strongest single piece of evidence against the over-engineering hypothesis in this
repository.** An abstraction was proposed (`fa2a289`, 187 lines of ADR), costed, and then explicitly not
built: *"if the reusable part has to keep shrinking to keep the label … then the label is not buying
anything. It was not. The reuse was hypothetical … Doing the refactor would have touched every file
measured in docs/05, putting working and verified code at risk so that one sentence of documentation
could be true. Deleting the sentence costs nothing and is equally honest."* The ADR was marked
**Rejected rather than deleted**, specifically to keep the NGO finding. Total cost of the episode: 234
lines of markdown, 16 minutes, and zero lines of production code churn. Engineers who over-engineer do
not write this commit.

**The two largest abstractions each carry a written trigger, and the trigger is evidence rather than
taste.** `e99a6fb` (43 files, +833/−43) did not split into assemblies because layering is nice — it
split and the *compiler refused*, exposing a real `Netcode ↔ Gameplay` cycle that had been invisible
inside `Assembly-CSharp` and in flat contradiction of a rule `docs/01` had asserted since day one. The
body states the general principle it derived ("an assembly is created when a system is, or adding one
later stops being a definition and becomes a migration") and, unusually, states what it deliberately
left uncovered and why ("Collision is deliberately uncovered: `MoveAndCollide` casts against scene
geometry, which is the part that is not pure. Pretending otherwise with a mocked cast would buy coverage
and no confidence"). `154dbec` introduced `IConnectionProvider` with exactly one implementation — a
textbook over-engineering signal — but the body names the second implementation as the motive, and Relay
arrived four PRs later in `0a6e7bf`. The seam was declared and filled. Neither has a separate ADR; the
commit body *is* the ADR, and it is longer and more specific than most ADRs.

**`PredictedPlayer.cs` is accreting, not thrashing.** Its 11 touches are +713/−22 — a 32:1 add:delete
ratio, and **every single touch is net-positive** (339 → 435 → 454 → 489 → 548 → 556 → 557 → 585 → 609
→ 650 → 691). Not one commit rewrites work a previous commit did. Each touch adds a distinct capability
(hardening, metrics, run recording, teleport classification, assembly move, spawn placement, stomp,
peer collision, spectator handoff) on top of a core that has held since `965d0f8`. That is the churn
signature of a design that was right the first time. The honest counter-observation is that the file
only ever grows and no commit in 55 ever split it — the risk there is size, not instability, and it
belongs to A2's domain rather than mine.

**`docs/03-roadmap.md`'s 13 touches are healthy plan-tracking, not a moving plan.** Twelve of the 13 are
≤8 added lines and each rides along with the feature commit that completed the item it ticks; only
`965d0f8` is a restructure. Better, when two Phase-5 items landed early the roadmap was annotated rather
than quietly rewritten — `docs/03-roadmap.md:88-92` keeps them struck through with "**done early**" and
records *why* the original deferral was a mistake. A plan that is edited to record what actually
happened, including its own errors, is the opposite of a plan moving under the work.

**The two deleted files were planned scaffolding removed on schedule, not abandonment.**
`NetTestBootstrap.cs` and `NetTest.unity` were both created in `965d0f8` as Phase 1 harness. `8a44c2e`
deletes the bootstrap with the reason stated — *"deleted rather than left disabled. Its own remarks said
Phase 2 would replace it, Phase 2 has, and a second launcher that nobody instantiates is exactly the
leftover the checklist forbids"* — and `97e7e3f` deletes the scene after carving it into three
(*"what remained had no objects in it"*). The repo also carries **zero** `TODO`/`HACK`/`FIXME` markers.
The only failure in this whole sequence is that the README was not updated alongside (F-A10-2).

**The structural hygiene is essentially perfect.** Zero direct commits to `dev`, 19 for 19 on PR
discipline, branch names matching `docs/04-workflow.md:27` exactly (`feature/*`, `bugfix/*`, `docs/*`,
kebab-case, no ticket numbers), zero evil merges, zero conflicts, zero reverts, zero rebase damage, max
branch lifetime 5 days against a documented budget of "days, not weeks," and a `.gitattributes` that
routes 18 Unity types to the YAML merge driver with a comment explaining why plain text merging is
unsafe. `.github/PULL_REQUEST_TEMPLATE.md` is unusually good for a solo project — its "How it was
verified" section explicitly demands *"List what you did NOT verify too."*

**Honest self-reporting under pressure.** `c9cd571` is the commit most people would have written
vaguely, and instead it says: *"I broke that in the approval commit and never saw it … approval was
'verified' by invoking its callback through reflection — which skips the handshake, which is where the
bug was. A test that exercised the part that worked."* An author who documents their own false
verification in the permanent record is giving a reviewer a reason to trust everything else in the log.

## Open questions for the team

1. **What is GitHub's actual default branch?** The local `origin/HEAD` symref resolves to `origin/main`,
   which would mean a reviewer's first view is the Phase 0 scaffold. That symref is set at clone time and
   may be stale. If the GitHub default is already `dev`, F-A10-1 loses its sharpest edge (though the zero
   tags and the stranded `main` remain).
2. **Was ADR 0002 written before the decision or after it?** `fa2a289` → `d7019ed` is 16 minutes.
   Written-then-decided is exemplary; decided-then-documented is still useful but a different claim to
   make in an interview. Only Luca knows which it was.
3. **Where is ADR 0001?** Numbering starts at 0002 with no 0001 in the tree and no commit that ever
   added or deleted one. Deliberate (starting at 2 to leave room), or a lost file?
4. **Were the PR descriptions actually filled in against the template?** `gh` is not installed on this
   machine and PR bodies are not in the git object store, so I could not verify a single one. Given how
   good the commit bodies are I would expect them to be filled in — but that is an expectation, not a
   finding, and for a portfolio the PR bodies are as visible as the commits.
5. **Is the 47-hour broken-`dev` window a one-off or the pattern?** It is the only one I can prove,
   because it is the only one the author documented. Absence of other evidence is not evidence of
   absence when the only detector is the author's own memory (F-A10-3/F-A10-4).
6. **Is `main` intended to be cut at `v1.0.0`?** `docs/04-workflow.md:67` says "1.0.0 is Phase 3 complete
   and playable over Relay," and `docs/03-roadmap.md` now marks Phases 1–3 done. If that is accurate, the
   release is overdue by the project's own rule; if Phase 3 is "done on paper, not on a second machine,"
   that is worth saying in the roadmap before tagging.
