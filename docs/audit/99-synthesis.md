# 99 — Synthesis

Lead auditor's consolidation of ten parallel domain audits of `Snackdown`, branch `dev`,
commit `10a2a13`, 2026-08-08. Working tree dirty in one generated file (`Snackdown.slnx`), audited as-is.

> ## Remediation status
>
> This audit is a snapshot of commit `10a2a13` and is **not** updated as findings are fixed — its
> value is being a dated, honest reading of one state of the project. What has been done about it is
> recorded here, and only here.
>
> | Date | Decision or fix | Closes |
> |---|---|---|
> | 2026-08-09 | Both hot RPCs declare an `InvokePermission`; input is range-checked on ingest via `InputCommand.Sanitized`; `SnapshotFrame` bounds its array length before allocating. `bugfix/rpc-authorization` | T1 — F-A3-1, F-A3-2, F-A7-1, F-A7-2, F-A7-3 |
> | 2026-08-09 | Docs realigned with the code: README rewritten against the real entry scene and the real phase state, `docs/01` `Core`/purity/ambient-lookups corrected, `docs/02` peer-collision behaviour and wire table corrected, roadmap claims made checkable, `docs/04` release model corrected to match what is actually done, ADR renumbered to 0001 and amended. `docs/repo-truth` | T2 — F-A1-1, F-A1-2, F-A1-6, F-A1-7, F-A1-9, F-A2-10, F-A3-3 (documented), F-A3-9, F-A3-14, F-A4-2, F-A8-6, F-A8-7, F-A8-8, F-A8-15, F-A10-2 |
> | 2026-08-09 | **Decisions taken, recorded so the reasoning is not lost:** PC is the target and WebGL is a documented future phase, not a current claim (T4). There is one build and it is the demo, so the debug overlay stays and its GC cost is treated as the defect instead (T5). Peer contact keeps its current behaviour and the choice moves to Phase 4 rather than invalidating the Phase 1 measurements now (F-A3-3). No release is cut until Phase 5, and `docs/04` says so instead of promising otherwise (F-A10-1, F-A9-3). No CI for now; the verification gap is closed in the PR template and `CLAUDE.md` instead (F-A9-1, F-A8-3). | — |
>
> Still open at the time of writing: the integration test (T3 headline), the HUD and character picker,
> late join, and the session-lifecycle fixes. See [03 — Roadmap](../03-roadmap.md).

**Inputs:** `00-recon.md` + `01`–`10`. **Raw finding count:** 122 across ten reports
(A1 11 · A2 13 · A3 14 · A4 8 · A5 9 · A6 14 · A7 13 · A8 17 · A9 13 · A10 10).
**After deduplication:** 41 distinct root findings in **6 systemic themes**.

---

## 1. Corrections to the record

Three things established during recon or in individual reports turned out to be wrong. They are
corrected here rather than carried forward silently.

| Claim | Source | Correction | Established by |
|---|---|---|---|
| "38 first-party runtime `.cs` files" | `00-recon.md` | **49 files**, 6,098 LOC. `Gameplay` is 17 files, not 14. The LOC figure was right. | A2 and A8 independently, both from full reads |
| "68 `[Test]`/`[TestCase]` attributes" | `09-infra-devops.md` | **77 `[Test]`, 0 `[TestCase]`, 0 `[UnityTest]`** | Verified by the lead auditor directly: `grep -c "\[Test\]"` per file = 6+10+10+8+15+10+11+7 |
| "`docs/01-architecture.md` claims service locators that do not exist" | `00-recon.md` | Service locators **do** exist — 9 ambient statics acting as an undeclared locator. They are not in `Core/`, which is what the doc gets wrong. | A1 (found 5), A2 (found 9, with call-site counts) |

---

## 2. Contradictions between agents, resolved

Per the mandate these are decided, not averaged.

### 2.1 The test count — 36 vs 68 vs 77

**Resolved: 77.** Verified directly by the lead auditor. A8 additionally explains why all three
numbers appear: the roadmap's "36" was exactly correct at commit `e99a6fb` (15+10+11 across the three
files then covered) and was never updated; A9's 68 counted a different attribute set. A1 was right
that the doc's claim was accurate *when written*. Only `docs/03-roadmap.md:90` is stale.

### 2.2 ADR 0002 — was it contradicted by the code?

**Both agents were right about different sections, and my first read of this was too narrow.**

- A2 and A10: the ADR's **Decision** — *"None of the options were taken. The requirement was withdrawn
  instead"* — stands, and the code matches it exactly. `Netcode/` still imports `Snackdown.Simulation`
  as an accepted dependency, and `docs/01` had the false "reusable core" sentence deleted rather than
  the code refactored. **Correct.**
- A1: the ADR's **Context** section (`adr/0002:23-25`) states that *"the assembly split … cannot be
  done — an assembly definition for `Netcode` would need a reference to `Gameplay`."* Commit `e99a6fb`,
  the next feature commit, **did** the split via a fourth route the ADR never lists (extract
  `Simulation` as a shared leaf + a non-generic `IPredictedPeer` at the loop boundary). The Context
  premise is now false and was never annotated. **Also correct.**

