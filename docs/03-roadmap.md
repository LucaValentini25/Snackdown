# 03 — Roadmap

Phased so that **every phase ends in something demoable.** A portfolio project that's finished at
Phase 3 still looks intentional; one abandoned mid-Phase-1 looks broken. Build outward from a
working core.

| Phase | Focus | Done when… |
|:---:|---|---|
| **0** | Scaffold + docs | ✅ Clean project opens; architecture & netcode documented |
| **1** | **Netcode core** — predicted character | ✅ Two clients move fluidly under simulated 150 ms latency; reconciliation visible in overlay, not felt |
| **2** | Connection layer | ✅ Join a real match by **Relay code** or **LAN IP**, same flow; nickname via approval |
| **3** | Gameplay core | 🔸 Rules all in and server-authoritative: life timer, fruit, head-bounce stun, win conditions. Open: the HUD that makes the life timer visible, the character picker, and late join |
| **4** | Extended features | Scoreboard, how peer contact is predicted, extra map(s), polished spectator |
| **5** | Polish | Network debug HUD, integration tests, final diagrams, a build |

## Phase 1 — Netcode core *(the heart)*

The one phase that matters most. Everything else is scaffolding around it.

- [x] Fixed network tick wired to `NetworkTickSystem` — 30 Hz, phases ordered in `NetworkSimulationLoop`
- [x] `InputCommand` (tick, moveX, jump) sampled on the owner and sent to the server
- [x] Shared `Move()` simulation used identically by client and server — `PlayerMotor.Simulate`
- [x] Client prediction — owner applies input locally, buffers `(tick, input, state)`
- [x] `StateSnapshot` broadcast from server with `lastProcessedInputTick`
- [x] Reconciliation — rewind to snapshot, replay pending inputs
- [x] Snapshot interpolation for remote players
- [x] Debug overlay: predicted vs authoritative, reconciliation count, RTT, tick
- [x] **Validated against NGO Network Simulator (latency + loss)** — host + client under simulated
      impairment, measured and written up in [05 — Validation](05-validation.md). Under 20% packet
      loss (≈3× the worst real-world profile Unity models) the median prediction error was 0.302
      units. One scenario still lacks a recorded run; see the open items there.

### Verified so far

Host session in the Phase 1 test arena — since carved into `Bootstrap`, `Lobby` and `Arena01`, and the
scene itself deleted: spawn placement, collision (stops exactly at the wall face and rests exactly on
the ground surface), jump arc, and the tick/snapshot loop all behave. Plus the property everything
else depends on — **40 ticks of mixed input, simulated twice from the same state, produce
bit-identical results**. Without that, replay would be fiction.

One caveat on that last claim, since it is the load-bearing one: the assertion covers position and
velocity, and the test fixtures set `GroundMask = 0`, so the terrain-cast path inside `Simulate` is
not part of what was proven identical. The pure arithmetic is; the collision half is covered by
inspection and by playing.

**Acceptance:** with 150 ms simulated latency, the local player feels instant; a watcher sees smooth
remote motion; forced desyncs self-correct within a tick or two. **Met** — see
[05 — Validation](05-validation.md) for the measurements. The one caveat worth carrying forward:
remote smoothness holds at 150 ms but not at 520 ms, where the 100 ms interpolation buffer runs dry.
The predicted local player stays responsive at both.

## Phase 2 — Connection layer

- [x] `IConnectionProvider` interface — async throughout, failures as return values
- [x] `DirectProvider` (UnityTransport, host + join by IP)
- [x] `RelayProvider` (Sessions API, join by code) — the project is linked as **Snackdown** under
      Luca's organization, with Relay and Lobby enabled; verified by hosting a real session
- [x] `ConnectionApproval` with payload (nickname, chosen character, version check)
- [x] `SessionRoster` — the player list with names, skins and ready state. Landed as a replicated
      `NetworkList<PlayerSlot>`; Phase 4 replaced both with an index over the live `PlayerSession`
      objects, which is where those three fields now live
