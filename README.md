# Snackdown 🍓

> **Working title.** A 2D multiplayer platformer built in **Unity 6** with **Netcode for GameObjects**.
> A portfolio project whose whole point is **multiplayer programming done right**.

This is a ground-up rebuild of a university assignment ([original analysis](docs/00-legacy-analysis.md)),
re-architected to demonstrate the fundamentals that actually matter in networked games:
**server authority, client-side prediction, server reconciliation, and snapshot interpolation** —
behind a connection layer that works both over **LAN** and the **internet (Unity Relay)**.

---

## 🎮 Gameplay

Last-player-standing survival. Your **life is a countdown timer** that drains on its own.

- **Collect fruit** scattered around the arena — rarity-weighted, each adds time to your timer.
- **Stomp rivals' heads** to stun them and buy yourself an edge.
- **Outlive everyone.** Last one alive wins; if the round timer runs out, most life left wins.

## 🛠️ Tech stack

| Area | Choice |
|------|--------|
| Engine | Unity **6000.2.6f2**, URP (2D) |
| Netcode | **Netcode for GameObjects 2.4** |
| Transport | Unity Transport + **Relay / Lobby** (Unity Gaming Services) |
| Input | New Input System |
| Art | *Pixel Adventure* + *DEVNIK 2D UI* (third-party) |

## 🌐 Netcode highlights — *the reason this project exists*

- **Server-authoritative movement.** No client is trusted with its own position.
- **Client-side prediction.** The local player responds instantly, with zero perceived input lag.
- **Server reconciliation.** On a mismatch, the client rewinds to the server's tick and replays pending inputs.
- **Snapshot interpolation** for remote players — smooth motion on top of a discrete tick stream.
- **Pluggable connection layer** — identical join flow whether you're on LAN or across the internet via Relay.
- **Connection approval** with a payload (nickname, version check) instead of post-connect hacks.

See **[docs/02-netcode.md](docs/02-netcode.md)** for the model in depth.

## 🚀 Running it

> Instructions land as the connection layer is built (Phase 2). For now this is a work in progress.

## 📚 Documentation

- **[00 — Legacy analysis](docs/00-legacy-analysis.md)** — what the original university project was, what it got right, and what this rebuild fixes.
- **[01 — Architecture](docs/01-architecture.md)** — layers, folder structure, authority rules.
- **[02 — Netcode design](docs/02-netcode.md)** — tick loop, prediction, reconciliation, interpolation.
- **[03 — Roadmap](docs/03-roadmap.md)** — phased plan, each phase independently demoable.
- **[04 — Git workflow](docs/04-workflow.md)** — branching model, PRs, releases, Unity merge setup.

## 📈 Status

🚧 **In development.** Phase 0 (scaffold + docs) complete. See the [roadmap](docs/03-roadmap.md).