**Verdict:** F-A1-6 stands, re-scoped. The ADR's decision is sound and is the repo's best artefact;
its Context section carries a superseded factual claim. The fix is an appended "Superseded in
practice" block, not a rewrite. Note the ADR's own closing line already anticipated the outcome:
*"Assembly definitions stay in Phase 5, justified by compile times and test isolation rather than by
proving a decoupling that is no longer a goal"* — `e99a6fb` executed exactly that justification, early.

### 2.3 Is `PredictedPlayer.cs` a god object?

**Resolved: both, on different axes — and it does not matter, because all three agents recommend the
same action.**

- A2 and A10 measured **churn and cohesion**: 713 lines added against 22 deleted across 11 commits
  (32:1), every touch net-positive, no commit ever rewriting a previous one, and every method
  converging on one `_state` field. On those axes it is **cohesive and converging, not thrashing.**
- A3 and A8 measured **responsibility count and testability**: seven separable roles in one
  `NetworkBehaviour`, and `Reconcile` — the project's headline mechanism, 90 lines and 9 branches —
  is unreachable from any EditMode test *because* it lives inside a `MonoBehaviour`. On those axes it
  is **a god object, and that is the direct cause of the coverage hole.**

Both are true. A2, A3 and A8 all independently recommend **not** splitting the component and instead
extracting two engine-free pieces (the reconcile algorithm and the server input queue). Recorded as
one finding with both framings; the action is unambiguous.

### 2.4 `NetDebugOverlay` severity — Minor or Major?

**Resolved: two separate findings, not one.**

- **As a cheat surface: Minor.** A7 is right and checked it properly — F1 flips a local `static bool`
  the server never reads, F2/F3 are local rendering toggles, F4 writes a local CSV. The panel prints
  peer ids and queue depths but no positions or life values. **No advantage, no wallhack.**
- **As release hygiene and host GC cost: Major.** A5, A6 and A9 are right. It is instanced in
  `Arena01.unity`, enabled by default, in a runtime assembly with no define constraint, and its IMGUI
  pass is ~97% of all managed allocation on the host (320–600 KB/s against ~12 KB/s from the entire
  simulation path).

Carried forward at **Major**, with A7's finding recorded explicitly so nobody later reports it as a
cheat vector.

### 2.5 Snapshot datagram size

A3 estimated ~198 bytes on the wire (payload + ~22 B framing). A4 counted the framing layer by layer
from NGO and UTP package source — batch header 16 B, RPC metadata 8 B, message header 3 B, UTP
fragmentation 2 B, connection layer 9 B, IP/UDP 28 B, Relay header 38 B — reaching **242 B direct /
280 B relayed**. **A4's figure is used.** Both agree the payload is 176 B, which is the number that
matters for F-A4-2.

### 2.6 Reconciliation replay cost

A5 reports 0.055–0.34 ms; A6 reports 0.03–0.10 ms. Not a contradiction — different declared device
assumptions (A5: `C_cast` 5 µs generic; A6: 3 µs PC / 10 µs phone). Both conclude the same thing and
both are far inside budget. **No resolution needed; the range 0.03–0.35 ms is reported.**

---

## 3. Root-cause map — six systemic themes

122 raw findings roll up into six. Every finding in the appendix belongs to exactly one.

### T1 — Two missing RPC attributes falsify the project's central claim
**Severity: Critical · 8 findings roll up**

Neither hot RPC declares an `InvokePermission`, so both default to `RpcInvokePermission.Everyone`,
which NGO 2.11 enforces receive-side. A3 and A7 found this independently, both verifying against NGO
package source rather than inferring it. Consequences: any client can drive any other player's
character; any client can broadcast forged authoritative snapshots through the server's own proxy
path. A third, adjacent gap — `InputCommand.MoveX` is an `sbyte` the server never clamps — yields a
127× speed hack.

What makes these Critical here is not competitive harm in a friendly 4-player match. It is that
`docs/01-architecture.md:101` states *"a `ServerRpc` assumes the caller is hostile until checked"* and
`docs/02-netcode.md:15-16` states *"the worst a cheater can do is move legally."* Neither is true as
built, and in this project the docs are the deliverable.

**The compensating fact, and it is a large one:** the gameplay half of the trust boundary is genuinely
well built. No `NetworkTransform` anywhere (verified three times independently, by A2, A4 and A7, each
resolving component GUIDs in the prefab YAML). Head-bounce, fruit pickup, life, death and win are all
resolved server-side against server-owned positions. A modified client cannot claim a stun, a pickup
or a win, and cannot shove another player. All three fixes are one line each. This is
under-engineering at three points, not a design that needs redoing.

*Rolls up:* F-A3-1, F-A3-2, F-A7-1, F-A7-2, F-A7-3, F-A7-9, F-A3-10, and the unbounded array read in
`SnapshotFrame.NetworkSerialize` (reported separately by A3, A4, A5, A6 and A7 as an allocation issue;
it is the same line).