- [x] Main menu → host/join → lobby, built with **UI Toolkit**, wired to the abstraction
- [~] Character select (4 Pixel Adventure skins, mechanically identical) — **the transport half is
      done and the picker is not.** The index travels in the connection payload, is clamped by
      approval, lives on the session and dresses the sprite; but nothing sets it, so it is provably 0
      in every session and all four players render identically. Previously marked done, which was
      wrong: the plumbing landing is not the feature landing. The picker closes with Phase 3

## Phase 3 — Gameplay core

- [x] Life timer (server-authoritative, ~1 Hz replication) — drains continuously server-side,
      publishes once a second, clients drain locally between updates. 1 write/s by configuration
      (`MatchSettings.LifeReplicationHz`), plus an immediate publish on fruit pickup and on death,
      where the original wrote every frame
- [x] Fruit spawner (rarity table as ScriptableObject) + networked pickup — 8 fruit from 35% common
      to 1% legendary, worth 3s to 20s. Weights are 35%→1% by construction; the 100k-roll
      distribution check was run outside the tree, and the test that reproduces it in-repo is a
      Phase 5 item
- [x] Head-bounce detection + stun (server-authoritative) — 2s stun and a bounce, no life stolen,
      matching the original. Stun lives in PlayerState so reconciliation replays through it
- [x] Players are solid to each other — predicted client-side against past peer positions, not
      decided by the host. Also the point where `PlayerMotor` became ordered steps, so later
      mechanics are insertions rather than rewrites
- [x] Death → spectator; last-alive / timeout win conditions; end screen. Their owner gets a free
      camera, clamped per arena by `ArenaBounds`. The referee replicates a verdict, not the numbers
      behind it. Landed with the character *hidden* rather than despawned, because despawning it
      would have taken the player with it; Phase 4 separated the two and the body is now genuinely
      despawned
- [ ] **In-match HUD** — own life countdown and round clock. `PlayerLife.Fraction` and
      `RoundReferee.RoundRemaining` were written for it and are already replicated, so this is one
      `.uxml` and a controller with no netcode change. Listed here rather than in Phase 4 because the
      life timer *is* the pitch: without a readout, a viewer sees characters collecting fruit for no
      visible reason and then dying, and the timeout ending is unobservable
- [ ] **Character picker** — four buttons in the menu passing the index through
      `ConnectionRequest.Host/Join`, and `ConnectionApproval.CharacterCount` wired from
      `CharacterCatalog.Count` instead of a hardcoded 4 (today a fifth skin would be silently clamped
      away). Closes the Phase 2 item above
- [x] **Late join → spectator** — a client that connects during `Countdown`, `Playing` or `Ended`
      joins as a spectator for the round in progress and plays the next one. It used to be placed at
      the origin with a full life bar, so a latecomer could fall out of the world and still win on
      the clock. Modelled as "already out this round", which reused the whole spectator path rather
      than adding a state: the match calls the same `ServerEndRound` running out uses, and the
      camera, the referee and the next round needed no changes at all

## Phase 4 — Extended features

Ordered by value-per-effort; each is independently droppable.

1. [x] **Life bars and round clock** — not a scoreboard: this game shows each player's own draining
   life, the way the original does. Built **both** ways so the better one can be picked by looking
   at it — floating over each character (UI Toolkit world-space panels) or along the bottom in the
   fighting-game arrangement — with `F3` swapping between them at runtime. Either way it puts
   **nothing new on the wire**: every number shown already crossed the network for its own reason,
   so these are views rather than second copies of facts that can disagree with the first.
