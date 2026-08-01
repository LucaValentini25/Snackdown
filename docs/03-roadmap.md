# 03 — Roadmap

Phased so that **every phase ends in something demoable.** A portfolio project that's finished at
Phase 3 still looks intentional; one abandoned mid-Phase-1 looks broken. Build outward from a
working core.

| Phase | Focus | Done when… |
|:---:|---|---|
| **0** | Scaffold + docs | ✅ Clean project opens; architecture & netcode documented |
| **1** | **Netcode core** — predicted character | Two clients move fluidly under simulated 150 ms latency; reconciliation visible in overlay, not felt |
| **2** | Connection layer | Join a real match by **Relay code** or **LAN IP**, same flow; nickname via approval |
| **3** | Gameplay core | Life timer, fruit spawn/collect, head-bounce stun — all server-authoritative; win conditions |
| **4** | Extended features | Power-ups, extra map(s), scoreboard, polished spectator |
| **5** | Polish | Network debug tooling, tests, assembly split, final diagrams, a build |

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
- [ ] **Validated against NGO Network Simulator (latency + loss)** — needs two live peers; the
      prediction/reconciliation paths have not been exercised against a real remote yet

### Verified so far

Host session in `NetTest.unity`: spawn placement, collision (stops exactly at the wall face and
rests exactly on the ground surface), jump arc, and the tick/snapshot loop all behave. Plus the
property everything else depends on — **40 ticks of mixed input, simulated twice from the same
state, produce bit-identical results**. Without that, replay would be fiction.

**Acceptance:** with 150 ms simulated latency, the local player feels instant; a watcher sees smooth
remote motion; forced desyncs self-correct within a tick or two.

## Phase 2 — Connection layer

- [ ] `IConnectionProvider` interface
- [ ] `DirectProvider` (UnityTransport, host + join by IP)
- [ ] `RelayProvider` (UGS Relay + Lobby, join by code) — needs a **fresh UGS project**; the inherited one from the original project is unlinked
- [ ] `ConnectionApproval` with payload (nickname, **chosen character**, version check)
- [ ] Character select (4 Pixel Adventure skins, mechanically identical)
- [ ] Main menu → mode select → host/join → lobby, wired to the abstraction

## Phase 3 — Gameplay core

- [ ] Life timer (server-authoritative, ~1 Hz replication)
- [ ] Fruit spawner (rarity table as ScriptableObject) + networked pickup
- [ ] Head-bounce detection + stun (server-authoritative)
- [ ] Death → spectator; last-alive / timeout win conditions; end screen

## Phase 4 — Extended features

Ordered by value-per-effort; each is independently droppable.

1. [ ] **Live scoreboard** — `NetworkVariable` + events over data Phase 3 already computes. Cheapest, and it's what makes a recorded demo readable.
2. [ ] **Spectator camera polish** — follow + target switching on death; exercises late-join, already part of the model.
3. [ ] **A second arena** — mostly level design; the networked scene load comes free from Phase 3.

*Power-ups are cut* — the most netcode-interesting item here (timed authoritative effects feeding
into the predicted `Move()`), but not worth the scope against the three above.

## Phase 5 — Polish

- [ ] Network debug HUD (bandwidth, tick, RTT, reconciliation graph)
- [ ] Edit/Play-mode tests for the prediction buffer & reconciliation math
- [ ] Assembly definitions (Netcode core as a standalone assembly)
- [ ] Final architecture diagrams; a runnable build

---

### Definition of "portfolio-ready"
Phases 0–3 complete, running over Relay, with the Phase 1 netcode demonstrable under simulated latency.
Phases 4–5 make it shine but the story is already there at Phase 3.
