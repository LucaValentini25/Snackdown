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
| Engine | Unity **6000.3.14f1**, URP (2D Renderer) |
| Netcode | **Netcode for GameObjects 2.11** |
| Transport | Unity Transport + **Relay / Lobby** (Unity Gaming Services) |
| Input | New Input System (legacy Input Manager disabled) |
| Art | *Pixel Adventure* + *DEVNIK 2D UI* (third-party) |
| Topology | **Host** (listen server), up to **4 players** |
| Network tick | **30 Hz**, decoupled from render framerate |

## 🌐 Netcode highlights — *the reason this project exists*

- **Server-authoritative movement.** No client is trusted with its own position.
- **Client-side prediction.** The local player responds instantly, with zero perceived input lag.
- **Server reconciliation.** On a mismatch, the client rewinds to the server's tick and replays pending inputs.
- **Snapshot interpolation** for remote players — smooth motion on top of a discrete tick stream.
- **Pluggable connection layer** — identical join flow whether you're on LAN or across the internet via Relay.
- **Connection approval** with a payload (nickname, version check) instead of post-connect hacks.

See **[docs/02-netcode.md](docs/02-netcode.md)** for the model in depth.

## 🚀 Running it

1. Open `Assets/_Project/Scenes/Bootstrap.unity` — the first scene in Build Settings — and press
   Play. Bootstrap holds no visible content of its own; it carries the connection, the roster and
   the match director, and the menu is loaded on top of it.
2. Type a name and hit **Host a game**. The lobby shows a six-character join code to share.
3. For a second peer, use **Multiplayer Play Mode** (`Window > Multiplayer > Multiplayer Play Mode`),
   enable a virtual player, and in that window enter the code and hit **Join a game**.
4. Both players hit **Ready**; the host hits **Start match**. Move with `A`/`D` or the arrows, jump
   with `Space`. Collect fruit, stomp heads, outlive the others.

**To play over a LAN instead of Relay:** in `Lobby.unity`, uncheck **Use Relay** on the
`MainMenuController`. The address field relabels itself from *Code* to *Address* and everything else
in the flow is identical — which is the whole point of the connection layer.

To see the netcode do its job, open `Window > Multiplayer > Network Simulator`, apply ~150 ms of
latency and some packet loss, then use the overlay:

| Key | Effect |
|---|---|
| `F1` | Toggle client-side prediction — off is what the game would feel like without it |
| `F2` | Toggle visual smoothing — off shows every raw correction |
| `F3` | Hide the overlay |
| `F4` | Export the client's run to CSV — correction rate, error, replayed ticks |

The red ghost is where the server says you are; the green box is where you predicted you'd be.

For the measured results and the procedure that produced them, see
**[docs/05 — Validation](docs/05-validation.md)**.

## 📚 Documentation

- **[00 — Legacy analysis](docs/00-legacy-analysis.md)** — what the original university project was, what it got right, and what this rebuild fixes.
- **[01 — Architecture](docs/01-architecture.md)** — layers, folder structure, authority rules.
- **[02 — Netcode design](docs/02-netcode.md)** — tick loop, prediction, reconciliation, interpolation.
- **[03 — Roadmap](docs/03-roadmap.md)** — phased plan, each phase independently demoable.
- **[04 — Git workflow](docs/04-workflow.md)** — branching model, PRs, releases, Unity merge setup.
- **[05 — Validation](docs/05-validation.md)** — how the netcode was measured under simulated latency and packet loss, and what the numbers were.
- **[ADR 0001](docs/adr/0001-decoupling-the-netcode-layer.md)** — a decoupling that was designed, costed and then *not* done, kept for the NGO codegen constraint it established by experiment.
- **[Audit](docs/audit/99-synthesis.md)** — a ten-domain read of the project against its own claims: what holds, what drifted, and the numbers behind both.

## 🧪 Tests

EditMode tests over the parts that can be tested without the engine running: the shared simulation
step and its replay determinism, the prediction ring, the snapshot interpolator, predicted peer
collision, the stun, the fruit rarity table, arena clamping and nickname sanitation. They need no
scene, no `NetworkManager` and no Play mode, which is a property of the design rather than of the
tests — see [docs/01](docs/01-architecture.md).

Stated plainly because it matters: **what they do not yet cover is anything involving two peers.**
There is no PlayMode or integration test, so no test in this repository can fail because of a
networking bug. That gap is [tracked in the roadmap](docs/03-roadmap.md), not hidden.

## 📈 Status

🚧 **In development.** Phases 0–3 are in: the netcode core (predicted character over a fixed 30 Hz
tick, with reconciliation, interpolation and measured validation under simulated latency), the
connection layer (join by Relay code or LAN address, same flow, with approval at the door), and the
gameplay core (life timer, fruit, head-bounce stun, death to spectator, win conditions, end screen).

Phase 4 is next. Targeting **PC**; WebGL is a documented future phase and needs transport work
before it can connect at all. See the [roadmap](docs/03-roadmap.md).