### T2 — The docs assert things the code does not do
**Severity: Critical (cumulative) · 14 findings roll up**

The single largest theme by count, and the one that matters most given the portfolio framing.

- `README.md` cannot be followed at all: it instructs opening `NetTest.unity`, deleted in `97e7e3f`,
  and describes a Phase-1 test arena while three phases are done. Untouched for 14 PRs / 39 commits.
- `docs/01-architecture.md` describes a `Core` layer holding service locators and an app state
  machine; `Core/` holds two files and neither. Its folder listing omits 9 shipped types.
- Predicted peer collision is documented in **three** places as resolving against tick-aligned peer
  positions. It resolves against *interpolated render* positions labelled with the current tick —
  ~0.47 s stale at the project's own measured RTT.
- `SnapshotFrame.cs:56` states "~120 byte datagram"; the payload is 176 B. Off by 46%.
- `PlayerMotor`'s `<summary>` says "Pure: same inputs, same output, always"; it issues three
  `Physics2D.BoxCast` calls against the live scene. (The `<remarks>` directly below is accurate.)
- `docs/02`'s wire table lists a fruit RPC that does not exist. Every other row is exact.
- Roadmap: "36 unit tests" (now 77); "distribution verified over 100k rolls" (no such test in-tree);
  "measured at 1 write/s" (correct by configuration, no measurement artefact).

The pattern is specific and worth naming precisely: **the reasoning in the docs is consistently
excellent and the factual assertions have drifted.** Every architectural argument checked out. What
failed is numbers, file paths and status lines — the parts that go stale when code moves and a doc does not.

*Rolls up:* F-A1-1, F-A1-2, F-A1-6, F-A1-7, F-A1-9, F-A2-10, F-A3-3, F-A3-9, F-A3-14, F-A4-2, F-A8-6,
F-A8-7, F-A8-8, F-A8-15, F-A10-2.

### T3 — Nothing verifies anything automatically
**Severity: Blocker · 11 findings roll up**

The 77 EditMode tests are genuinely well-crafted — property-based, boundary-aware, each documenting
the failure it guards. They are also entirely single-peer, single-process and synchronous. No
PlayMode test, no `[UnityTest]`, no `NetcodeIntegrationTest`, and `manifest.json` lacks a `"testables"`
entry so NGO's integration harness is not even reachable. **No test in this repository can fail
because of a networking bug.**

`Reconcile` — 90 lines, 9 branches, the project's headline mechanism — has zero coverage. The two
tests named after replay assert `f(x) == f(x)` on a pure static function called twice in one process.

Nothing runs the suite either: no CI, not in the PR template, not in `CLAUDE.md`'s pre-commit list.
A10 found the cost concretely: a join-breaking regression introduced in `32bb167` survived **six
consecutive merged PRs over ~47 hours**, and the commit that fixed it says so in its own body — the
approval path had been "verified" by invoking its callback through reflection, which skips the
handshake, which is where the bug was.

The same absence covers measurement: no bandwidth was ever counted (though
`com.unity.multiplayer.tools` is installed and the metrics flags are *already enabled* in
`Bootstrap.unity:465-466`), and no profiler capture, frame-time or memory data exists anywhere.

*Rolls up:* F-A8-1, F-A8-2, F-A8-3, F-A8-5, F-A3-6, F-A7-11, F-A9-1, F-A10-3, F-A10-4, F-A4-1, F-A6-14.

### T4 — Two of three stated platforms do not exist
**Severity: Blocker (as claimed) · 9 findings roll up**

Mobile and WebGL are named targets with no supporting evidence: zero `#if` directives in the entire
first-party codebase, empty `scriptingDefineSymbols`, no build profile, no Addressables, no touch
input path, `m_UseWebSockets: 0`, and `WithRelayNetwork()` called without `"wss"`. A WebGL build
cannot connect by either provider. Mobile has no input path — `Keyboard.current` and `Gamepad.current`
are both null on a phone, so the character stands still while its life drains.

Two second-order consequences worth carrying: WebGL forces WebSocket/TCP, which converts both
deliberately-unreliable channels into reliable-ordered with head-of-line blocking — precisely the
failure the design comments say they avoid. And the documented `runInBackground` fix in
`docs/05-validation.md` is a desktop-only setting; on a phone or a browser tab the underlying failure
is fully intact.

**This theme's severity is entirely a function of the claim.** The netcode work is PC-shaped and loses
nothing by saying so. Drop the claim and this collapses to a one-line doc fix; keep it and it is the
largest gap in the project.

*Rolls up:* F-A6-1, F-A6-2, F-A6-3, F-A6-6, F-A6-13, F-A1-10, F-A2-12, F-A4-3, F-A9-12.
Related but not blocked on the answer: F-A4-8 (host uplink at 82% of a 256 kbps mobile budget).

### T5 — The work is done and nobody can see it
**Severity: Major · 10 findings roll up**

