# 06 — Board

> Generated from `docs/board.json` by `tools/board/build_board.py` — do not edit by hand.

Live state of the work: what is done, what is being done now, every decision taken and why,
and what is known to be wrong. Updated when a task, an epic or a working session closes.

**Last updated:** 2026-08-24

**Overall:** 63% — 29 done, 0 in progress, 0 blocked, 17 to do, 2 dropped, across 8 epics.

## Epics

| Epic | Phase | Status | Progress |
|---|:---:|---|---:|
| [Netcode core — predicted character](#netcode-core-predicted-character) | 1 | Done | 9/9 |
| [Connection layer — LAN and Relay behind one flow](#connection-layer-lan-and-relay-behind-one-flow) | 2 | Done | 6/6 |
| [Gameplay core — the rules, server-authoritative](#gameplay-core-the-rules-server-authoritative) | 3 | Done | 5/5 |
| [In-match HUD — life bars and round clock](#in-match-hud-life-bars-and-round-clock) | 4 | Done | 3/3 |
| [Separate player identity from avatar](#separate-player-identity-from-avatar) | 4 | In progress | 6/8 |
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
| `[x]` | SessionRoster — replicated names, skins, ready state | — | Rewritten by ps-2: the NetworkList and PlayerSlot are gone, the three fields live on PlayerSession, and the roster is an index over them in Gameplay/Player |
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
| `[x]` | Stand up the networked test harness | PlayMode — HandshakeTests: the client is synchronized, the host sees it, it sees the host, and two clients see each other | The testables entry this task was written around turned out to be the wrong route and was not added — see D-011. NetworkedFixture is ours: 4 tests, no manifest change |
| `[x]` | PlayerSession exists alongside the avatar, unread | PlayMode — PlayerSessionTests: a joining client gets a session of its own, both peers read the sanitized name, the host's own session carries the name it chose, and a leaving client takes its session with it | Cost two things the task did not anticipate: NetworkSimulation had to become a prefab to be spawnable in a test (D-013), and the session reads its own name out of approval rather than being handed it, because the object that spawns it lives in the assembly this one depends on |
| `[x]` | Roster becomes an index over live sessions | PlayMode — SessionRosterTests: a client joining a running session sees all four players with their names, skins and ready flags; every peer orders the list the same way; a client cannot ready up a player it does not own | NetworkList and PlayerSlot are deleted as planned, and the roster went with them — it cannot name PlayerSession from Snackdown.Connection, so it moved to Gameplay/Player (D-014). Two things the task did not anticipate: the pre-spawn write ps-1 promised turns out to log a warning on every join and was not done (D-015), and the harness had a real flake — every session bound the same UDP port, so a session now takes one of its own |
| `[x]` | Life, fruit count and stats move onto the session | PlayMode — FruitPickupTests: a fruit the client walks into adds life and a count on the server and both reach the client, the host is not credited for it, and a fruit nobody is standing on collects nothing and spawns silently | PlayerLife moved as a component onto the PlayerSession prefab rather than being folded into the class (D-016), so the referee, the HUD and the spectator camera did not change at all. Stats stayed at one number — fruit, cumulative for the connection — narrowing D-003, which had said per-round. The four places that reached life through the avatar now resolve owner to session, which is what forced PlayerSession.Of to take the peer it should look in (D-017). Two things found on the way: D-015 was reasoning from a wrong premise and is corrected, and FruitSpawner had been logging a warning per fruit for the same reason |
| `[x]` | Flip the spawn — PlayerPrefab becomes the session | PlayMode — AvatarLifecycleTests: GetPlayerNetworkObject returns the session and connecting alone grants no body; running out despawns the character on both peers while the session keeps its name and the life it ended on; the next round hands back a different character at the point it was asked for, with the life refilled | Both workarounds are gone as planned. The teleport went further than the note expected: with a character created at its spawn point instead of moved there, PlayerSnapshot lost its IsTeleport flag and the packet dropped from 8+42N to 8+41N (D-018). PredictedPlayer lost the life reference, IsSolid and the hide-on-death branch, and HeadBounce lost its alive check. The life reset moved from returning to the lobby onto starting a round, which closes a hole where a rematch from the end screen would have begun on the previous round's leftovers |
| `[x]` | Late join arrives as a spectator | manual — a real client joined a running sandbox match: the server holds it not alive, with no character anywhere, and ending the only player who was actually in the round produced NoWinner rather than handing the match to the joiner. PlayMode — AvatarLifecycleTests covers the other side of the boundary, that joining in the lobby still arrives in the round | The board's own test is not reachable in the harness: it needs phase Playing, and NetworkedFixture runs with EnableSceneManagement off because both peers share one editor scene list. Verified against a real join instead, with the improvised second peer created in-process — see D-019, which records this as a deliberate exception to D-007 rather than a gap. No new replicated state: the match sits a late arrival out through the same ServerEndRound that running out uses, so the camera, the referee and the next round all needed no changes |
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

### D-019 — The match layer ships verified by hand, and the reason is written down

*2026-08-24* · epic **player-session**

**Chosen:** ps-5 landed with a manual verification against a real join instead of the PlayMode test its task asked for. The half that is reachable — joining in the lobby still arrives in the round — is automated.

**Why:** Everything the match decides is gated on the phase, and every route to a phase past Lobby runs through a networked scene load. NetworkedFixture disables scene management because its two peers share one editor process and therefore one scene list, which is the machinery D-011 deliberately did not import from NGO. Enabling it is not a small change and its cost is unknown; extracting the phase machine into something EditMode can drive, which docs/01 recommends for cases like this, would test the transitions and still not test a join. So the honest options were a manual verification recorded in full or a task left open, and the feature was worth more than the checkbox. This is the second task in a row the same wall has blocked — the fix for the discarded scene-event status shipped the same way — so it is a decision now rather than a habit.

**Rejected:** Opening the harness to scene management first, which is the part of NGO's own test rig D-011 rejected and whose cost is unknown. Extracting the phase machine, which buys transition tests and not this one. Skipping ps-5 for ps-6, which is testable, and leaving the epic with a hole in the middle.

### D-018 — The teleport flag leaves the wire with the reposition it announced

*2026-08-24* · epic **player-session**

**Chosen:** PredictedPlayer.ServerTeleport, its announce counter and PlayerSnapshot.IsTeleport are deleted. A round spawns a character at its spawn point rather than moving one that already exists. The snapshot is 8+41N bytes instead of 8+42N — 172 rather than 176 with four players.

**Why:** The flag existed for exactly one situation: the server repositioning a character the owner was already predicting, which on the wire is indistinguishable from prediction having gone badly wrong. It was worth a byte per player per tick because the alternative was a spawn placement counted as a 3.8-unit correction, poisoning the one statistic the netcode layer is judged by. That situation no longer occurs — the character is created where it belongs, so every peer starts out agreeing about its position instead of being corrected into agreement. Keeping a public server API and a wire field for a case the game can no longer reach is exactly the leftover CLAUDE.md forbids, and a bool that is always false is worse than no bool.

**Rejected:** Keeping the mechanism as an unused capability and recording it as debt, which leaves dead public API and a field nobody can explain. Repurposing it for a mid-round respawn, which contradicts this task's own test — death despawns the character.

### D-017 — Looking a player up takes the peer to look in

*2026-08-24* · epic **player-session**

**Chosen:** PlayerSession.Of(NetworkManager peer, ulong clientId) rather than Of(clientId). The single-argument version was removed rather than kept beside it.

**Why:** Game code now crosses from one networked object to another to find a player: a fruit sees a character, resolves its owner, and writes to that owner's session. PlayerSession.All is a static, and the PlayMode harness runs a host and its clients in one process, so it holds several peers' copies of the same player and the first match is not reliably the caller's. Reading the wrong one is a wrong answer; writing to it is a permission error NGO logs and nothing acts on, which means a fruit that is silently never banked. Keeping a one-argument overload beside it would leave the footgun in the API and a remark explaining not to use it. In a shipped build every copy belongs to the only peer there is and the argument costs nothing.

**Rejected:** Documenting the hazard and keeping the short overload. Having each caller filter PlayerSession.All itself, which is the same three lines copied into four files.

### D-016 — PlayerLife moved to the session as a component, not as fields on it

*2026-08-24* · epic **player-session**

**Chosen:** The PlayerLife component was taken off the Player prefab and put on the PlayerSession prefab unchanged. PlayerSession exposes it as Life and adds one number of its own, the fruit count. The class was not merged into PlayerSession.

**Why:** Moving the component keeps one file to one type: the session is identity, PlayerLife is the rules of the life clock, and they are now two components on one object instead of one class doing both. It also cost almost nothing to the rest of the project — the referee, the bottom-strip HUD, the spectator camera and the director all read PlayerLife.All, which is unchanged, so none of them were touched. Folding the fields into PlayerSession would have meant rewriting all four to read PlayerSession.All and leaving a large class that mixes a player's name with the drain rate. What did have to change is the four places that reached life through the avatar — the fruit, the head bounce, the predicted character and the nameplate — and those had to change under either option.

**Rejected:** Merging life into PlayerSession and deleting PlayerLife. Leaving PlayerLife on the avatar and having the session hold a reference to it, which is the arrangement this epic exists to undo.

### D-015 — The session still adopts its identity after spawn, not before

*2026-08-23* · epic **player-session**

**Chosen:** PlayerSession keeps reading its name and skin out of ConnectionApproval in OnNetworkSpawn. Both are inside the spawn message every client receives. CORRECTED in ps-3 — the original entry claimed they arrived as deltas after it, and that was wrong.

**Why:** The decision is right and the original reasoning behind it was not, so both are recorded. What is true: writing a NetworkVariable before Spawn logs "NetworkVariable is written to, but doesn't know its NetworkBehaviour yet" — NGO does not attach a variable to its behaviour until the object spawns, and the flag that silences it is internal to the package. Verified in ps-3 by making a fruit do it and watching the test catch it. What was wrong: this was written up as a trade against two extra deltas. There are no deltas. NetworkObject.SpawnInternal runs the server's own spawn — and therefore OnNetworkSpawn — before calling SendSpawnCallForObject, which serializes each variable's current value, so anything written there is already in the initial state clients receive. Writing before Spawn buys nothing at all and costs a warning per join. The ps-1 note that promised this as a follow-up was wrong on the same point.

**Rejected:** Reaching NetworkVariableBase.IgnoreInitializeWarning by reflection. Spawning the session and having the roster write the values straight after, which is a real delta with an extra hop.

### D-014 — SessionRoster moved to Gameplay and stopped replicating anything

*2026-08-23* · epic **player-session**

**Chosen:** The roster is an index over the live PlayerSession objects, ordered by owner id, living in Snackdown.Gameplay.Player. NetworkList<PlayerSlot> and the PlayerSlot struct are deleted; name, skin and ready state are NetworkVariables on the session, and the ready Rpc moved there with them.

**Why:** The roster held a second copy of three facts the session already owns, kept in step by hand on the server — and a second copy is a copy that can disagree, which is the failure this whole epic exists to remove. Indexing means naming PlayerSession, and Snackdown.Connection cannot: Gameplay depends on it, not the other way round. So the file moved to the assembly that was always describing it, the same answer that broke the Netcode/Gameplay cycle in Phase 1. Nothing was lost on the wire — a late joiner still arrives to a full list, because NGO synchronizes the spawned session objects for the same reason it used to send the whole NetworkList. The roster keeps three jobs: spawning one session per connection, ordering the list so every peer draws the lobby identically, and raising one Changed event.

**Rejected:** Keeping the roster in Connection behind an IPlayerSession interface — an abstraction bought purely to keep a file in the wrong folder. Deleting the roster outright and letting each view read PlayerSession.All, which leaves nobody owning the order or the change event, and hands every reader a static that is shared between peers under the test harness.

### D-013 — A networked scene object becomes a prefab when it needs to be tested

*2026-08-23* · epic **player-session**

**Chosen:** NetworkSimulation — the one object carrying the tick loop, the roster, the director and the referee — was saved as a prefab and the scene now holds an instance of it.

**Why:** A scene object cannot be spawned by a test: the harness runs both peers in one editor process, so they share the scene and therefore the object, where a real session gives each peer its own copy through scene synchronization. Without this the only way to test a join was to bypass the roster and spawn sessions by hand, which tests the class and not the thing the task asks for. Saving it as a prefab also means the scene and the test exercise the same object rather than two configurations that drift.

**Rejected:** Splitting SessionRoster onto a GameObject of its own; leaving ps-1 without a test of the real join path.

### D-012 — Networked tests load the real prefabs, not ones built in code

*2026-08-23* · epic **player-session**

**Chosen:** PlayerSessionTests loads Player, PlayerSession and NetworkSimulation from the asset database. The test class is wrapped in UNITY_EDITOR; the assembly stays a Play mode one.

**Why:** Fabricating a prefab at runtime needs NetworkObject.GlobalObjectIdHash, which is internal — NGO can only do it in its own tests because its assembly is granted access. A runtime-built object keeps hash 0, and this task needs two of them, so registering the second is refused with a duplicate-hash error. Loading the real assets sidesteps that and buys the half a hand-built prefab could never cover: that the object which actually ships is wired up. An editor-only assembly was tried and reverted — the Test Runner reclassifies it as Edit mode, where NGO never initialises its message table and every test fails on the first message sent.

**Rejected:** Writing the internal hash by reflection; moving the prefabs into a Resources folder that every build would then carry.

### D-011 — The networked harness is ours, not the one NGO ships

*2026-08-23* · epic **player-session**

**Chosen:** A NetworkedFixture in a PlayMode assembly that creates a NetworkManager and a UnityTransport per peer on loopback. manifest.json is untouched.

**Why:** NGO's NetcodeIntegrationTest is only compiled by listing the package under testables, and in 2.11 it lives in Unity.Netcode.Runtime.Tests together with the package's own 672 tests — all of which would then appear in this project's Test Runner, so the suite the project is judged on stops being legible in its own window. What that harness does for the ordinary case is one NetworkManager and one transport per peer, which is roughly a hundred lines written in this project's terms, against an API that is public and stable rather than one that was already renamed once in 2.0.

**Rejected:** Adding the testables entry the ps-0 task was originally written around, and inheriting from NetcodeIntegrationTest.

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
| `[ ]` | Nothing past phase Lobby can be reached in the test harness | Every match decision is gated on the phase, and every route past Lobby runs through a networked scene load — which NetworkedFixture disables, because its peers share one editor process and one scene list. So the countdown, the round clock, the referee's verdicts, the arena's spawn points and anything conditioned on a match being under way are unreachable from a test and ship verified by hand. Twice so far: the discarded scene-event status, and ps-5. See D-019. | open |
| `[ ]` | Almost no test in the repository can fail because of a networking bug | The handshake is now covered by PlayMode tests over a real transport, which is the layer the regression that survived six merged PRs broke. Approval, the session, the roster, the fruit pickup and the avatar lifecycle followed. Reconciliation is still verified by nothing. | open |
| `[ ]` | Ambient statics are shared by peers under the test harness | Eight one-value statics — the Current pointers, PlayerLife.All, PlayerSession.All, ActivePlayers — are per-process, which is right for a shipped game and wrong for a harness that runs several peers in one editor. Tests must read NetworkManager.SpawnManager instead, and anything whose behaviour depends on one of these cannot be told apart between peers. Narrowed by ps-3: the two lookups that game code uses to cross between objects, PlayerSession.Of and SessionRoster.Of, now filter by peer (D-017). The registries themselves and the Current pointers are unchanged. | open |
| `[ ]` | Reconcile has zero test coverage | 90 lines and 9 branches of the project's headline mechanism, unreachable from EditMode because it lives in a MonoBehaviour. | open |
| `[ ]` | No byte of bandwidth has ever been measured | Every figure in the docs is derived from reading the serializers, while the profiler package is installed and its metrics already enabled. | open |
| `[ ]` | main is 55 commits behind and no build has ever been produced | A reviewer following the repository link lands on the Phase 0 scaffold. Managed stripping and scene-by-name resolution are unexercised. | open |
| `[ ]` | The debug overlay ships enabled and dominates host allocation | Its IMGUI pass is ~97% of all managed allocation on the host — 320-600 KB/s against ~12 KB/s from the entire simulation path. | open |
| `[ ]` | Peer contact is predicted against interpolated positions | Roughly 0.47 s stale at the measured RTT, so close contact between two moving players reliably corrects. Fixing it invalidates the Phase 1 measurements. | open |
| `[ ]` | Snapshots broadcast at 30 Hz while the session sits in the lobby | Bandwidth spent on a match that is not running. | open |
| `[ ]` | FruitSpawner.ServerDespawnAll has no call sites | Fruit survives into the lobby and into the next match. | open |

## Session log

- **2026-08-24** — Closed ps-5. A player who arrives while a round is being played is sat out of it by the match, through the same call running out uses — no new replicated state, and the spectator camera, the referee and the next round all worked unchanged as a result. Verified against a real join into a running sandbox: not alive, no character on any peer, and ending the only player who had actually been playing gave NoWinner instead of handing the match to the arrival. The console was not clean during that check and the noise is worth naming — scene validation failures, handle mismatches and deferred-spawn timeouts, all from the improvised second peer sharing this process's scenes, none from Snackdown. A single-peer run after it was clean. The wall this ran into is now recorded as a risk and a decision (D-019) rather than being worked around a third time in silence.
- **2026-08-24** — Fixed the thing ps-4 only observed. MatchDirector discarded the SceneEventProgressStatus that NGO's LoadScene and UnloadScene return, so a load refused because another scene event was still in flight did nothing at all — no log, no exception, and a phase left in Loading waiting on reports for a scene nobody had asked for. The load is now checked and puts the phase back to Lobby with the reason in the console; the unload is reported but not acted on, since nothing downstream waits for it. Reproduced in the sandbox before and after: same two calls in one frame, phase Loading forever versus phase Lobby and "Netcode refused to load Arena01: SceneEventInProgress".
- **2026-08-24** — Closed ps-4, and with it both workarounds the epic was written to remove: a player who is out is despawned rather than hidden, and a round spawns a character at its spawn point rather than moving one that was already standing somewhere else. The second went further than the task note expected — with nothing left to reposition, PlayerSnapshot could drop its teleport flag, which is the first time this project has taken something off the wire (D-018). Also closed a hole nobody had hit yet: the life reset lived on the way back to the lobby, a path a rematch started from the end screen never travels, so it moved onto the call that hands out a body. The death-to-despawn wiring was checked by cutting it — two of the four new tests then fail on a timeout rather than an assertion, which is what a despawn that never arrives looks like.
- **2026-08-24** — Closed ps-3. PlayerLife moved onto the session prefab and the fruit now credits a player rather than a body. Both new tests were checked by breaking what they cover: matching the pickup on any NetworkObject instead of on a character makes a fruit collect itself, and writing the fruit's kind before Spawn trips the log assertion. That second one settled a claim I had got wrong in ps-2 — a NetworkVariable written in the server's OnNetworkSpawn is already inside the spawn message, so D-015's talk of deltas was reasoning from a wrong premise and is corrected in place. The port fix from ps-2 also turned out to be half a fix: the leftover socket is not always from this run, so the harness now probes a port before binding it. Related and not fixed: the editor process is holding UDP 7777 from a Play session that ended, which is why the sandbox cannot host without an editor restart — Enter Play Mode Options has domain reload disabled, so a leaked socket outlives the session that opened it.
- **2026-08-23** — Closed ps-2. The roster stopped replicating a player list and became an index over the sessions, which deleted PlayerSlot and moved the roster out of Snackdown.Connection (D-014). The pre-spawn write ps-1 left as a follow-up turned out to be a console warning per join and was rejected rather than inherited (D-015). Adding the tests surfaced a real flake in the harness: every session bound the same UDP port, and roughly one run in three failed to bind it in whichever test ran next, so a session now takes a port of its own.
- **2026-08-23** — Closed ps-1. Three things were learned by trying: a prefab cannot be built at runtime without an internal NGO field (D-012), a networked scene object cannot be spawned by a test at all (D-013), and the ambient statics documented in docs/01 as safe because a peer is a process stop being safe the moment a harness runs two peers in one. The last one is recorded as a risk and the architecture doc now says so rather than claiming the opposite. Each new test was checked by breaking the code it covers: removing the session spawn fails exactly the four session tests and none of the handshake ones.
- **2026-08-23** — Closed ps-0 with a harness of our own after opening the testables route and finding it worse than the note in the task assumed (D-011). Four handshake tests now run over a real transport. Two things learned while writing it and worth having recorded: a NetworkManager added at runtime has no NetworkConfig at all, and Shutdown() only raises a flag — the socket closes on the next network update, so a fixture that destroys the peer in the same frame leaves the port bound and breaks the next test rather than its own.
- **2026-08-23** — Stood up this board: one JSON source, two generated views, and a rule in CLAUDE.md that closing a task, an epic or a session updates it.
- **2026-08-23** — Designed the player-session refactor and took ten decisions on it. Confirmed by inspection that the auto-spawn does not need disabling, that every host-configurable parameter is server-read only, and that the EditMode suite is untouched by the change.
- **2026-08-23** — Read the repository end to end after time away: 64 scripts, six design documents and a ten-domain audit. Conclusion recorded — the netcode is solid and measured; everything unfinished sits in the lobby, identity and configuration layer.

