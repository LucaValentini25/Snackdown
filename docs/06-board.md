# 06 — Board

> Generated from `docs/board.json` by `tools/board/build_board.py` — do not edit by hand.

Live state of the work: what is done, what is being done now, every decision taken and why,
and what is known to be wrong. Updated when a task, an epic or a working session closes.

**Last updated:** 2026-08-23

**Overall:** 50% — 23 done, 0 in progress, 0 blocked, 23 to do, 2 dropped, across 8 epics.

## Epics

| Epic | Phase | Status | Progress |
|---|:---:|---|---:|
| [Netcode core — predicted character](#netcode-core-predicted-character) | 1 | Done | 9/9 |
| [Connection layer — LAN and Relay behind one flow](#connection-layer-lan-and-relay-behind-one-flow) | 2 | Done | 6/6 |
| [Gameplay core — the rules, server-authoritative](#gameplay-core-the-rules-server-authoritative) | 3 | Done | 5/5 |
| [In-match HUD — life bars and round clock](#in-match-hud-life-bars-and-round-clock) | 4 | Done | 3/3 |
| [Separate player identity from avatar](#separate-player-identity-from-avatar) | 4 | In progress | 0/8 |
| [Wardrobe — unique, changeable skins](#wardrobe-unique-changeable-skins) | 4 | To do | 0/5 |
| [Verification — make the netcode claims checkable](#verification-make-the-netcode-claims-checkable) | 5 | To do | 0/4 |
| [Polish and release](#polish-and-release) | 5 | To do | 0/6 |

### Netcode core — predicted character

A character that responds instantly on the owning client, is decided by the server, and self-corrects when the two disagree. Everything else in the project is scaffolding around this.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[x]` | Fixed 30 Hz tick wired to NetworkTickSystem | EditMode — tick ordering | — |
| `[x]` | InputCommand sampled on the owner and sent to the server | EditMode — input sanitation | — |
| `[x]` | Shared replayable Simulate() used by both sides | EditMode — 40 ticks replayed twice are bit-identical | Covers the arithmetic; the terrain-cast path runs with GroundMask = 0 in fixtures |
| `[x]` | Client prediction with a (tick, input, state) ring | EditMode — PredictionBufferTests | — |
| `[x]` | Server snapshots carrying lastProcessedInputTick | — | — |
| `[x]` | Reconciliation — rewind to snapshot, replay pending inputs | none — lives inside a MonoBehaviour and is unreachable from EditMode | 90 lines, 9 branches, 0% coverage. The project's headline mechanism |
| `[x]` | Snapshot interpolation for remote players | EditMode — SnapshotInterpolatorTests | — |
| `[x]` | Debug overlay: predicted vs authoritative, RTT, corrections | — | Ships in the build and costs ~97% of host managed allocation |
| `[x]` | Validated under simulated latency and packet loss | manual — 150 ms / 20% loss, median error 0.302 units | Written up in docs/05 |

### Connection layer — LAN and Relay behind one flow

The same join flow whether the peer is on the LAN or across the internet, with the door closed to anything unchecked.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[x]` | IConnectionProvider — async throughout, failures as return values | — | — |
| `[x]` | DirectConnectionProvider — host and join by IP | manual — two peers on a LAN | — |
| `[x]` | RelayConnectionProvider — join by six-character code | manual — a real hosted session | Sessions API; project linked with Relay and Lobby enabled |
| `[x]` | Connection approval with payload and version check | EditMode — nickname sanitation, ConnectionApprovalTests | — |
| `[x]` | SessionRoster — replicated names, skins, ready state | — | About to be rewritten as an index by the player-session epic |
| `[x]` | Menu and lobby in UI Toolkit, wired to the abstraction | — | — |
| `[-]` | Character picker | — | Transport half shipped, picker never did. Moved to the wardrobe epic where it belongs |

### Gameplay core — the rules, server-authoritative

The whole pitched loop running end to end, with every rule decided by the server against server-owned positions.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[x]` | Life timer draining server-side, replicated ~1 Hz | — | 1 write/s by configuration, plus an immediate publish on pickup and on death |
| `[x]` | Fruit spawner with a rarity table, networked pickup | EditMode — FruitTableTests | 8 fruit, 35% to 1%, worth 3s to 20s |
| `[x]` | Head-bounce detection and stun | EditMode — StunTests | Stun lives in PlayerState so reconciliation replays through it |
| `[x]` | Players solid to each other, predicted client-side | EditMode — PeerCollisionTests | Resolves against interpolated peer positions — see known problems |
| `[x]` | Death to spectator, win conditions, end screen | EditMode — ArenaBoundsTests | Player hidden rather than despawned — the workaround the player-session epic removes (PR #19) |

### In-match HUD — life bars and round clock

Make the core mechanic visible. A draining life timer nobody can see makes the phase complete on paper and undemoable in practice.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[x]` | Round clock | — | (PR #23) |
| `[x]` | Life bars, built both ways — floating and along the bottom | — | F3 swaps them at runtime so the better one can be picked by looking (PR #23) |
| `[x]` | Nameplates sized in metres, not by transform scale | — | Three consecutive sizing bugs; the rule they came from is written down in docs/01 (PR #23) |
| `[-]` | Pick one layout and delete the other | — | Superseded by D-006 — it becomes a player preference instead of a choice to make |

### Separate player identity from avatar

One networked object per connection that owns identity, life and stats, and spawns a disposable avatar per round. Unblocks late join, real despawn on death, the wardrobe and host-configured matches — none of which can be built cleanly on top of NGO's default one-prefab-per-connection model.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[ ]` | Stand up the networked test harness | host and client complete the handshake and each sees the other | Needs a testables entry in manifest.json — NGO's integration harness is currently unreachable |
| `[ ]` | PlayerSession exists alongside the avatar, unread | on connect, both peers see a session carrying the sanitized nickname | — |
| `[ ]` | Roster becomes an index over live sessions | a third client sees the two already present, with names and ready state | NetworkList and PlayerSlot are deleted here |
| `[ ]` | Life, fruit count and stats move onto the session | a fruit taken by the client adds life on the server and reaches both HUDs | — |
| `[ ]` | Flip the spawn — PlayerPrefab becomes the session | death despawns the avatar, the session survives with its final life, next round respawns it | The big one. Removes hide-instead-of-despawn and the spawn-point teleport |
| `[ ]` | Late join arrives as a spectator | a client joining during Playing has no avatar, is not counted alive, and cannot win on the clock | — |
| `[ ]` | Host can kick, with a reason the kicked client sees | a non-host asking for a kick is ignored by the server | — |
| `[ ]` | Replicated match settings and difficulty profiles | host lowers starting life to 30 and the client's round starts at 30 | Nothing Simulate() reads is exposed — see D-005 |

### Wardrobe — unique, changeable skins

First free skin on arrival, changeable in the lobby, never two players wearing the same one. Nearly free once the skin lives on the session.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[ ]` | Approval assigns the first free skin instead of always 0 | four clients connect and get four different skins | — |
| `[ ]` | Wire CharacterCount from CharacterCatalog.Count | a fifth catalog entry is selectable | Hardcoded 4 today, so a fifth skin would be silently clamped away |
| `[ ]` | Wardrobe UI in the lobby, taken skins shown as taken | a client asking for a taken skin is refused by the server | — |
| `[ ]` | Nickname remembered in PlayerPrefs | — | Zero PlayerPrefs calls in the project today |
| `[ ]` | HUD layout preference, host default plus local override | — | D-006 |

### Verification — make the netcode claims checkable

Close the gap where no test in the repository can fail because of a networking bug, and no byte has ever been counted.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[ ]` | Integration test — a dropped snapshot leaves no permanent offset | self | — |
| `[ ]` | Extract Reconcile so it can be tested without a MonoBehaviour | EditMode — replay converges after a forced desync | — |
| `[ ]` | Seeded 100k-roll fruit distribution test | self | The claim exists in docs; the test that backs it does not |
| `[ ]` | Measure bandwidth with the profiler already installed | self | Every byte figure in the docs is derived from reading serializers |

### Polish and release

Make the finished work visible to someone who was not here while it was built.

| | Task | Verified by | Notes |
|:---:|---|---|---|
| `[ ]` | Decide how peer contact is predicted | — | Touching it invalidates the Phase 1 measurements, which would need re-running |
| `[ ]` | Spectator camera follows a player and switches targets | — | — |
| `[ ]` | A second arena | — | Authoring only — ArenaCatalog and the networked load are already in place |
| `[ ]` | Public lobby browser | — | D-009 — deferred to the end deliberately |
| `[ ]` | Keep debug tooling out of the player build | — | — |
| `[ ]` | A runnable build, main brought up to date, a tag | — | No player build has ever been produced |

## Decisions

Every choice that closed off an alternative, with the reasoning that closed it. This is
the part of the board worth reading a year from now.

### D-010 — The board is generated, never hand-kept

*2026-08-23*

**Chosen:** docs/board.json is the only file edited. A script renders docs/06-board.md for readers and docs/local/board.html for working.

**Why:** A board is touched on every task, and two hand-maintained copies of a thing edited that often disagree within a week — the same failure the audit found across the docs, where the reasoning stayed excellent and the facts drifted. One source removes the possibility rather than requiring discipline.

**Rejected:** Hand-writing the HTML alongside the markdown; keeping the board only as an HTML page, which the repository's own rules forbid as a deliverable.

### D-009 — Public lobby browser is deferred to the end

*2026-08-23* · epic **polish**

**Chosen:** Join by code only until the player lifecycle and the wardrobe are finished.

**Why:** It demonstrates no netcode that the connection layer does not already demonstrate, and it adds a screen and a lifecycle of its own. It is worth having for how it looks in a demo video, which is a reason to do it late rather than never.

**Rejected:** Building it now as the product-facing front of the project.

### D-008 — Naming — PlayerSession and PlayerRoster

*2026-08-23* · epic **player-session**

**Chosen:** PlayerSession for the per-connection object, PlayerRoster for the index over them.

**Why:** Controller already means a character controller in Unity, and the object being named is a connection with an identity attached, which is what a session is. Roster keeps the name the UI layer already uses.

**Rejected:** PlayerController, PlayerHub, NetworkPlayer.

### D-007 — Tests ship with the feature, not as a phase

*2026-08-23*

**Chosen:** Every task carries the test that verifies it, written in the same PR. The networked harness is stood up first so this is possible at all.

**Why:** A verification phase at the end is a phase that gets cut. The cost of not having this is already measured: a join-breaking regression survived six merged PRs over 47 hours, and the commit that fixed it says so in its own body.

**Rejected:** Writing the integration tests after the refactor, against the new architecture; leaving them in Phase 5 as originally planned.

### D-006 — HUD layout is a player preference with a host-set default

*2026-08-23* · epic **wardrobe**

**Chosen:** The host picks the room default; each player can override it locally, remembered in PlayerPrefs.

**Why:** It is the only setting that changes nothing about the match, only how one screen looks. Replicating it means a player whose taste differs from the host's plays all night with the layout they did not want, for no rule-level reason.

**Rejected:** Host-controlled and replicated for everyone; picking one layout and deleting the other.

### D-005 — Match settings replicate as a struct; nothing Simulate() reads is configurable

*2026-08-23* · epic **player-session**

**Chosen:** A MatchSettings struct in a server-written NetworkVariable, seeded from difficulty presets and overridable field by field. MovementConfig stays out of it entirely.

**Why:** Everything the host wants to tune — life, drain, round length, stun, fruit — is read by the server alone and reaches clients as an already-applied result, so a mismatch costs nothing. Everything Simulate() reads is executed identically on both sides of the wire, and a divergence there produces a trembling character whose symptom points at reconciliation, which is not where the bug is.

**Rejected:** One flat replicated config covering movement too; closed difficulty profiles with no per-field override.

### D-004 — Keep NGO's auto-spawn and repoint it

*2026-08-23* · epic **player-session**

**Chosen:** NetworkManager.PlayerPrefab becomes the PlayerSession prefab. The avatar becomes an ordinary network prefab the session spawns with SpawnWithOwnership.

**Why:** Disabling AutoSpawnPlayerPrefabClientSide and hand-rolling the spawn would mean rewriting ConnectionApproval's admission path and losing GetPlayerNetworkObject. Repointing keeps both, and makes NGO's own clientId-to-object lookup return exactly the object worth reaching.

**Rejected:** Turning auto-spawn off and spawning both objects manually from the approval callback.

### D-003 — Life, fruit count and match stats live on the session

*2026-08-23* · epic **player-session**

**Chosen:** The session tracks life, fruit eaten and per-round stats, all server-validated. The avatar is physical only; the HUD reads the session.

**Why:** If the avatar has to survive death to keep the life value, it stays the owner of identity and the refactor achieves nothing. Moving life up also removes a branch from PredictedPlayer.IsSolid: a dead player has no avatar, so peer-collision prediction never considers it, rather than checking a flag.

**Rejected:** Leaving PlayerLife on the avatar and continuing to hide the character instead of despawning it.

### D-002 — The roster becomes an index, not a store

*2026-08-23* · epic **player-session**

**Chosen:** PlayerRoster keeps no data of its own — it is an ordered view over the live sessions, the same pattern as PlayerLife.All.

**Why:** Nickname and skin already exist in three places, which works only because the flow is one-directional and once-only. The wardrobe, per-round avatars and late join all break that condition, and a value with three owners fails by showing each peer something different — no crash, no exception, and rare enough to be blamed on the network.

**Rejected:** Letting the roster and the sessions both hold identity; keeping the NetworkList as the source and having sessions read it.

### D-001 — Separate player identity from avatar, and do it first

*2026-08-23* · epic **player-session**

**Chosen:** One PlayerSession NetworkObject per connection, owning identity and life; a disposable avatar spawned per round.

**Why:** Four wanted features — late join, real despawn on death, the wardrobe and returning to the lobby — are each awkward on their own under NGO's default model, and each stops being a special case once identity and avatar have separate lifetimes. Building them first means building them twice.

**Rejected:** Doing the wardrobe and match config first for faster visible progress; keeping NGO's model and patching late join and death as special cases.

## Known problems

Things that are wrong and are not being fixed yet, recorded so they are a decision
rather than an oversight.

| | Problem | Impact | Status |
|:---:|---|---|---|
| `[ ]` | No test in the repository can fail because of a networking bug | The deliverable is netcode correctness and nothing verifies it. A join-breaking regression once survived six merged PRs. | open |
| `[ ]` | Reconcile has zero test coverage | 90 lines and 9 branches of the project's headline mechanism, unreachable from EditMode because it lives in a MonoBehaviour. | open |
| `[ ]` | No byte of bandwidth has ever been measured | Every figure in the docs is derived from reading the serializers, while the profiler package is installed and its metrics already enabled. | open |
| `[ ]` | main is 55 commits behind and no build has ever been produced | A reviewer following the repository link lands on the Phase 0 scaffold. Managed stripping and scene-by-name resolution are unexercised. | open |
| `[ ]` | The debug overlay ships enabled and dominates host allocation | Its IMGUI pass is ~97% of all managed allocation on the host — 320-600 KB/s against ~12 KB/s from the entire simulation path. | open |
| `[ ]` | Peer contact is predicted against interpolated positions | Roughly 0.47 s stale at the measured RTT, so close contact between two moving players reliably corrects. Fixing it invalidates the Phase 1 measurements. | open |
| `[ ]` | Snapshots broadcast at 30 Hz while the session sits in the lobby | Bandwidth spent on a match that is not running. | open |
| `[ ]` | FruitSpawner.ServerDespawnAll has no call sites | Fruit survives into the lobby and into the next match. | open |

## Session log

- **2026-08-23** — Stood up this board: one JSON source, two generated views, and a rule in CLAUDE.md that closing a task, an epic or a session updates it.
- **2026-08-23** — Designed the player-session refactor and took ten decisions on it. Confirmed by inspection that the auto-spawn does not need disabling, that every host-configurable parameter is server-read only, and that the EditMode suite is untouched by the change.
- **2026-08-23** — Read the repository end to end after time away: 64 scripts, six design documents and a ten-domain audit. Conclusion recorded — the netcode is solid and measured; everything unfinished sits in the lobby, identity and configuration layer.