Three phases are complete on `dev`. `main` is 55 commits behind at the Phase 0 scaffold, with zero
tags, `bundleVersion` stuck at `0.1.0`, and — per the local `origin/HEAD` — it is what a reviewer
following a link lands on. **No player build has ever been produced**, confirmed four independent ways.
A build made today would ship the network simulator, the debug overlay and a hotkey that turns off
client prediction.

Inside the running game the same shape repeats: there is **no in-match HUD**. Zero `UIDocument` in
`Arena01.unity`; `PlayerLife.Fraction` and `RoundReferee.RoundRemaining` were both written for a bar
and have zero call sites. The pitch is "your life is a countdown timer" and no player can see it.
Character select is marked `[x]` in the roadmap but `MainMenuController` never passes the index, so it
is provably 0 in every session and all four players render identically. And every carefully-written
connection diagnostic — version mismatch, "is the project linked and Relay enabled?" — is routed into
a field the UI deliberately never renders.

*Rolls up:* F-A1-3, F-A1-4, F-A2-5, F-A7-12, F-A9-2, F-A9-3, F-A9-4, F-A9-6, F-A9-9, F-A10-1.

### T6 — Small structural residue
**Severity: Minor · 9 findings roll up**

`Snackdown.Core` is an assembly nothing references, with 2 of its 3 asmdef references unused.
`Snackdown.UI` reads the Input System directly, breaking the one invariant `Snackdown.Input` exists to
enforce. A `PlayerLife` ↔ `MatchDirector` type cycle hides inside the single `Gameplay` assembly —
the same class of cycle the assembly split is celebrated for catching one level up. Nine ambient
statics form an undeclared service locator. `ReconciliationStats` and `RunRecorder` duplicate the same
aggregation and have already drifted (RTT columns in one only). `FruitSpawner.ServerDespawnAll()` has
zero call sites — found independently by A1, A3 and A5 — so fruit survives into the lobby and the next
match. Seven dead public members. `Snackdown.slnx` is generated, tracked, and permanently dirty.

*Rolls up:* F-A2-2, F-A2-3, F-A2-4, F-A2-7, F-A2-8, F-A2-11, F-A8-10, F-A10-5, and the merged
fruit-cleanup finding (F-A1-8 / F-A3-11 / F-A5-2).

---

## 4. Top 10, ranked by severity × confidence × (1/effort)

| # | Finding | Sev | Conf | Effort | Theme | IDs merged |
|---:|---|---|---|---|---|---|
| 1 | `SubmitInputRpc` accepts input for **any** player's character — one attribute missing. Enables puppeteering and a ~20 B/s permanent remote freeze. Falsifies `docs/01:101`. | Critical | High | S | T1 | F-A3-1, F-A7-1 |
| 2 | `SnapshotRpc` is client-invokable and proxied by the server. One forged frame permanently kills a victim's reconciliation for the session. Falsifies `docs/01:96-100`. | Critical | High | S | T1 | F-A3-2, F-A7-2 |
| 3 | `InputCommand.MoveX` never clamped server-side → 127× speed hack. Falsifies `docs/02:15-16` verbatim. One `Math.Sign`. | Critical | High | S | T1 | F-A7-3 |
| 4 | `README.md` cannot be followed: sends the reader to a scene deleted two days ago, and understates the project by two phases. | Critical | High | S | T2 | F-A1-1, F-A10-2, F-A8-15 |
| 5 | No test in the repo can fail because of a networking bug. `Reconcile` has 0% coverage; the "replay" tests are tautologies. | Blocker | High | M | T3 | F-A8-1, F-A3-6, F-A8-2 |
| 6 | Mobile and WebGL are stated targets with zero supporting code. WebGL cannot connect; Mobile has no input path. | Blocker | High | S to retract | T4 | F-A6-1, F-A6-2, F-A6-3, F-A1-10, F-A2-12, F-A4-3 |
| 7 | Predicted peer collision runs against interpolated (~0.47 s stale) peer positions, contradicting three documents. | Major | High | S (doc) / M (code) | T2 | F-A3-3 |
| 8 | No in-match HUD. The core mechanic — a draining life timer — is invisible to players. Both feeder properties exist and are unused. | Major | High | S | T5 | F-A1-3 |
| 9 | `main` is 55 commits behind at Phase 0, zero tags, no build ever produced. The documented release model has a 0% execution rate. | Major | High | S | T5 | F-A10-1, F-A9-2, F-A9-3 |
| 10 | Character select is checked in the roadmap and provably unreachable; `CharacterCount` is never set, so a 5th skin would be silently unselectable. | Major | High | S | T5 | F-A1-4, F-A2-5, F-A7-12 |

**Just outside:** no CI (F-A9-1/F-A10-4, Major, S); debug tooling ships in the player build
(F-A9-4/F-A6-4/F-A5-3/F-A2-9, Major, S); snapshot RPC broadcasts at 30 Hz in the lobby (F-A5-1, Major,
S); bandwidth never measured while the profiler is already installed and enabled (F-A4-1, Major, S).

---

## 5. Delete list — negative-cost fixes

Over-engineering findings whose remedy is removal. Total: **~1 assembly, ~160–180 LOC, 18 stale refs.**

