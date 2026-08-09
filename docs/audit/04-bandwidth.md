# Bandwidth & Serialization Audit

Auditor **A4**. Read-only. Target: `d:\Unity Projects\Snackdown`, branch `dev`, commit `10a2a13`.

## Verdict

The wire surface is exactly what recon predicted and nothing more: **one unreliable 18-byte input RPC
up and one unreliable 176-byte snapshot RPC down, per tick, plus a handful of low-rate
`NetworkVariable` writes** — and I verified the prefabs directly, **there is no `NetworkTransform`,
`NetworkRigidbody2D` or `NetworkAnimator` anywhere in `Assets/`**, so there is no double-syncing.
Every replicated type implements `INetworkSerializable` by hand; nothing falls back to reflection;
there is no string, no `Quaternion`, no large struct in a `NetworkVariable`, and no per-frame
replicated value — the two places where a naive implementation would have put one (`PlayerLife`,
the two countdowns) explicitly avoid it and say why. The design is frugal and the reasoning is
written down. What is missing is the other half of the project's own thesis: **nobody ever measured
it.** `RunRecorder` exports prediction error, replayed ticks and RTT, and no bytes at all; there is
no profiler capture, no `RuntimeNetStatsMonitor`, no bandwidth line in the overlay — while
`com.unity.multiplayer.tools` 2.2.8 is installed and `NetworkMessageMetrics`/`NetworkProfilingMetrics`
are already switched on in `NetworkConfig`. The one number the code does state — "a single ~120 byte
datagram" — is off by ~46 % against the payload the struct actually produces. The real constraint,
if Mobile stays a target platform, is the **host uplink**: ~209 kbps at 4 players over Relay, 82 % of
a 256 kbps budget, growing as O(N²) and unmeasured.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Wire-format efficiency (bytes per field) | 4 | Hand-written `INetworkSerializable` everywhere, 6-byte input, no strings on hot paths. Loses a point for a raw 8-byte `NetworkObjectId` per player per tick and 12 owner-only bytes broadcast to everyone. |
| Message design & delivery model | 5 | Unreliable both ways with an explicit redundancy window instead of retransmission; one frame per tick rather than one message per player; deadlines instead of replicated counters. Each choice is documented with the failure it prevents. |
| Replication discipline (`NetworkVariable` hygiene) | 5 | 12 `NetworkVariable`s, all primitives or enums, all event-rate or ≤1 Hz. `EnsureNetworkVariableLengthSafety` off. No per-frame writes exist. |
| Measurement & evidence | 1 | Zero bytes ever measured. The profiler that would do it ships in an installed package and metrics are enabled. A documented byte figure is wrong. |
| Scaling shape & platform fit | 2 | O(N²) host uplink is inherent to the (settled) topology and fine at 4 — but Mobile and WebGL are stated targets, the host sits at 82 % of a mobile budget, and WebGL forces the one delivery mode this design is built to avoid. |

## Findings

