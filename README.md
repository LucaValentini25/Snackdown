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
2. Type a name and hit **Host a game**. The lobby shows a six-character join code to share. The
   game is also listed publicly, so anybody can find it without the code.
3. For a second peer, use **Multiplayer Play Mode** (`Window > Multiplayer > Multiplayer Play Mode`),
   enable a virtual player, and in that window hit **Join a game**. That opens the browser: pick the
   game out of the list, or type the code and hit **Join**.
4. The host picks the arena in the lobby's **Rules** panel; everyone sees which one is coming. Both
   players hit **Ready**; the host hits **Start match**. Move with `A`/`D` or the arrows, jump
   with `Space`. Collect fruit, stomp heads, outlive the others. When you go out the camera follows
   a survivor — tap left or right to watch somebody else; the strip along the bottom outlines who
   you are on.
5. `Escape` — or `Start` on a pad — opens the pause menu, which is where you leave a match or close
   the game. It does not pause anything: there is nothing to pause when the other players keep
   moving, so your character stays under your control while it is open. The front screen has its own
   **Quit game**, which is the only way out of a build.

**To play over a LAN instead of Relay:** in `Bootstrap.unity`, uncheck **Use Relay** on the
`SessionConnection`. The field on the join screen relabels itself from *Code* to *Address*, the
browser disappears because a LAN socket has no directory behind it, and everything else in the flow
is identical — which is the whole point of the connection layer.

To see the netcode do its job, open `Window > Multiplayer > Network Simulator`, apply ~150 ms of
latency and some packet loss, then use the overlay:

| Key | Effect |
|---|---|
| `F1` | Toggle client-side prediction — off is what the game would feel like without it |
| `F2` | Toggle visual smoothing — off shows every raw correction |
| `F3` | Hide the overlay |
| `F4` | Export the client's run to CSV — correction rate, error, replayed ticks |
| `F5` | Swap where rival positions come from when predicting contact — restarts the recording |

The red ghost is where the server says you are; the green box is where you predicted you'd be.

> **None of this exists in a released build.** The overlay, the ghost and the CSV recorder are the
> most expensive thing in the project — the overlay's IMGUI pass alone was measured at ~97% of the
> host's managed allocation — and they are there to explain the netcode, not to play with. They run
> in the editor and in **development builds**, which is what a build handed to somebody to try
> should be. A release build drops them entirely.

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

Alongside them, PlayMode tests that need two peers: a host and one or more clients, each with its
own `NetworkManager`, handshaking over a real transport inside a single Play mode session. They are
the first thing in this repository that can fail because of a networking bug.

Stated plainly because it matters: **what they cover so far is the handshake.** Everything past it —
approval, spawning, replication, reconciliation — is still verified by nothing. That gap is
[tracked in the roadmap](docs/03-roadmap.md), not hidden.

## 📈 Status

🚧 **In development.** Phases 0–3 are in: the netcode core (predicted character over a fixed 30 Hz
tick, with reconciliation, interpolation and measured validation under simulated latency), the
connection layer (join by Relay code or LAN address, same flow, with approval at the door), and the
gameplay core (life timer, fruit, head-bounce stun, death to spectator, win conditions, end screen).

Phase 4 is next. Targeting **PC**; WebGL is a documented future phase and needs transport work
before it can connect at all. See the [roadmap](docs/03-roadmap.md).

## 🎨 Credits

Character, terrain, item and background art: **[Pixel Adventure 1](https://pixelfrog-assets.itch.io/pixel-adventure-1)**
by [Pixel Frog](https://pixelfrog-assets.itch.io/), released under **CC0** — public domain, no
attribution required. Credited anyway, because taking credit for someone else's pixel art in a
portfolio is not a thing worth doing.

The four playable characters are the pack's Mask Dude, Ninja Frog, Pink Man and Virtual Guy. They
are cosmetic only: nothing in the simulation reads which one you picked, which is what makes them
mechanically identical rather than merely intended to be.

Interface art: **[Complete UI Essential Pack](https://crusenho.itch.io/complete-ui-essential-pack)**
by **Crusenho Agus Hennihuno**, released under
**[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)**. Only the Flat theme's individual
sprites are bundled. *Changes made:* re-imported for this project — point filtering, no compression,
full-rect meshes — and the nine-slice borders are declared in
[`Snackdown.uss`](Assets/_Project/UI/Snackdown.uss) rather than baked into the sprites.

Interface font: **[BoldPixels](https://yukipixels.itch.io/boldpixels)** by **Yūki
([@YukiPixels](https://linktr.ee/yukipixels))**, released under
**[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)**. *Changes made:* the `.otf` is
unmodified; `BoldPixels SDF.asset` is generated from it by TextMesh Pro and is therefore an
adaptation, so **that file carries CC BY-SA 4.0 and not the MIT grant below**. Using a font to draw
text does not make the thing drawing it a derivative, so nothing else here is affected.

**Every pack here is redistributable, and that is why they are bundled rather than linked.** The Unity Asset
Store was excluded from this project before any pack was looked at: its EULA forbids redistributing
an asset, and committing one to a public repository is redistribution. The usual answer — gitignore
the art and tell people where to buy it — hands anyone who clones this a project that does not open.

Everything else in this repository — every script, test, scene, prefab and document — is original.

## 📄 License

Source code, tests and documentation: **[MIT](LICENSE)**.

The bundled art is covered by its own terms, above, not by the MIT grant. That distinction is why
nothing under a licence that forbids redistribution lives in this repository: an asset you cannot
publish does not belong in a repository whose point is being read.