| # | Delete | Saves | Risk | Source |
|---:|---|---|---|---|
| 1 | `Snackdown.Core.asmdef` (the file, not its code) | 1 of 8 assemblies, 1 `.csproj`, 2 dead refs | **None.** Nothing imports it, verified by grep. `FrameRatePolicy` is a `[RuntimeInitializeOnLoadMethod]`; `AppBootstrap`'s GUID reference survives an assembly move. Ask first — it changes the layer diagram. | F-A2-3 |
| 2 | `"Unity.InputSystem"` from `Snackdown.UI.asmdef` | 1 asmdef edge; makes a documented invariant compiler-enforced | ~20 LOC to relocate the F1–F4 hotkeys into `Snackdown.Input` | F-A2-4 |
| 3 | Merge `ReconciliationStats` + `RunRecorder` into one store with two views | ~60–80 of 246 LOC, and removes existing drift | None external — both consumers survive; the CSV format `docs/05` depends on is preserved | F-A2-8 |
| 4 | 7 dead public members | ~30 LOC | None. But check `WorldSnapshotBuffer.Clear()` first — its sibling *is* called, so the asymmetry may be a bug rather than dead code | F-A8-10 |
| 5 | Orphaned `Assets/InputSystem_Actions.inputactions` | 1 asset + meta | None — zero first-party references. **Unity asset deletion: needs Luca per `CLAUDE.md`.** | F-A2-11 |
| 6 | `Snackdown.slnx` from git tracking (+ `*.slnx` to `.gitignore`) | A permanently dirty working tree | None. Mutating git op — needs Luca. | F-A10-5 |
| 7 | 17 merged remote branches | Branch-list noise a reviewer sees first | None. Mutating — needs Luca. | F-A10-7 |

**Explicitly NOT deleted, and defended:** `IPredictedPeer` (40 LOC, one implementation). Deleting it
puts `Gameplay` types inside `Snackdown.Netcode.asmdef` and recreates the exact cycle ADR 0002 was
written about. `docs/01:159-160` names "gameplay depends on netcode, never the reverse" as *enforced
rather than promised* — the assembly boundary is that enforcement. Deleting it to save 40 lines turns
the project's most defensible architectural claim back into a convention. A2, A3 and A8 all reached
this independently.

---

## 6. The three mandate answers

### 6.1 Is the vision faithfully implemented? — **7 / 10**

`dev` is a coherent vertical slice, not a pile of parallel experiments. The whole pitched loop runs
end to end. Every one of the 49 runtime files is reachable from a scene, prefab, `.asset`, or a type
that is — **zero dead files, zero abandoned subsystems, zero TODO/HACK/WIP markers** across 57 C#
files, all confirmed rather than assumed.

The effort distribution reflects the stated priority, which is the unusual part: **Pillar 1 (netcode)
is 42% of all C# and 59% of test code.** The common failure mode for a portfolio netcode project —
menu and lobby sprawl swallowing the netcode — did not happen. UI is 15% of runtime, and 216 of those
913 lines are the netcode debug overlay.

Three points off: −1.5 for a README whose "Running it" section fails on step 1, −1 for Pillar 2
shipping without the HUD that makes it a game rather than a simulation, −0.5 for the
checked-but-unreachable character select and two asserted platforms with nothing behind them.

Scope drift is real but small: ~530 LOC, 8.7% of runtime, concentrated in multi-arena and spectator-pan
machinery for content that does not exist yet. The mildest possible form — small, correct, honestly
documented, and load-bearing for the networked additive load Phase 3 needed anyway.

### 6.2 Will it run online without saturating servers? — **GO, with changes**

`target_ccu` is 0 and hosting is Relay-only, so this is answered as the design analysis the author
should have ready, not as a launch gate.

| Question | Answer |
|---|---|
| **Host uplink at 4 players, relayed** | ~25.2 kB/s ≈ **202 kbps** snapshot + ~7 kbps `NetworkVariable` ≈ **209 kbps** (ESTIMATED) |
| **Client downlink / uplink** | ~70 kbps / ~29 kbps (ESTIMATED) |
| **Host tick cost** | **0.074 ms against a 33.3 ms budget — 0.22%.** ~450× headroom it will never need |
| **Reconciliation replay spike** | 0.03–0.35 ms at realistic RTT; hard-bounded at 11.3 ms by an explicit capacity check |
| **CCU ceiling as built** | **n = 9.** `WorldSnapshotBuffer.MaxBodies = 8` truncates silently past it — a deliberate, documented constant matching the 4-player target |
| **First bottleneck after that** | **Bandwidth, ~n = 22.** CPU does not bind until ~n = 200. *Bandwidth binds 9× before CPU, and a deliberate constant binds before either.* |
| **Scaling shape** | Host uplink is **O(N²)**: `30(N−1)(42N+112)` B/s. 3× players = **8.1×** host uplink. Client downlink grows only linearly. MTU wall at n ≈ 30 |
| **Cost** | **~$0.0032 per 10-minute 4-player session**, ~2¢/hour. 0.0202 GiB/session is the durable figure; the $0.16/GiB rate must be re-checked against current UGS pricing |
| **Interest management** | None, and none needed at 4 players in one arena. Adding it would be a textbook rubric-#7 hit |