### F-A4-1 — No bandwidth was ever measured, in a project whose thesis is measured netcode

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/RunRecorder.cs:26-33` (the `Sample` struct: `Time`,
  `Error`, `ReplayedTicks`, `MeasuredRttMs`, `TransportRttMs` — no byte counters);
  `Assets/_Project/Scripts/Netcode/RunRecorder.cs:113` (the CSV column header, same five fields);
  `Assets/_Project/Scripts/UI/NetDebugOverlay.cs:133-192` (the whole overlay — tick, rate, RTT,
  peers, snapshots *sent as a count*, corrections, error, replayed ticks; no bytes);
  `docs/05-validation.md:66-88` (the results table: corrections, error, replayed ticks, RTT — no
  bandwidth row); `docs/03-roadmap.md:88` (`- [ ] Network debug HUD (bandwidth, tick, RTT,
  reconciliation graph)` — unchecked, Phase 5);
  `Assets/_Project/Scenes/Bootstrap.unity:465-466` (`NetworkMessageMetrics: 1`,
  `NetworkProfilingMetrics: 1` — already on);
  `Packages/manifest.json` → `com.unity.multiplayer.tools` **2.2.8** (ships the Network Profiler and
  `RuntimeNetStatsMonitor`); `git ls-files` returns no `.csv`, no profiler capture, no metrics file
  anywhere in the repo.
- **What it is**: The measurement harness that exists (`RunRecorder`, `F4` export, the procedure in
  `docs/05-validation.md`) records prediction accuracy only. No bandwidth figure exists in the code,
  the overlay, the docs or the repository. `NetworkSimulationLoop.SnapshotsSent` counts messages, not
  bytes. The package that would answer the question in one Play-mode session is already a dependency,
  and the two `NetworkConfig` flags it needs are already enabled.
- **Why it matters**: The project's stated purpose is "multiplayer programming done right", and
  `docs/05-validation.md` is built around the principle that a claim without the conditions that
  produced it "is not a result, it is a number". Bandwidth is the second axis every netcode
  interviewer asks about after latency, `docs/02-netcode.md:35` makes an explicit bandwidth argument
  ("30 Hz … halves bandwidth"), and there is nothing to back either with. It is also how F-A4-2 went
  unnoticed. The cost of closing it is one Play-mode session with the Network Profiler open.
- **Recommendation**: Run one 4-player (or host+1) session with the Multiplayer Tools Network
  Profiler recording, and add a bandwidth row to the `docs/05-validation.md` results table under the
  same "conditions" discipline the rest of the file already enforces. Optionally add
  `RuntimeNetStatsMonitor` to the debug scene, which is the Phase 5 HUD item already on the roadmap.
- **Effort**: S

### F-A4-2 — The one snapshot size stated in the code is wrong by ~46 %

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:55-56` — *"with four players that's
  a single **~120 byte** datagram instead of four"*. Counted against
  `SnapshotFrame.NetworkSerialize` (`SnapshotFrame.cs:63-73`),
  `PlayerSnapshot.NetworkSerialize` (`SnapshotFrame.cs:41-47`) and
  `PlayerState.NetworkSerialize` (`Assets/_Project/Scripts/Simulation/PlayerState.cs:40-48`),
  the payload at 4 players is **176 bytes**, and the datagram it is claimed to describe is
  **242 bytes** direct / **280 bytes** relayed. Full arithmetic in
  [Quantified Estimates](#quantified-estimates).
- **What it is**: `PlayerSnapshot` is 42 bytes (`ulong` 8 + `PlayerState` 29 + `uint` 4 + `bool` 1),
  and `SnapshotFrame` adds `uint Tick` 4 + `int count` 4. 4 + 4 + 4×42 = 176 bytes of payload, before
  NGO's RPC metadata, message header and batch header and before any transport framing. 120 bytes
  would be right for roughly 2.7 players, or for a `PlayerState` without the three feel timers.
- **Why it matters**: The XML remarks are, by this repo's own rules (`CLAUDE.md` — "that reasoning is
  the deliverable of this project"), part of what is being judged. A reviewer who counts the fields —
  which is a five-minute exercise and exactly the kind of thing a netcode interviewer does — finds
  the number does not survive contact with the struct beneath it. The argument the sentence is making
  (one datagram, not four) is completely correct and does not need the wrong number to stand up.
- **Recommendation**: Replace "~120 byte" with the counted figure and say which layer it refers to —
  e.g. "176 bytes of payload, ~240 on the wire". Best done in the same commit as F-A4-1 so the
  number comes from a measurement rather than from another estimate.
- **Effort**: S

### F-A4-3 — WebGL is a stated target platform, and it removes the delivery mode the whole wire design rests on

- **Severity**: Major
- **Type**: Scalability
- **Confidence**: Medium
- **Evidence**: `target_platforms: ["PC (Windows)", "Mobile", "WebGL"]` (project context);
  `Assets/_Project/Scenes/Bootstrap.unity:408` — `m_UseWebSockets: 0`;
  `Assets/_Project/Scripts/Netcode/NetworkSimulationLoop.cs:119` and
  `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:380` — both channels are
  `RpcDelivery.Unreliable`;
  `Library/PackageCache/com.unity.netcode.gameobjects@60c1d83693e8/Runtime/Transports/UTP/UnityTransport.cs:664-665`
  — `NetworkDelivery.Unreliable` maps to `m_UnreliableFragmentedPipeline`;
  `Library/PackageCache/com.unity.transport@ed7eca02732f/Runtime/WebSocketNetworkInterface.cs` and
  `Runtime/TCPNetworkInterface.cs` — the WebGL path is WebSocket over TCP;
  the design rationale that this breaks:
  `NetworkSimulationLoop.cs:114-118` ("Reliability would be actively harmful here"),
  `Assets/_Project/Scripts/Simulation/InputPacket.cs:10-13` ("Retransmission would be worse"),
  `docs/02-netcode.md:163-166`.
- **What it is**: A WebGL build cannot open a UDP socket. `UnityTransport` must run over WebSockets,
  which is TCP: every packet is delivered, in order, with retransmission and head-of-line blocking.
  The `RpcDelivery.Unreliable` attribute still compiles and still selects the unreliable NGO
  pipeline, but the transport underneath it no longer offers unreliability, so a lost snapshot
  becomes a stall that delays every snapshot behind it — precisely the failure mode the two design
  comments above say the project is avoiding. The flag is also currently off, so nothing works over
  WebGL today regardless.
- **Why it matters**: If WebGL stays on the target list, then the single most-defended decision in
  the netcode layer silently does not hold there, and the project would be shipping a build whose
  behaviour contradicts its own documentation — the exact category the audit brief calls a docs
  claim the code does not implement. Confidence is Medium rather than High because I verified the
  transport's WebSocket/TCP path from package source but could not run a WebGL build to observe the
  degradation.
- **Recommendation**: Pick one and write it down. Either (a) drop WebGL from the target platform list
  in the README and `docs/`, which costs nothing and is the honest answer for a 15-day project; or
  (b) keep it and add a paragraph to `docs/02-netcode.md` stating that the WebGL build runs over TCP,
  that unreliable delivery degrades to reliable-ordered there, and what that does to snapshot
  interpolation under loss. Option (b) is arguably the *better* portfolio answer — knowing the limit
  of your own design is worth more than the design.
- **Effort**: S (document) / L (actually support and validate WebGL)

### F-A4-4 — `NetworkObjectId` is sent as a raw 8-byte `ulong` per player per tick

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:10` and `:43` —
  `public ulong NetworkObjectId;` / `serializer.SerializeValue(ref NetworkObjectId);`, which is an
  unmanaged 8-byte write. Contrast NGO's own handling of the same field:
  `Library/PackageCache/com.unity.netcode.gameobjects@60c1d83693e8/Runtime/Messaging/Messages/RpcMessages.cs:10`
  — `BytePacker.WriteValueBitPacked(writer, metadata.NetworkObjectId)`, which costs 1 byte for ids
  below 16 (`Runtime/Serialization/BytePacker.cs:452-472`).
- **What it is**: 8 of the 42 bytes in every `PlayerSnapshot` — **32 of the 176-byte payload, 18 %** —
  are an object id that in this game never exceeds a two-digit number.
- **Why it matters**: It is the single largest cheap saving in the packet, and the fix is one line.
  Bit-packing it (or, cheaper still, sending a `byte` slot index and resolving it against the
  frame-stable player list) takes the 4-player payload from 176 to 148 bytes, ~16 %, which is ~2.5
  kB/s off the host uplink at 30 Hz. It is also the kind of detail an interviewer notices, in a file
  whose whole purpose is to be read.
- **Recommendation**: `BytePacker.WriteValueBitPacked` / `ByteUnpacker.ReadValueBitPacked` on
  `NetworkObjectId` inside `PlayerSnapshot.NetworkSerialize`. Note this makes the struct's size
  variable, so any future assumption of a fixed stride has to go — there is none today.
- **Effort**: S

### F-A4-5 — Reconciliation-only fields are broadcast to peers that never read them

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Simulation/PlayerState.cs:22-35,40-48` — `CoyoteTimer`,
  `JumpBufferTimer` and `StunTimer` are three `float`s, 12 of the struct's 29 bytes;
  `Assets/_Project/Scripts/Netcode/SnapshotInterpolator.cs:88-91` — the remote render path copies the
  sample and then only interpolates `Position`, `Velocity` and `Grounded`;
  `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:475-481` — a peer either reconciles
  (owner) or interpolates (remote), never both.
- **What it is**: The three timers exist so that a *rewind* resumes with the right values — the
  reasoning in `PlayerState.cs:9-15` is correct and I would not remove them from the struct. But only
  the owning client rewinds. Each client receives 4 entries and reconciles against exactly one of
  them; for the other 3 the 12 timer bytes are never read. That is 36 of 176 payload bytes per
  snapshot, **20 %**, and 3 × 36 × 30 = 3.2 kB/s of the host's uplink.
- **Why it matters**: It is real waste with a real number attached, and the honest counter-argument
  is also real: eliminating it means the server no longer sends one frame but one frame *per
  recipient*, which trades a documented simplicity (`SnapshotFrame.cs:50-57`, "the world, once per
  tick") for four serializations instead of one and a shared timestamp that stops being trivially
  shared. At 4 players and 25 kB/s that trade is probably not worth taking, and this finding is
  logged so the author can say so deliberately rather than not having considered it.
- **Recommendation**: Leave as is, and add one sentence to `SnapshotFrame`'s remarks acknowledging
  the 20 % and why one frame for everyone still wins. If bandwidth ever does become the constraint,
  the cheap version is a per-entry flag: serialize the timers only for the entry whose
  `NetworkObjectId` matches the recipient's owned object.
- **Effort**: S (document) / M (implement per-recipient frames)

### F-A4-6 — No delta or dirty check: an idle or eliminated player costs the same as a running one

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/NetworkSimulationLoop.cs:100-112` — `BroadcastSnapshot`
  builds an entry for every registered peer unconditionally;
  `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:457-469` — `BuildSnapshot` has no
  change test; `PredictedPlayer.cs:422-424` — `ServerSimulateTick` returns early when
  `!ShouldSimulate`, so an eliminated player's state is provably frozen and still shipped 30 times a
  second; `PredictedPlayer.cs:129-147` — `ShouldSimulate` is false for a dead player and outside
  Countdown/Playing.
- **What it is**: Full-state replication with no baseline, no dirty flag and no per-player skip.
  A player standing still, a player in the lobby phase, and a player eliminated ten seconds ago each
  cost 42 bytes per tick, forever, to every client.
- **Why it matters**: In the last-player-standing endgame — the phase the demo video will show — up
  to 3 of 4 entries are frozen corpses, so up to 75 % of the snapshot is a state that has not changed
  since the player died. At 25 kB/s of host uplink this is not urgent, but it interacts with F-A4-8:
  the fix is also the cheapest reduction available to the host's mobile budget. The reason not to
  do full delta compression is sound and worth stating: deltas against a baseline require the server
  to know which baseline each client actually received, which means acks, which is exactly the
  reliability machinery `NetworkSimulationLoop.cs:114-118` argues against.
- **Recommendation**: Not delta compression — a per-entry skip. Omit players for whom
  `ShouldSimulate` is false and who have already been announced as such (the `IsTeleport` field
  already establishes the "repeat a flag N times because delivery is unreliable" pattern at
  `PredictedPlayer.cs:101-111`, and the same trick applies). Remote peers already hold the last
  sample and hold it steady when the buffer runs out (`SnapshotInterpolator.cs:73-77`), so a missing
  entry renders correctly with no extra work.
- **Effort**: S

### F-A4-7 — Every received snapshot allocates a new array, 30 times a second, on every client

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:70` —
  `if (serializer.IsReader) Players = new PlayerSnapshot[count];`. Compare the send side, which is
  explicitly pooled: `Assets/_Project/Scripts/Netcode/NetworkSimulationLoop.cs:45,104-108`
  (`_snapshotScratch`, reallocated only when the player count changes).
- **What it is**: The deserialization path allocates a fresh `PlayerSnapshot[4]` — 4 × 42 bytes of
  payload in a managed array, ~192 bytes with header — per snapshot per client. At 30 Hz that is
  ~5.8 kB/s of pure garbage on every client, ~350 kB per minute, for an object that lives for the
  duration of one method call.
- **Why it matters**: On PC it disappears into gen-0. On Mobile — a stated target — a steady 30 Hz
  allocation stream is exactly what produces periodic GC hitches in an otherwise smooth frame, and
  it lands in the one code path that must not stutter. It is also a visible inconsistency: the
  author pooled the send side deliberately and commented on it, and the receive side does the
  opposite two files away.
- **Recommendation**: Keep a static/instance scratch array on the receiving side and reuse it when
  `count` matches, mirroring `_snapshotScratch`. The frame is consumed synchronously inside
  `SnapshotRpc` (`NetworkSimulationLoop.cs:120-136`) and never retained, so reuse is safe.
- **Effort**: S

### F-A4-8 — Host uplink sits at 82 % of a 256 kbps mobile budget at 4 players, uncapped and unmeasured

- **Severity**: Minor
- **Type**: Scalability
- **Confidence**: Medium
- **Evidence**: Arithmetic in [Quantified Estimates](#quantified-estimates), derived from
  `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:41-73`,
  `Library/PackageCache/com.unity.netcode.gameobjects@60c1d83693e8/Runtime/Messaging/RpcTargets/NotServerRpcTarget.cs:16-45`
  and `.../Runtime/Messaging/NetworkMessageManager.cs:637-700` (one datagram per client, not a
  broadcast), `.../com.unity.transport@ed7eca02732f/Runtime/Relay/Messages/RelayMessageRelay.cs:9`
  (38-byte Relay header). `target_platforms` includes Mobile.
- **What it is**: Over Relay at 4 players the host uploads **~25.2 kB/s ≈ 202 kbps** of snapshot
  traffic (~209 kbps including `NetworkVariable` deltas). A 256 kbps budget leaves ~18 % of headroom,
  and typical mobile *uplink* is the scarcer direction. There is no send-rate cap, no adaptive
  throttle and no bandwidth telemetry that would show the ceiling being approached.
- **Why it matters**: This is **not** an argument against the host topology, which is settled — a
  dedicated server would move the same bytes. It is a statement about which node is the bottleneck
  and on which platform: a mobile *client* is comfortable (≈ 70 kbps down, 29 kbps up), a mobile
  *host* is not. The practical consequence is a product decision — whether the mobile build is
  allowed to host at all — that is currently unmade because the number was never computed.
- **Recommendation**: Compute it for real (F-A4-1), write the figure into `docs/02-netcode.md`'s
  "what replicates how" table, and decide explicitly whether a mobile build offers Host or Join-only.
  F-A4-4 and F-A4-6 together take the host uplink to roughly 150–170 kbps with no design change,
  which restores real headroom if hosting on mobile is wanted.
- **Effort**: S

## Over-engineering check (this domain)

Applying the shared rubric to serialization and the wire format specifically: **zero hits.** There is
no custom serializer replacing NGO's, no generic/factory layer over message construction, no
configuration switch for a send rate that never changes, no speculative compression stage, no
delta/baseline machinery built for a player count that does not exist. Every struct on the wire is a
plain `INetworkSerializable` with a hand-written body. The only "custom infrastructure replacing an
engine feature" in this domain is the snapshot system replacing `NetworkTransform` — and that is
rubric item #6 answered correctly: the shortcoming is documented (`docs/02-netcode.md:59-64`,
`docs/00-legacy-analysis.md`), it is the entire point of the project, and the replacement is 75 lines.

**Under-engineering** in this domain is the real risk and it is F-A4-1: there is no measurement seam
at all. `NetworkSimulationLoop.SnapshotsSent` counts messages because counting bytes was never asked
for; a project whose deliverable is evidence should have had a byte counter before it had a
correction counter.

## Quantified Estimates

**Everything below is tagged `ESTIMATED`.** Per the brief, `ESTIMATED` means derived from reading
code — including deterministic byte counts read straight out of NGO and UTP package source. **No row
here is `MEASURED`: there is no profiler capture, no bandwidth log and no byte counter anywhere in
the repository** (F-A4-1). `docs/05-validation.md` contains no bandwidth figures at all, so nothing
from it (Scenario C or otherwise) is used below.

### Assumptions, with sources

| # | Assumption | Value | Source |
|---|---|---|---|
| A1 | Tick rate | 30 Hz | `Assets/_Project/Scenes/Bootstrap.unity:446` (`TickRate: 30`) |
| A2 | Players | 4 (1 host + 3 clients) | project context (settled) |
| A3 | `bool` on the wire | 1 byte | NGO `FastBufferWriter.WriteUnmanagedSafe<bool>` |
| A4 | `Vector2` on the wire | 8 bytes, uncompressed float32 ×2 | `PlayerState.cs:41-42`, unmanaged memcpy path |
| A5 | NGO batch header | **16 bytes** per datagram (`ushort`+`ushort`+`int`+`ulong`, written raw) | `.../Runtime/Messaging/NetworkBatchHeader.cs`; written via `WriteValue` at `NetworkMessageManager.cs:867` |
| A6 | NGO message header | bit-packed `MessageType` + `MessageSize` → **1–2 + 1–2 bytes** | `NetworkMessageManager.cs:643-649`; `BytePacker.cs:408-428` |
| A7 | NGO RPC metadata | bit-packed `NetworkObjectId` (≈2) + `NetworkBehaviourId` (1) + `NetworkRpcMethodId` (5, it is a 32-bit hash) = **8 bytes** | `.../Messaging/Messages/RpcMessages.cs:9-15`; `BytePacker.cs:408-472` |
| A8 | UTP unreliable pipeline | fragmentation stage header **2 bytes** (release build) | `.../com.unity.transport/Runtime/Pipelines/FragmentationPipelineStage.cs:41`; pipeline selected at `UnityTransport.cs:664-665` |
| A9 | UTP connection layer | **9 bytes** (1 type + 8-byte `ConnectionToken`) | `.../Runtime/Layers/SimpleConnectionLayer.cs:14`; `Runtime/ConnectionToken.cs:10` |
| A10 | Relay layer | **38 bytes** (4 header + 16 from-id + 16 to-id + 2 length) | `.../Runtime/Relay/Messages/RelayMessageRelay.cs:9`; `.../RelayMessageHeader.cs:8`; `RelayAllocationId.cs:14` |
| A11 | IPv4 + UDP | **28 bytes** per datagram | standard |
| A12 | **Host sends N−1 unicast copies, not a broadcast** | confirmed | `NotServerRpcTarget.Send` builds an `RpcTargetGroup` of every observer (`NotServerRpcTarget.cs:29-45`); `SendMessage` serializes once then loops `SendPreSerializedMessage` per client into per-client send queues (`NetworkMessageManager.cs:637-700`). Relay does not fan out either — `RelayMessageRelay` carries a single `ToAllocationId` (`RelayMessageRelay.cs:9`) |
| A13 | Relay $/GiB | **$0.16 / GiB egress** | Unity Gaming Services published Relay bandwidth rate. **Not verifiable from this repo — the author must confirm against the current UGS pricing page before quoting it.** Figures below are given per-GiB so a different rate substitutes directly. |
| A14 | Session length for cost | 10 minutes | demo-length assumption; scales linearly |

### 1. Every synced type, field by field

`INetworkSerializable` is implemented by hand on every type below — **no reflection-based
serialization anywhere in the project**, verified by reading each `NetworkSerialize` body.

#### `InputCommand` — `Assets/_Project/Scripts/Simulation/InputCommand.cs:41-46`

| Field | Type | Bytes | Tag |
|---|---|---:|---|
| `Tick` | `uint` | 4 | ESTIMATED |
| `MoveX` | `sbyte` | 1 | ESTIMATED |
| `Buttons` | `byte` (bitfield: `JumpHeld`, `JumpPressed`) | 1 | ESTIMATED |
| **Total** | | **6** | ESTIMATED |

The struct's own comment claims "six bytes on the wire" — **that one is exactly right.**

#### `InputPacket` — `Assets/_Project/Scripts/Simulation/InputPacket.cs:21-26`

| Field | Bytes | Tag |
|---|---:|---|
| `Newest` (`InputCommand`) | 6 | ESTIMATED |
| `Previous` | 6 | ESTIMATED |
| `Oldest` | 6 | ESTIMATED |
| **Total payload** | **18** | ESTIMATED |

#### `PlayerState` — `Assets/_Project/Scripts/Simulation/PlayerState.cs:40-48`

| Field | Type | Bytes | Read by remote peers? | Tag |
|---|---|---:|---|---|
| `Position` | `Vector2` (2×float32, uncompressed) | 8 | yes (`SnapshotInterpolator.cs:89`) | ESTIMATED |
| `Velocity` | `Vector2` (2×float32, uncompressed) | 8 | yes (`:90`) | ESTIMATED |
| `Grounded` | `bool` | 1 | yes (`:91`) | ESTIMATED |
| `CoyoteTimer` | `float` | 4 | **no** | ESTIMATED |
| `JumpBufferTimer` | `float` | 4 | **no** | ESTIMATED |
| `StunTimer` | `float` | 4 | **no** | ESTIMATED |
| **Total** | | **29** | 17 useful to a remote (F-A4-5) | ESTIMATED |

#### `PlayerSnapshot` — `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:41-47`

| Field | Type | Bytes | Tag |
|---|---|---:|---|
| `NetworkObjectId` | `ulong`, **raw, not bit-packed** (F-A4-4) | 8 | ESTIMATED |
| `State` | `PlayerState` | 29 | ESTIMATED |
| `LastProcessedInputTick` | `uint` | 4 | ESTIMATED |
| `IsTeleport` | `bool` | 1 | ESTIMATED |
| **Total per player** | | **42** | ESTIMATED |

#### `SnapshotFrame` — `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:63-73`

| Field | Bytes | Tag |
|---|---:|---|
| `Tick` (`uint`) | 4 | ESTIMATED |
| `count` (`int`, raw) | 4 | ESTIMATED |
| `Players[N]` | 42 N | ESTIMATED |
| **Total payload, N=4** | **176** | ESTIMATED |
| **Formula** | `8 + 42N` | ESTIMATED |

> Fits `NonFragmentedMessageMaxSize` = 1296 bytes
> (`NetworkMessageManager.cs:113`) up to N ≈ 30 players, so fragmentation is never reached at 4.

#### `PlayerSlot` (the roster entry) — `Assets/_Project/Scripts/Connection/PlayerSlot.cs:27-33`

| Field | Type | Bytes | Tag |
|---|---|---:|---|
| `ClientId` | `ulong`, raw | 8 | ESTIMATED |
| `Nickname` | `FixedString32Bytes` → 4-byte length + UTF-8 bytes, ≤16 chars | 4 + 0…16 | ESTIMATED |
| `CharacterIndex` | `int` | 4 | ESTIMATED |
| `IsReady` | `bool` | 1 | ESTIMATED |
| **Total** | | **17–33** | ESTIMATED |

Length prefix is a fixed `sizeof(uint)` (`FastBufferWriter.cs:942`). Nickname is capped at 16 by
`ConnectionApproval.MaxNicknameLength` (`ConnectionApproval.cs:29`). **This is the only
`FixedString` on the wire and it is not on a hot path** — it moves on join, leave and ready-toggle
only, via `NetworkList<PlayerSlot>` deltas (`SessionRoster.cs:24`).

#### `ConnectionPayload` — `Assets/_Project/Scripts/Connection/ConnectionPayload.cs:35-40`

`GameVersion` (4 + ≤32) + `Nickname` (4 + ≤16) + `CharacterIndex` (4) = **12–56 bytes, once per
connection**, riding the NGO handshake. Not recurring traffic.

#### All 12 `NetworkVariable`s

| Variable | File:line | Wire size | Write rate | Who receives | Tag |
|---|---|---:|---|---|---|
| `_life` (`float`) | `Player/PlayerLife.cs:29` | 4 | **1 Hz** (`MatchConfig.LifeReplicationHz: 1`, `MatchConfig.asset`), plus immediately on fruit pickup and on death | all peers | ESTIMATED |
| `_alive` (`bool`) | `Player/PlayerLife.cs:47` | 1 | on death / round reset | all peers | ESTIMATED |
| `_kind` (`int`) | `Fruits/Fruit.cs:35` | 4 | once, **before** `Spawn()` so it rides the spawn message (`FruitSpawner.cs:93-99`) | all peers | ESTIMATED |
| `_phase` (`MatchPhase` enum) | `Match/MatchDirector.cs:33` | 4 | on phase change (≈5 per match) | all peers | ESTIMATED |
| `_arenaIndex` (`int`) | `Match/MatchDirector.cs:34` | 4 | once per match | all peers | ESTIMATED |
| `_playStartsAtServerTime` (`double`) | `Match/MatchDirector.cs:50` | 8 | **once** per match — a deadline, not a counter | all peers | ESTIMATED |
| `_loadedCount` (`int`) | `Match/MatchDirector.cs:82` | 4 | once per client that finishes loading | all peers | ESTIMATED |
| `_expectedCount` (`int`) | `Match/MatchDirector.cs:83` | 4 | once per match | all peers | ESTIMATED |
| `_winner` (`ulong`) | `Match/RoundReferee.cs:27` | 8 | twice per round (reset + verdict) | all peers | ESTIMATED |
| `_outcome` (`MatchOutcome` enum) | `Match/RoundReferee.cs:28` | 4 | twice per round | all peers | ESTIMATED |
| `_roundEndsAtServerTime` (`double`) | `Match/RoundReferee.cs:38` | 8 | **once** per round | all peers | ESTIMATED |
| `_slots` (`NetworkList<PlayerSlot>`) | `Connection/SessionRoster.cs:24` | 17–33 per entry | full list on spawn, delta on join/leave/ready | all peers | ESTIMATED |

`EnsureNetworkVariableLengthSafety: 0` (`Bootstrap.unity:459`), so there is no extra per-variable
size prefix. **No `NetworkVariable` holds a large struct, and none is written per frame** — the two
that a naive implementation would have written every frame (the life timer and the two countdowns)
are explicitly interval-published and deadline-published respectively, with the reasoning at
`PlayerLife.cs:16-21` and `MatchDirector.cs:36-48`.

#### `NetworkTransform` — settled by reading the YAML

**There is none.** I mapped every `m_Script` GUID in both prefabs against the package `.meta` files:

| Prefab | Networked components | Tag |
|---|---|---|
| `Assets/_Project/Prefabs/Player.prefab` | `NetworkObject` (`:52`, guid `d5a57f76…`) + `InputReader` (`:150`) + `PredictedPlayer` (`:163`) + `CharacterAppearance` (`:181`) + `PlayerLife` (`:196`) + `VisualSmoother` on the `Visual` child (`:393`) | ESTIMATED |
| `Assets/_Project/Prefabs/Fruit.prefab` | `NetworkObject` (`:142`) + `Fruit` (`:167`) | ESTIMATED |

A project-wide grep for the NGO component GUIDs — `NetworkTransform` `e96cb6065543e43c4a752faaa1468eb1`,
`AnticipatedNetworkTransform` `5abfce83…`, `NetworkRigidbody` `f6c0be61…`, `NetworkRigidbody2D`
`80d7c879…`, `NetworkAnimator` `e8d0727d…` (all read from
`.../com.unity.netcode.gameobjects@60c1d83693e8/Runtime/Components/*.cs.meta`) — returns **no matches
anywhere under `Assets/`**, prefabs and scenes included. **No double-syncing exists.**

One nuance worth knowing: `Player.prefab:60` sets `SynchronizeTransform: 1` on the `NetworkObject`.
That is a **one-time** spawn/scene-sync payload, not per-tick replication, and it is load-bearing
here — `PredictedPlayer.OnNetworkSpawn` seeds `_state` from `transform.position`
(`PredictedPlayer.cs:249`). Correct as configured.

### 2. Bytes on the wire, per datagram

Both channels send one NGO message per tick, so each is one batch and one datagram. Direct = LAN /
`DirectConnectionProvider`; Relay = `RelayConnectionProvider` (`.WithRelayNetwork()`,
`RelayConnectionProvider.cs:74-77`).

| Layer | Snapshot (down) | Input (up) | Source | Tag |
|---|---:|---:|---|---|
| Application payload | 176 | 18 | this file, §1 | ESTIMATED |
| + NGO RPC metadata | 8 | 8 | A7 | ESTIMATED |
| + NGO message header | 3 | 2 | A6 | ESTIMATED |
| + NGO batch header | 16 | 16 | A5 | ESTIMATED |
| + UTP fragmentation stage | 2 | 2 | A8 | ESTIMATED |
| + UTP connection layer | 9 | 9 | A9 | ESTIMATED |
| **= UDP payload, direct** | **214** | **55** | | ESTIMATED |
| + IPv4/UDP | 28 | 28 | A11 | ESTIMATED |
| **= on the wire, direct** | **242** | **83** | | ESTIMATED |
| + Relay header | 38 | 38 | A10 | ESTIMATED |
| **= on the wire, via Relay** | **280** | **121** | | ESTIMATED |

Note the shape of the input channel: **18 bytes of payload inside a 121-byte relayed datagram — 85 %
framing.** That is not a defect to fix (a per-tick channel needs a per-tick datagram) but it is the
honest answer to "how much does input cost", and it is why raising the tick rate would cost far more
than the payload arithmetic suggests.

### 3. Bytes per second, 4 players @ 30 Hz

`bytes/s = datagram_bytes × 30`. Host sends **3 unicast copies** of the snapshot (A12).

| Row | Formula | B/s | kbps | Tag |
|---|---|---:|---:|---|
| **Direct connection** | | | | |
| Client ← snapshot | 242 × 30 | 7,260 | 58.1 | ESTIMATED |
| Client → input | 83 × 30 | 2,490 | 19.9 | ESTIMATED |
| **Host → total (uplink)** | 3 × 242 × 30 | **21,780** | **174.2** | ESTIMATED |
| Host ← total (downlink) | 3 × 83 × 30 | 7,470 | 59.8 | ESTIMATED |
| Session, all uplinks summed | 21,780 + 3×2,490 | 29,250 | 234.0 | ESTIMATED |
| **Via Relay** | | | | |
| Client ← snapshot | 280 × 30 | 8,400 | 67.2 | ESTIMATED |
| Client → input | 121 × 30 | 3,630 | 29.0 | ESTIMATED |
| **Host → total (uplink)** | 3 × 280 × 30 | **25,200** | **201.6** | ESTIMATED |
| Host ← total (downlink) | 3 × 121 × 30 | 10,890 | 87.1 | ESTIMATED |
| `NetworkVariable` deltas, added | ~4 reliable msgs/s (`_life` ×4 players) × ~80 B, per client | ~320 /client | ~2.6 | ESTIMATED (wider error bars: the reliable pipeline's ack/resend header was not counted field-by-field) |
| **Host uplink, all in** | 25,200 + 3×320 | **≈26,160** | **≈209** | ESTIMATED |
| **Client downlink, all in** | 8,400 + 320 | **≈8,720** | **≈70** | ESTIMATED |

### 4. Against the two budgets

| Node / direction | Relayed load | vs **256 kbps mobile** | vs **5 Mbps home** | Tag |
|---|---:|---:|---:|---|
| Client downlink | 70 kbps | **27 %** | 1.4 % | ESTIMATED |
| Client uplink | 29 kbps | **11 %** | 0.6 % | ESTIMATED |
| **Host uplink** | **209 kbps** | **82 %** | **4.2 %** | ESTIMATED |
| Host downlink | 87 kbps | 34 % | 1.7 % | ESTIMATED |

**Mobile is a stated target platform, so the 82 % row is a real constraint, not a hypothetical.**
Playing on mobile is comfortable; **hosting on mobile is marginal**, and mobile uplink is typically
the scarcer direction — see F-A4-8. A 5 Mbps home connection is not remotely stressed by anything
here, in any role.

### 5. Scaling shape — analysis only, not a proposal to change the player count

The 4-player limit is settled. This section exists because "what does your design cost at 3× the
players" is a standard interview question and the author should have the number ready.

The host's uplink is **O(N²)**: the snapshot payload grows linearly in N *and* it is sent to N−1
clients.

```
host_uplink(N) = 30 × (N − 1) × (42N + 8 + 8 + 3 + 16 + 2 + 9 + 38 + 28)
               = 30 × (N − 1) × (42N + 112)   bytes/sec, relayed
```

| N | Snapshot payload | Relayed datagram | Host uplink | vs N=4 | Client downlink | Tag |
|---:|---:|---:|---:|---:|---:|---|
| 2 | 92 B | 196 B | 5,880 B/s (47 kbps) | 0.23× | 47 kbps | ESTIMATED |
| **4** | **176 B** | **280 B** | **25,200 B/s (202 kbps)** | **1.00×** | **67 kbps** | ESTIMATED |
| 8 | 344 B | 448 B | 94,080 B/s (753 kbps) | 3.73× | 108 kbps | ESTIMATED |
| **12 (3×)** | **512 B** | **616 B** | **203,280 B/s (1.63 Mbps)** | **8.07×** | **148 kbps** | ESTIMATED |
| 16 | 680 B | 784 B | 352,800 B/s (2.82 Mbps) | 14.0× | 188 kbps | ESTIMATED |

**Tripling the player count multiplies the host's uplink by 8.1×**, because it is quadratic. Every
client's downlink meanwhile grows only linearly (67 → 148 kbps), which is the useful half of the
answer: *the design scales fine for the people watching and quadratically for the person hosting.*
The first hard wall is not bandwidth but MTU — the payload passes `NonFragmentedMessageMaxSize`
(1296 B, `NetworkMessageManager.cs:113`) at **N ≈ 30**, after which every snapshot fragments and the
unreliable delivery model degrades badly. Nothing in the code assumes 4; nothing needs changing for
this to remain true.

### 6. Interest management

**There is none, and none is needed.** Verified: `NetworkSimulationLoop.BroadcastSnapshot`
(`NetworkSimulationLoop.cs:100-112`) serializes every registered peer into one frame and
`SendTo.NotServer` delivers it to every observer; there is no visibility filter, no distance cull,
no `NetworkObject.CheckObjectVisibility` override anywhere in `Assets/_Project/Scripts`, and
`SpawnWithObservers: 1` on both prefabs means everything is visible to everyone by default.

That is the correct answer for this game. Four players share one screen-sized arena
(`ArenaBounds` clamps the spectator camera to it), so every player is relevant to every other player
at all times — filtering would compute a predicate whose answer is always `true`. Adding interest
management here would be a textbook rubric-#7 hit: complexity for a scale that is explicitly not the
target. **Not a finding.** It becomes one at roughly the N=12–16 rows above, and the 8.1× figure is
the number that would justify it.

### 7. Relay cost per demo session

`target_ccu` is 0. No monthly bill is computed. What follows is the per-session figure the author can
quote.

Relay routes **every** byte through Unity's servers: the host's 3 snapshot copies go up to Relay and
come back down to 3 clients, and each client's input goes up and comes back down to the host. There
is no fan-out at the Relay — `RelayMessageRelay` carries one `ToAllocationId`
(`RelayMessageRelay.cs:9`), so the host really does upload 3 separate copies. Verified against the
configuration: `RelayConnectionProvider` uses `new SessionOptions{…}.WithRelayNetwork()`
(`RelayConnectionProvider.cs:74-79`) with no direct-connect or host-migration fallback, so **100 % of
a Relay session's traffic is relayed**. Compared with a direct connection carrying the same game, the
relayed byte count is **~1.16× larger** (the 38-byte Relay header on a 242-byte datagram), and the
bytes Unity is billed for are **~2× the one-way game traffic**, because every packet is counted once
into Relay and once out.

| Quantity | Formula | Value | Tag |
|---|---|---:|---|
| Relay ingress (all peers → Relay) | 25,200 (host) + 3 × 3,630 (clients) | 36,090 B/s | ESTIMATED |
| Relay egress (Relay → all peers) | 3 × 8,400 + 3 × 3,630 | 36,090 B/s | ESTIMATED |
| Egress per 10-min session | 36,090 × 600 | **21.65 MB** (0.0202 GiB) | ESTIMATED |
| Ingress + egress per session | × 2 | 43.3 MB | ESTIMATED |
| **Cost per 10-min 4-player session** | 0.0202 GiB × $0.16/GiB (A13) | **≈ $0.0032** | ESTIMATED |
| Cost per hour of continuous play | 0.121 GiB × $0.16 | ≈ $0.019 | ESTIMATED |
| Sessions per $1 | 1 / 0.0032 | ≈ 310 | ESTIMATED |

**The figure to quote: about a third of a cent per ten-minute four-player demo session, or roughly
2 cents an hour.** The rate in A13 is not verifiable from this repository and must be checked against
the current UGS pricing page before it is stated publicly; the per-GiB figure (0.0202 GiB/session)
is the durable number and survives any rate change.

## What is genuinely good here

Specific, cited, and not padding — this domain is the strongest part of the project I looked at.

1. **The wire surface is genuinely minimal, and that is a design achievement, not an accident.**
   Three RPCs in 6,200 lines. Two of them carry the game; the third
   (`SessionRoster.cs:133`) is a ready-toggle. No `NetworkTransform`, no `NetworkRigidbody`, no
   `NetworkAnimator` — I checked the YAML rather than the greps. A hand-rolled snapshot system living
   *alongside* an engine transform sync is the single most common way this kind of project doubles
   its bandwidth, and it did not happen here.

2. **Every replicated type is hand-serialized. There is no reflection anywhere.** `InputCommand`,
   `InputPacket`, `PlayerState`, `PlayerSnapshot`, `SnapshotFrame`, `PlayerSlot`, `ConnectionPayload`
   — seven types, seven explicit `NetworkSerialize` bodies. `PlayerSlot` even implements
   `IEquatable` correctly for `NetworkList` delta detection, with the reasoning at
   `PlayerSlot.cs:35-39` for why an id-only comparison would silently never replicate a ready-up.

3. **`InputCommand.MoveX` is quantized to `sbyte` for determinism, not for bytes**
   (`InputCommand.cs:9-13`). Getting to a 6-byte input struct is the *side effect* of the correct
   reason — an analog axis rounds differently on two machines and breaks replay. Arriving at the
   efficient answer via the correct argument is the thing an interviewer is actually looking for.

4. **Redundancy instead of retransmission, argued rather than asserted.** `InputPacket` sends each
   command three times across three consecutive unreliable packets (`InputPacket.cs:5-14`), and the
   server dedupes on `_highestReceivedInputTick` (`PredictedPlayer.cs:400-416`). This costs 12 extra
   bytes per tick and buys immunity to two consecutive losses with no added latency. The same
   reasoning is applied consistently to the `IsTeleport` flag (`PredictedPlayer.cs:101-111`) — a flag
   sent once over unreliable delivery is a flag that can be lost.

5. **The two places a naive implementation bleeds bandwidth were both identified and closed.**
   `PlayerLife` publishes on a 1 Hz interval while draining every frame server-side, and clients
   interpolate downward between updates (`PlayerLife.cs:16-21`, `:163-172`) — the comment even names
   the original project's 60 writes/second as the thing being fixed. `MatchDirector` and
   `RoundReferee` replicate a **deadline** against the shared `ServerTime` clock instead of a
   counting-down number (`MatchDirector.cs:36-48`, `RoundReferee.cs:30-38`), which removes both the
   traffic and the disagreement rather than managing them. That is exactly the right instinct and it
   is applied twice, independently.

6. **One frame per tick, not one message per player.** `BroadcastSnapshot` builds a single
   `SnapshotFrame` (`NetworkSimulationLoop.cs:100-112`) into a **reused scratch array**, so the send
   path allocates nothing. It saves 3 batch headers (48 bytes) and 3 UTP framings per tick versus
   per-player messages, and — more importantly — gives every state in the frame a shared timestamp,
   which is what makes `SnapshotInterpolator` able to line remote characters up against one clock.

7. **Unreliable is the deliberate choice on both channels, with the argument written next to it**
   (`NetworkSimulationLoop.cs:114-118`: "a re-sent snapshot arrives describing a moment that has
   already been superseded"). Most projects at this stage use reliable everywhere and discover
   head-of-line blocking under loss much later.

8. **`EnsureNetworkVariableLengthSafety: 0` and `RpcHashSize: 0`** (`Bootstrap.unity:452,458`) are set
   to the efficient values rather than left at whatever the inspector defaulted to.

## Open questions for the team

1. **Does WebGL stay on the target list?** (F-A4-3.) It is a one-line answer with a large consequence:
   keeping it means the unreliable-delivery argument that anchors `docs/02-netcode.md` needs a stated
   exception, and dropping it costs nothing.
2. **Is a mobile build allowed to Host, or Join only?** (F-A4-8.) At 82 % of a 256 kbps uplink the
   answer is a judgement call, and it cannot be made responsibly until the number is measured rather
   than derived.
3. **Was the "~120 byte" figure in `SnapshotFrame.cs:56` a measurement from an earlier version of
   `PlayerState`, or an estimate?** If it was measured before the three feel timers and the teleport
   flag were added, the fix is to re-measure; if it was always an estimate, that is worth knowing
   before any other number in the docs is trusted.
4. **Is the current UGS Relay egress rate still $0.16/GiB?** Assumption A13 could not be verified
   from the repository, and the per-session dollar figure depends entirely on it. The
   0.0202 GiB-per-session figure does not.
5. **Should a byte counter be added alongside `ReconciliationStats`?** The project already has the
   discipline (`RunRecorder` writes conditions with every run); extending the CSV with bytes
   sent/received per second would make bandwidth a first-class measured result rather than a Phase 5
   HUD item, and it would close F-A4-1 permanently instead of once.
