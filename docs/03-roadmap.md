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

- [ ] Fixed network tick wired to `NetworkTickSystem`
- [ ] `InputCommand` (tick, moveX, jump) sampled on the owner and sent to the server
- [ ] Shared `Move()` simulation used identically by client and server
- [ ] Client prediction — owner applies input locally, buffers `(tick, input, state)`
- [ ] `StateSnapshot` broadcast from server with `lastProcessedInputTick`
- [ ] Reconciliation — rewind to snapshot, replay pending inputs
- [ ] Snapshot interpolation for remote players
- [ ] Debug overlay: predicted vs authoritative, reconciliation count, RTT, tick
- [ ] Validated against NGO Network Simulator (latency + loss)

**Acceptance:** with 150 ms simulated latency, the local player feels instant; a watcher sees smooth
remote motion; forced desyncs self-correct within a tick or two.

## Phase 2 — Connection layer

- [ ] `IConnectionProvider` interface
- [ ] `DirectProvider` (UnityTransport, host + join by IP)
- [ ] `RelayProvider` (UGS Relay + Lobby, join by code)
- [ ] `ConnectionApproval` with payload (nickname, version check)
- [ ] Main menu → mode select → host/join → lobby, wired to the abstraction

## Phase 3 — Gameplay core

- [ ] Life timer (server-authoritative, ~1 Hz replication)
- [ ] Fruit spawner (rarity table as ScriptableObject) + networked pickup
- [ ] Head-bounce detection + stun (server-authoritative)
- [ ] Death → spectator; last-alive / timeout win conditions; end screen

## Phase 4 — Extended features

- [ ] Power-ups (networked spawn + timed effects)
- [ ] A second arena
- [ ] Live scoreboard
- [ ] Spectator camera polish

## Phase 5 — Polish

- [ ] Network debug HUD (bandwidth, tick, RTT, reconciliation graph)
- [ ] Edit/Play-mode tests for the prediction buffer & reconciliation math
- [ ] Assembly definitions (Netcode core as a standalone assembly)
- [ ] Final architecture diagrams; a runnable build

---

### Definition of "portfolio-ready"
Phases 0–3 complete, running over Relay, with the Phase 1 netcode demonstrable under simulated latency.
Phases 4–5 make it shine but the story is already there at Phase 3.