**The changes in "GO with changes"** are the three one-line RPC fixes (T1) — not capacity work. Nothing
about the traffic model needs to change for the stated target. The one real platform constraint is
that a mobile *host* sits at 82% of a 256 kbps uplink budget; a mobile *client* is comfortable.

**Measurement caveat that must travel with every number above:** all of them are `ESTIMATED`. Not one
byte has ever been counted in this project, despite `com.unity.multiplayer.tools` 2.2.8 being
installed and `NetworkMessageMetrics`/`NetworkProfilingMetrics` being **already enabled** in
`Bootstrap.unity`. One Play-mode session with the Network Profiler closes this permanently.

### 6.3 Is the team over-engineering? — **No.**

The suspicion that commissioned this audit is **not borne out**, and the evidence is quantitative.

- **5 over-engineering rubric hits across 49 files (0.10/file)**, four of them Minor or Nit.
- **Rubric items #2 (factories/generics), #7 (premature performance), #8 (speculative extensibility)
  and #10 (leaky abstraction over an unstable domain) have zero hits project-wide.**
- Across 6,098 LOC: no DI container, no event bus, no service-locator class, no ScriptableObject
  "architecture", no reflection, no source generators, no custom serializer, no state-machine
  framework, no abstract base classes, **no generic types at all**, exactly **2 interfaces**, and
  **3 RPCs total**.
- One interface has a single implementation (`IPredictedPeer`, 40 LOC) and it is load-bearing for a
  compiler-enforced boundary, not decorative.
- All 5 `ScriptableObject` configs were judged individually against rubric #4. **None is a hit.** The
  only genuine #4 hit in the project is a plain `int` property (`CharacterCount`).
- **The abstraction tax is MEASURED at approximately zero.** The stun mechanic cost 4 runtime files,
  +138/−0 lines, of which **+6/−0** in `PlayerMotor` — verifying `docs/02`'s "insertions rather than
  rewrites" claim from git rather than from prose. Every file a dev must touch to add an ability would
  exist in a flat single-assembly design too.

**The strongest single piece of counter-evidence** is commit `d7019ed`: an abstraction proposed in a
187-line ADR, costed, and then deliberately **not built**, with the ADR marked *Rejected* rather than
deleted so an NGO IL-postprocessor finding established by experiment would survive. Total cost: 234
lines of markdown, 16 minutes, zero lines of production churn. Engineers who over-engineer do not
write that commit.

**The failure is the opposite one, at five specific points:** three missing one-line guards on the
RPCs (T1), a 413-LOC `MainMenuController` that is simultaneously composition root, menu, lobby and
roster view, `Reconcile` living inside a `MonoBehaviour` where no test can reach it, nine ambient
statics serving as an undeclared service locator, and no input seam for two stated platforms.

**The three concrete deletions** are items 1–3 in §5: `Snackdown.Core.asmdef`, the `Unity.InputSystem`
reference on `Snackdown.UI`, and the `ReconciliationStats`/`RunRecorder` duplication. Together
~160–180 LOC and one assembly. That is the entire over-engineering surface of this project.

---

## 7. Remediation roadmap

### Now — this sprint (all S, ~1–2 days total)

| # | Action | Closes |
|---:|---|---|
| 1 | Add `InvokePermission = RpcInvokePermission.Owner` to `SubmitInputRpc` and `= Server` to `SnapshotRpc` | F-A3-1, F-A3-2, F-A7-1, F-A7-2 |
| 2 | Clamp `MoveX` with `Math.Sign` and mask `Buttons` on ingest in `EnqueueIfNew`; bound `count` in `SnapshotFrame.NetworkSerialize` | F-A7-3, and the allocation amplifier |
| 3 | Rewrite `README.md`'s "Running it" and "Status" against `Bootstrap.unity` and the Phase-3 state; fix `docs/03-roadmap.md:13,35`; add the test-suite line | F-A1-1, F-A10-2, F-A8-15, F-A8-17 |
| 4 | **Decide on Mobile/WebGL and write the answer down.** Retracting is one line and costs the netcode story nothing | F-A6-1, F-A1-10, F-A2-12, F-A4-3 |
| 5 | Correct the doc/comment facts: `~120 byte` → 176 B, `PlayerMotor`'s "Pure" `<summary>`, `docs/02`'s fruit RPC row, "36 tests" → 77, the 100k-rolls and "measured 1 write/s" claims | F-A4-2, F-A8-8, F-A3-14, F-A8-6, F-A8-7, F-A1-7 |
| 6 | Append "Superseded in practice" to ADR 0002 recording option 4 and `e99a6fb`; add `adr/README.md` explaining the numbering | F-A1-6 |
| 7 | Call `ServerDespawnAll()` from `ServerReturnToLobby` — after confirming whether the fruit actually survives the unload | F-A1-8, F-A3-11, F-A5-2 |
| 8 | Gate the snapshot broadcast by match phase (with a keepalive or a per-transition frame) | F-A5-1 |
| 9 | Wire `CharacterCount` from `CharacterCatalog.Count`; either add the picker or uncheck the roadmap item | F-A1-4, F-A2-5, F-A7-12 |