2. [ ] **Decide how peer contact is predicted** — today a client resolves contact against
   *interpolated* peer positions, so it is offset from the server by the interpolation delay plus its
   own lead, and close contact between two moving players reliably corrects. The documented options are
   to key the world buffer off authoritative snapshots and extrapolate the gap, or to keep the current
   behaviour as a stated limit. Belongs here rather than in Phase 3 because choosing well means feeling
   the difference with the HUD in, and because touching it invalidates the [Phase 1
   measurements](05-validation.md) and they would need re-running. See
   [02 — Netcode](02-netcode.md#characters-collide-with-each-other-and-it-is-predicted).
3. [x] **Spectator camera polish** — the camera follows a survivor on death and a tap left or right
   moves to the next, in the same order the strip along the bottom shows. Free panning is what is left
   when nobody is alive to follow. The choice of who to watch is a [`SpectatorTargetRing`](../Assets/_Project/Scripts/Gameplay/Match/SpectatorTargetRing.cs)
   the camera owns but does not contain, so what happens when the watched player dies is a unit test
   rather than something you would need a four-player match to see.
4. [ ] **A second arena** — mostly level design; the networked scene load comes free from Phase 3, and
   `ArenaCatalog` plus the `ArenaBounds` camera clamp are already in place, so this is authoring only.

*Power-ups are cut* — the most netcode-interesting item here (timed authoritative effects feeding
into the predicted `Move()`), but not worth the scope against the four above.

## Phase 5 — Polish

- [ ] Network debug HUD (bandwidth, tick, RTT, reconciliation graph). Bandwidth is the gap that
      matters: `com.unity.multiplayer.tools` is already a dependency and `NetworkMessageMetrics` is
      already enabled in `Bootstrap`, yet **no byte has ever been counted in this project** — every
      bandwidth figure in the docs is derived from reading the serializers
- [x] ~~Edit-mode tests for the simulation, prediction buffer and interpolator~~ — **done early**;
      they needed no refactor and could have been written from day one. 36 cases at the time this
      line was written, and the suite has roughly doubled since — the number is deliberately not
      restated here, because it drifted once already
- [x] ~~Assembly definitions~~ — **done early**, one per system. Deferring them was the mistake:
      an assembly is created when a system is, or retrofitting becomes a migration. Doing it here
      is what made the `Netcode ↔ Gameplay` cycle visible at all. See
      [ADR 0001](adr/0001-decoupling-the-netcode-layer.md), whose *Context* predicted this could not
      be done and was overtaken two commits later
- [~] **Integration tests** — the harness landed early and elsewhere, with the player-session epic
      rather than here, because that refactor is not safe to attempt without it: `NetworkedFixture`
      stands up a host and *n* clients over a real transport inside one Play mode session, and the
      handshake is covered. What is still owed to this bullet is the rest of it — that the owner and
      the server converge after a scripted input sequence, and that a dropped snapshot leaves no
      permanent offset
- [x] **Fruit distribution test** — a seeded 100k-roll check, so the claim in Phase 3 is true of the
      repository and not only of a session that happened once
- [ ] Final architecture diagrams; a runnable build. No player build has ever been produced, so
      managed stripping, scene-by-name resolution and two separate processes on two machines are all
      unexercised
- [ ] **WebGL** — a future phase, not a current target. Two known blockers: `UnityTransport` needs
      WebSockets and Relay needs `wss`, neither of which is configured; and TCP turns both
      deliberately-unreliable channels into reliable-ordered ones with head-of-line blocking, which is
      the exact failure the wire design avoids. Worth documenting as a limit even if it is never built

---

### Definition of "portfolio-ready"
Phases 0–3 complete, running over Relay, with the Phase 1 netcode demonstrable under simulated latency.
Phases 4–5 make it shine but the story is already there at Phase 3.

One clarification learned the hard way: **a rule being server-authoritative is not the same as the rule
being visible.** Phase 3's mechanics were all implemented and correct while the timer they revolve
around had no readout anywhere on screen, which made the phase complete on paper and undemoable in
practice. "Demoable" now means someone watching can see why what happens is happening.

Releases are a Phase 5 concern — see [04 — Workflow](04-workflow.md#releases).