### Next — before calling it portfolio-ready

| # | Action | Effort | Closes |
|---:|---|---|---|
| 10 | **One `NetcodeIntegrationTest`**: host + client, scripted input, assert convergence and that a dropped snapshot leaves no permanent offset. Add `"testables"` to the manifest. One test closes the credibility gap; a suite is not required | M | F-A8-1, F-A3-6 |
| 11 | Extract `Reconcile`'s algorithm and the server input queue into engine-free types in `Snackdown.Netcode`, then test them | M | F-A3-7, F-A8-2, F-A8-4 |
| 12 | Add the HUD: one `HUD.uxml` + ~60 LOC reading the two properties that already exist and are already replicated. No netcode change | S | F-A1-3 |
| 13 | Move `NetDebugOverlay` into its own asmdef with `defineConstraints`, taking the Multiplayer Tools reference off `Snackdown.UI` | S | F-A9-4, F-A6-4, F-A5-3, F-A2-9 |
| 14 | Produce one Windows x64 build; run a real two-machine Relay session; record what broke | S | F-A9-2 |
| 15 | Merge `dev` → `main`, tag per the project's own scheme, sync `bundleVersion`. **After** #3 | S | F-A10-1, F-A9-3 |
| 16 | One GitHub Actions workflow: compile from a cold clone + EditMode suite, on PRs into `dev`. Nothing more | S | F-A9-1, F-A10-4, F-A8-3 |
| 17 | Decide and document the peer-collision behaviour — either key the world buffer off authoritative peer state, or correct the three docs | S/M | F-A3-3 |
| 18 | Handle the host disappearing: subscribe `OnClientStopped`, unload the arena locally, surface `DisconnectReason`. Render the connection failure text the UI already receives | S | F-A3-5, F-A9-6, F-A9-9 |
| 19 | Refuse connections outside Lobby/Loading (the refusal path already exists and already reaches the player) | S | F-A3-4 |

### Later — pre-launch equivalent, or never for a portfolio

Execute the delete list (§5). Split `MainMenuController`'s composition-root role into a
`SessionLauncher` in Bootstrap (F-A2-6). Break the `PlayerLife` ↔ `MatchDirector` cycle or declare
intra-`Gameplay` layering conventional in `docs/01` (F-A2-2). Bit-pack `NetworkObjectId` and add a
per-entry skip for eliminated players — together ~16% off the host uplink (F-A4-4, F-A4-6). Reuse the
receive-side snapshot array (F-A4-7, F-A6-10, F-A5-7). Resolve the implicit-`private` debt one way or
the other (F-A8-12). Sprite atlas + compressed platform overrides when content grows (F-A6-12).

### Measure first — before deciding anything in this list

Four things are currently derived and should be measured. Every one is under an hour.

1. **Bandwidth.** One Play-mode session with the Multiplayer Tools Network Profiler. The package is
   installed and the two `NetworkConfig` flags are already on. Closes F-A4-1 and turns every number in
   §6.2 from ESTIMATED to MEASURED.
2. **One profiler capture** of a 4-peer session. The GC Alloc column alone settles the overlay cost
   (F-A6-5, F-A5-3) and `C_cast = 5 µs`, the assumption every CPU figure rests on.
3. **Does spawned fruit survive the additive arena unload?** One two-match session with the hierarchy
   open. Decides whether F-A5-2 is a leak to wire up or a method to delete.
4. **`docs/05`'s open item — "ten corrections in B replayed zero ticks."** A3 offers a concrete
   mechanism (`LocalTime` reset backwards; note `EnableTimeResync: 0` in `Bootstrap.unity`).
   One temporary log line confirms it. *"We diagnosed it"* is a better interview answer than
   *"not diagnosed."*

---

## 8. What is genuinely good — consolidated

Every agent was required to fill this section and none of them struggled. The recurring items,
deduplicated:

- **The wire surface is minimal and hand-built.** Three RPCs in 6,098 LOC. Every replicated type
  implements `INetworkSerializable` by hand — no reflection anywhere. No `NetworkTransform`,
  `NetworkRigidbody2D` or `NetworkAnimator` in the entire project, verified independently three times
  by resolving component GUIDs in the prefab YAML. A hand-rolled snapshot system living *alongside*
  an engine transform sync is the most common way this kind of project doubles its bandwidth, and it
  did not happen here.
- **`PlayerState` is complete on the wire, and the completeness is argued.** `CoyoteTimer`,
  `JumpBufferTimer` and `StunTimer` are serialized alongside position and velocity. Replicating
  pos/vel and leaving the feel timers local is the single most common source of "random" desync in
  hand-written reconciliation, and it is correctly avoided with the reasoning written down.
- **Redundancy instead of retransmission, applied consistently.** Three commands per input packet
  deduped by a monotonic tick; the same logic applied independently to the `IsTeleport` flag because
  *"a flag sent once over unreliable delivery is a flag that can be lost."*
- **Teleport is distinguished from misprediction, and it was found by measurement** — a first
  correction of 3.8 units against a real one of 0.29, recorded as a pitfall in `docs/05`.
- **The RTT derivation.** Establishing that `UnityTransport.GetCurrentRtt` reports off the *reliable
  sequenced* pipeline while every packet this layer sends is unreliable, deriving the honest number
  from `(latestPredictedTick − ackTick)`, and then keeping the transport's wrong number side by side
  in the CSV purely to show the discrepancy. A3 and A8 both called this the sharpest work in the repo.
- **Deadline replication instead of counted-down timers**, in two independent places. Every peer
  derives the number from `ServerTime` — they agree because they read the same clock, not because
  someone keeps telling them.
- **Allocation discipline in the simulation path is essentially perfect: 0 bytes/s.** Ring buffers
  with per-slot tick stamps so a wrapped entry cannot masquerade as fresh, a `readonly struct`
  context over a borrowed array, reused scratch arrays on the send path, non-allocating overlap
  queries. Six independent decisions pointing the same way. Zero LINQ in the project; zero coroutines.
- **The replay depth is explicitly bounded** with the right reasoning — the difference between an
  11.3 ms worst case and an unbounded one. Very few hobby implementations have this check at all.
- **The reconciler-not-listener pattern transferred from netcode to UI**, in three files, named as
  such, after a real observed bug (two stacked lobby scenes). Design reasoning moving across layers is
  the single most interview-legible thing in the repo.
- **ADR 0002.** A compiled probe against NGO 2.11 reproducing a `NullReferenceException` inside
  `Mono.Cecil` from the IL post-processor, a second probe narrowing the boundary, the correct refactor
  designed — and then declined. The NGO codegen finding alone is publishable.
- **The commit log survives close reading.** 55,847 bytes of body prose over 36 commits, median 24
  lines. Zero direct commits to `dev`, zero evil merges, zero conflicts, zero reverts, zero rebase
  scars. A10 read six bodies in full and none is a dressed-up diff restatement. `c9cd571` documents
  the author's own false verification in the permanent record.
- **The tests that exist are the right kind** — properties, not golden values — and each one names the
  failure it prevents. They also honestly document their own limits.
- **Source-control infrastructure**: a hand-written `.gitattributes` routing 18 Unity YAML types with
  the rejected alternative explained inline, a correct and documented no-LFS decision (78 bytes of
  binary added across all 55 commits), a committed lockfile, and a PR template that demands *"List
  what you did NOT verify too."*
- **Config and logging hygiene: 5/5.** No keys, tokens or endpoints. Eleven `Debug.Log` calls in
  6,098 LOC, none on a per-tick path, none logging player data. Analytics, ads, crash and performance
  reporting all disabled. Both third-party art packs verified inert — zero executable files.

---

## 9. Open questions — human decisions only

1. **Do Mobile and WebGL stay on the target list?** Six findings and one Blocker turn on this single
   answer. Retracting costs the netcode story nothing.
2. **Is the shipping build the demo build?** `README.md` instructs a reviewer to press F1, which
   implies the overlay is *meant* to be there. If there is only ever one build and it is the demo,
   F-A9-4 softens considerably. The project currently has no way to express the difference.
3. **Is mid-match joining supported?** The cheap fix for F-A3-4 (refuse outside Lobby/Loading) is
   correct only if the answer is no.
4. **Is a HUD inside Phase 3, or accepted as a demo limitation?** Phase 3 is marked complete and a
   viewer cannot see the mechanic the pitch is built on.
5. **Is the GitHub repository public, and what is its actual default branch?** The local `origin/HEAD`
   says `main` — which would mean a reviewer's first view is the Phase 0 scaffold — but that symref
   may be stale. If public, the committed `cloudProjectId`/`organizationId` let anyone consume the
   owner's UGS Relay quota (identifiers, not credentials — worth knowing, not urgent).
6. **Was ADR 0002 written before the decision or after it?** 16 minutes separate the ADR from its
   rejection. Written-then-decided is exemplary; decided-then-documented is a different claim to make.
7. **Should the implicit-`private` debt be normalised or the convention amended?** The codebase is 100%
   consistent with itself and 0% consistent with `CLAUDE.md:54`. `CLAUDE.md` itself requires asking.
8. **Was the 100k-roll verification run somewhere that still exists?** If so, promoting it into
   `FruitTableTests` is 15 minutes rather than a rewrite.
9. **Is `Snackdown.Core` intended to grow?** `docs/01:37` promises a Phase 2 app state machine that
   never arrived. If it is coming, keep the assembly and fix the doc's tense.
10. **Should `MovementConfig` be replicated or hashed into the connection payload?** A version-skewed
    client with the same `Application.version` but a different asset desyncs continuously with no
    diagnostic. Six lines, if the answer is yes.
