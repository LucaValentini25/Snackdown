# Security & Anti-Cheat Audit

**Agent:** A7 · **Target:** `dev` @ `10a2a13` · **Date:** 2026-08-08 · **Scope:** trust boundary, RPC
authorization, connection approval, secrets/config, debug surfaces, supply chain.

## Verdict

The trust boundary is drawn in the right place and the *gameplay* half of it is genuinely well
built: there is no `NetworkTransform` on `Player.prefab`, every match-deciding write is
`IsServer`-guarded, and head-bounce and fruit pickup are resolved on the server against the server's
own positions — a modified client cannot claim a stun, a pickup, a death or a win. But the
**authorization** half is missing at exactly two attribute declarations. Both RPCs that carry the
simulation are declared with NGO's default `InvokePermission = RpcInvokePermission.Everyone`
(`RpcAttributes.cs:29,93`), and NGO enforces that permission on the receiving side
(`RpcMessages.cs:66-77`) — so **any client can submit input for any other player's character**, and
**any client can broadcast forged authoritative snapshots to every other client**. A third gap is
adjacent: `InputCommand.MoveX` is documented as quantized to -1/0/1 but is an `sbyte` that the server
never clamps before feeding it to `PlayerMotor`, producing a speed hack of up to 127× move speed.

These three are the **docs-contradiction** kind of Critical, not the business-risk kind:
`docs/02-netcode.md:15-16` states "the worst a cheater can do is move *legally*", and
`docs/01-architecture.md:101` states "A `ServerRpc` assumes the caller is hostile until checked".
Neither is true as implemented, and those claims are the deliverable of this project. The
compensating fact is that all three fixes are one line each — `InvokePermission =
RpcInvokePermission.Owner`, `InvokePermission = RpcInvokePermission.Server`, and a `Math.Sign` — so
this is under-engineering at three specific points, not a design that needs redoing.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Trust boundary design (where authority lives) | 4 | Correct and consistently applied; no client-writable state anywhere. Loses a point because the boundary is enforced by convention (`IsServer` guards) and not by any RPC authorization. |
| RPC authorization & payload validation | 1 | Zero of the 3 RPCs declares an `InvokePermission`. Sender identity is never checked on the input channel; `MoveX` is never range-checked; `SnapshotFrame` array length is unbounded. |
| Connection front door (`ConnectionApproval`) | 4 | Payload treated as hostile, `TryRead` catch-all, control chars stripped, index double-clamped, version gated. Loses points for a racy player cap and rich-text markup surviving sanitation. |
| Secrets & config hygiene | 5 | No keys, tokens or endpoints in the repo. Analytics/ads/crash reporting all off. No PII logged, no `Debug.Log` on a hot path. |
| Debug surface & supply chain | 3 | `NetDebugOverlay` ships in release builds (verified in `Arena01.unity`), but its four hotkeys are all local-only and confer no advantage. `com.coplaydev.unity-mcp` floats on `#main`, mitigated by a tracked `packages-lock.json` hash. |

---

## The trust boundary as implemented

Established by reading every `NetworkBehaviour`, both networked prefabs, and NGO's own message
handlers. **Not** as documented — as built.

| Decision | Who decides, as built | Can a modified client influence it? |
|---|---|---|
| Own position / velocity | Server (`PredictedPlayer.cs:422-455`). No `NetworkTransform` on `Player.prefab` (component guids resolved: `NetworkObject`, `InputReader`, `PredictedPlayer`, `CharacterAppearance`, `PlayerLife`, `VisualSmoother` — nothing else). | **Only through input.** Position is never accepted from the wire. But see F-A7-1 and F-A7-3. |
| Move axis magnitude | Server, but **unvalidated** (`PlayerMotor.cs:67`). | **Yes — F-A7-3.** |
| Whose input drives a character | **Nobody checks** (`PredictedPlayer.cs:380`). | **Yes — F-A7-1.** |
| Head-bounce stun | Server only, once per tick, over server-owned positions (`HeadBounce.cs:58-100`). `ServerApplyStun`/`ServerBounce` are both `if (!IsServer) return`. | **No.** A client cannot claim a stomp. Only indirectly, by moving into position faster than legal (F-A7-3). |
| Predicted peer collision | Owner predicts locally against its **own** `WorldSnapshotBuffer` (`PredictedPlayer.cs:650-674`); the server independently captures its own (`PredictedPlayer.cs:452`) and its answer wins via reconciliation. `SweepPeers` only *limits the mover* — it never displaces the other body (`PlayerMotor.cs:210-251`). | **No.** The host never reads a client's opinion of where anyone else was. A cheater cannot shove or displace another player; it can body-block, and F-A7-3 makes body-blocking far more effective. This is the strongest part of the design and `docs/02`'s claim about it holds. |
| Fruit pickup | Server-side `Physics2D.OverlapCircle` in `Fruit.Update`, gated `if (!IsServer)` (`Fruit.cs:69-88`). | **No.** |
| Life timer | Server (`PlayerLife.cs:135-161`); `ServerAdd`/`ServerReset` both `IsServer`-guarded. `NetworkVariable` write permission left at the default (Server). | **No.** |
| Death / alive flag | Server (`PlayerLife.cs:155-160`), replicated as a flag rather than derived from the interpolated number. | **No.** |
| Win condition | Server (`RoundReferee.cs:86-141`); replicated as a *verdict*, not as ingredients. | **No.** |
| Match phase, arena, countdown | Server (`MatchDirector.cs:137-236`). Not reachable from any RPC — `ServerStartMatch` is only called from the host's own UI (`MainMenuController.cs:385`). | **No.** |
| Ready state | Server, sender id from `RpcParams` (`SessionRoster.cs:133-146`). Verified against NGO: on the server, `RpcMessage.Handle` overrides the wire-supplied `SenderClientId` with the transport id (`RpcMessages.cs:209`), so the XML doc's claim holds. | **Own flag only.** But no rate limit — F-A7-4. |
| Nickname / character index | Server, sanitized and clamped in `ConnectionApproval.Approve`. | Clamped correctly (twice: `ConnectionApproval.cs:183` and `CharacterCatalog.cs:41`). Markup survives — F-A7-6. |
| Roster membership | Server only (`SessionRoster.cs:81-105`), driven by NGO connect/disconnect callbacks. | **No.** |

**Authoritative logic in the shared assembly.** `Snackdown.Simulation` (`PlayerMotor`, `PlayerState`,
`MovementConfig`) is compiled into the client by design, and that is correct — prediction requires
it. What it means for a cheater is narrower than it sounds: the client's *copy* of the motor and its
`MovementConfig` values are irrelevant, because the server runs its own copy with its own asset
(`MovementConfig.cs:9-12` — never replicated) over the only thing the client actually supplies, an
`InputCommand`. So the entire attack surface of the shared assembly reduces to **the six bytes of
`InputCommand`** — which is exactly the right answer, and exactly why F-A7-3 (an unvalidated field in
those six bytes) is the finding that matters.

---

## Findings

### F-A7-1 — Any client can submit input for any other player's character

- **Severity**: Critical
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:380-389`;
  `Library/PackageCache/com.unity.netcode.gameobjects@60c1d83693e8/Runtime/Messaging/RpcAttributes.cs:29,93`;
  `.../Runtime/Messaging/Messages/RpcMessages.cs:66-77`; `docs/01-architecture.md:101`
- **What it is**: `SubmitInputRpc` is declared `[Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]`.
  `RpcAttribute.InvokePermission` is not set, and its default is `RpcInvokePermission.Everyone`
  (`RpcAttributes.cs:29` — the enum's zero value). NGO enforces the permission table on the
  **receiving** side in `RpcMessageHelpers.Handle`:
  ```csharp
  if ((permission == RpcInvokePermission.Server && rpcParams.SenderId != NetworkManager.ServerClientId) ||
      (permission == RpcInvokePermission.Owner  && rpcParams.SenderId != networkObject.OwnerClientId))
  ```
  With `Everyone`, neither branch fires. The method body never reads `RpcParams` and has no
  `IsOwner`/sender check of its own — it goes straight to `EnqueueIfNew` on `this` player's queue.
  The RPC is addressed by `NetworkObjectId` + `NetworkBehaviourId` + method hash, all of which a
  modified client already has for every spawned peer.
- **How a scripted attacker performs it**: run a modified build; in the client's own tick loop, call
  the same `SubmitInputRpc` a second time against a *victim's* `PredictedPlayer` component instead
  of its own. No packet crafting is needed — the generated send path works verbatim once the target
  behaviour reference is swapped, because the sender-side guard in `__beginSendRpc`
  (`NetworkBehaviour.cs:342`) also only trips on `Owner`/`Server` permission, which is not set.
  Two payloads matter:
  1. **Puppeteering** — send `MoveX`/jump for the victim. The server enqueues and simulates them
     authoritatively, interleaved with the victim's genuine inputs.
  2. **Remote freeze** — send one packet with `Tick = serverTick + 64`. `EnqueueIfNew` accepts it
     (it is exactly at the `MaxInputTickLead` boundary) and raises the victim's
     `_highestReceivedInputTick`. Every genuine input the victim sends for the next 64 ticks is then
     rejected by `if (input.Tick <= _highestReceivedInputTick) return` (`PredictedPlayer.cs:404`),
     the server starves, and `ServerSimulateTick` repeats `_lastConsumedInput` forever
     (`PredictedPlayer.cs:443-449`). Repeat once every 2.1 s to hold it indefinitely.
- **Why it matters**: it is the single exploit that most directly falsifies the project's central
  claim. `docs/01-architecture.md:101` — "**RPCs are validated.** A `ServerRpc` assumes the caller is
  hostile until checked" — is the invariant this violates, and the XML doc at
  `PredictedPlayer.cs:82-84` repeats the same sentence while the code checks only *how much* the
  caller sends, never *who* they are. In an interview this is the first thing a senior netcode
  engineer greps for.
- **Recommendation**: `[Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]`.
  The host is owner of its own object and never takes this path (`NetworkSimulationLoop.cs:80`), so
  nothing legitimate breaks. Update the XML remark at `PredictedPlayer.cs:77-85` to name the
  ownership check as the mitigation rather than implying the queue cap is the whole story.
- **Effort**: S

### F-A7-2 — Any client can broadcast forged authoritative snapshots to every other client

- **Severity**: Critical
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/NetworkSimulationLoop.cs:119-136`;
  `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:63-73`;
  `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:475-582`;
  `.../Runtime/Messaging/RpcTargets/NotServerRpcTarget.cs:16-67`;
  `.../Runtime/Messaging/Messages/ProxyMessage.cs` (`Handle`)
- **What it is**: `SnapshotRpc` is `[Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]` —
  again `InvokePermission = Everyone`. `SendTo.NotServer` is **not** a server-only direction:
  `NotServerRpcTarget.Send` explicitly handles `!behaviour.IsServer` by routing through a
  `ProxyRpcTargetGroup`, and `ProxyMessage.Handle` on the server relays it after checking the
  permission table — which says `Everyone`, so it relays. On the receiving clients,
  `RpcMessage.Handle` uses the wire-supplied `SenderClientId` when not server, and
  `PredictedPlayer.ApplySnapshot` checks only `if (IsServer) return` — never that the sender *was*
  the server.
- **How a scripted attacker performs it**: from a modified client, call `SnapshotRpc` on the scene's
  `NetworkSimulationLoop` with a hand-built `SnapshotFrame`. `ProxyMessage` carries an
  attacker-chosen `TargetClientIds` list, so victims can be selected. Three payloads:
  1. **Teleport griefing** — `IsTeleport = true` with an arbitrary `State.Position`. The victim's
     `Reconcile` takes the `HardSnapTo` branch (`PredictedPlayer.cs:507-517`) and their local
     character is yanked, at whatever rate the attacker sends.
  2. **Permanent client-side desync** — one frame with `LastProcessedInputTick = uint.MaxValue`.
     `_lastAckedTick` latches to that value, and the guard `if (ackTick < _lastAckedTick) return`
     (`PredictedPlayer.cs:499`) then discards **every genuine snapshot for the rest of the
     session**. The victim's prediction runs forever uncorrected; nothing recovers it short of a
     reconnect.
  3. **Allocation flood** — `SnapshotFrame.NetworkSerialize` reads `count` as a raw `int` from the
     wire and does `Players = new PlayerSnapshot[count]` (`SnapshotFrame.cs:68-70`) with no bound
     and no sign check, *before* reading any element. See Quantified Estimates.
- **Why it matters**: the down-channel is the one message clients treat as ground truth. This is the
  only finding that lets one player make the game unplayable for the other three from outside the
  rules, and (2) is unrecoverable in-session. It also contradicts `docs/02-netcode.md:13-16` and
  `docs/01-architecture.md:96-100`, which describe snapshots as authoritative by construction.
- **Recommendation**: `InvokePermission = RpcInvokePermission.Server` on `SnapshotRpc` — `ProxyMessage.Handle`
  will then drop the relay at the host, so forged frames never leave the attacker. Independently,
  bound the array in `SnapshotFrame.NetworkSerialize` (`if (count < 0 || count > MaxPlayers) { Players = null; return; }`)
  as defence in depth, since the deserializer is reachable before any permission check would be in a
  future refactor. Optionally assert `snapshotTime`/`Tick` monotonicity in `Reconcile` so a single
  bad frame cannot latch `_lastAckedTick` permanently.
- **Effort**: S

### F-A7-3 — The move axis is never range-checked on the server (speed hack)

- **Severity**: Critical
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Simulation/InputCommand.cs:22-23,41-46`;
  `Assets/_Project/Scripts/Simulation/PlayerMotor.cs:65-71`;
  `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:400-416`;
  `Assets/_Project/Settings/MovementConfig.asset`; `docs/02-netcode.md:15-16`
- **What it is**: `InputCommand.MoveX` is an `sbyte` documented as "Horizontal intent: -1, 0 or 1"
  and serialized raw. `EnqueueIfNew` validates the *tick* (duplicate, future-lead) and the *queue
  depth*, but nothing validates the *content*. `HorizontalStep` then computes
  `target = input.MoveX * cfg.MoveSpeed` and `Mathf.MoveTowards` climbs toward it every tick with no
  ceiling of its own — `MoveSpeed` is the intended cap only because `MoveX` is assumed to be ±1.
- **How a scripted attacker performs it**: change one field in the modified client's
  `SampleInput` — `MoveX = 127` instead of `±1`. Nothing else. Collision still holds (the `BoxCast`
  in `MoveAndCollideStep` is a sweep, so no wall tunneling and no out-of-bounds escape), but the
  character crosses the arena in a fraction of a tick, is effectively unhittable by a head bounce,
  reaches every fruit spawn first, and can pin any other player against geometry using the peer sweep.
  Combined with F-A7-1 this is also usable *against* a victim.
- **Why it matters**: `docs/02-netcode.md:15-16` says in as many words "A hacked client can send
  bogus input, but the server simulates it under the same rules as everyone else — **so the worst a
  cheater can do is move legally**." That sentence is the thesis of the netcode chapter and it is
  false as built. It also decides every match outcome in the game: last-standing and most-life-left
  both reduce to who reaches fruit and who lands stomps.
- **Recommendation**: clamp on ingest, not in the motor, so the invariant lives at the trust
  boundary — in `EnqueueIfNew`, before enqueueing: `input.MoveX = (sbyte)Math.Sign(input.MoveX);`
  and mask `input.Buttons &= (InputCommand.JumpHeldBit | InputCommand.JumpPressedBit);`. Add a
  `PlayerMotorTests` case asserting that an out-of-range `MoveX` produces the same result as `±1`.
- **Effort**: S

### F-A7-4 — `SetReadyRpc` has no rate limit; each call fans out a list delta and a full UI rebuild

- **Severity**: Major
- **Type**: Security
- **Confidence**: High (mechanism), Medium (magnitude)
- **Evidence**: `Assets/_Project/Scripts/Connection/SessionRoster.cs:133-146,79`;
  `Assets/_Project/Scripts/UI/MainMenuController.cs:283,294-338`;
  `.../Runtime/NetworkVariable/Collections/NetworkList.cs:678,97-100`
- **What it is**: `SetReadyRpc` correctly authenticates the sender, and the `if (slot.IsReady == ready) return`
  guard rejects a repeat of the same value — but not an *alternating* one. Every accepted write
  appends to `NetworkList.m_DirtyEvents` (`NetworkList.cs:678`), all of which are flushed in one
  delta per tick, and every event dispatched on every peer fires `OnSlotsChanged` → `Changed` →
  `MainMenuController.RefreshRoster`, which does `_rosterList.Clear()` and reconstructs three
  `VisualElement`s per roster row from scratch.
- **How a scripted attacker performs it**: sit in the lobby and call `SetReadyRpc(!last)` in
  `Update` instead of on a button click. One line.
- **Why it matters**: one client can pin the host's and every other client's main thread on UI
  Toolkit element allocation during the lobby, and inflate the reliable channel with a delta packet
  carrying hundreds of events per tick. It is the only unauthenticated-in-effect amplification in
  the project. Impact is bounded (lobby only, no match state corrupted), which is why it is Major
  and not Critical.
- **Recommendation**: server-side debounce in `SetReadyRpc` — reject a second change from the same
  sender within, say, 250 ms of the last accepted one, using `NetworkManager.ServerTime.Time`. One
  `Dictionary<ulong,double>` field. Separately, `RefreshRoster` should diff rather than
  `Clear()`-and-rebuild, but that is A6's call.
- **Effort**: S

### F-A7-5 — The player cap is checked against a counter NGO has not updated yet

- **Severity**: Major
- **Type**: Correctness / Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Connection/ConnectionApproval.cs:160-163,186-193`;
  `.../Runtime/Connection/NetworkConnectionManager.cs:841-856,863-878`
- **What it is**: `Approve` refuses when `_networkManager.ConnectedClientsIds.Count >= _maxPlayers`.
  NGO invokes the approval callback synchronously from `ApproveConnection` while handling the
  `ConnectionRequestMessage`, but only calls `HandleConnectionApproval` → `AddClient` (which is what
  updates `ConnectedClientsIds`) later, in `ProcessPendingApprovals`. Every connection request that
  lands in the same message-processing batch therefore reads the **same stale count** and all of
  them are admitted.
- **How a scripted attacker performs it**: open N transport connections in the same frame. It is
  also reachable without an attacker — two friends clicking Join within the same batch.
- **Why it matters**: the doorman's own XML doc (`ConnectionApproval.cs:12-15`) names "the player
  cap" as one of the three things that happen here specifically because rejecting later is
  expensive. A 5th player in a 4-player arena means an unassigned spawn point wrap
  (`PlayerSpawnPoints.cs:75`) and a `WorldSnapshotBuffer.MaxBodies` budget of 8 that starts to
  matter. On the Relay path `SessionOptions.MaxPlayers` (`RelayConnectionProvider.cs:76`) is a second
  gate at the service level; the Direct/LAN path has no second gate at all.
- **Recommendation**: count what this class itself has already admitted —
  `_approvedNicknames.Count >= _maxPlayers` — instead of asking NGO. The dictionary is written in
  `Admit` (`ConnectionApproval.cs:188`) synchronously, so it is correct within a batch, and it is
  already cleaned up by `Forget`. One-line change.
- **Effort**: S

### F-A7-6 — Nickname sanitation strips control characters but not UI Toolkit rich-text markup

- **Severity**: Major
- **Type**: Security
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/Connection/ConnectionApproval.cs:216-232` (and the stated
  threat model at `:24-28`); `Assets/_Project/Scripts/UI/MainMenuController.cs:314-320`
- **What it is**: `SanitizeNickname` drops `char.IsControl` characters and caps at 16 chars. It does
  not touch `<` or `>`. The sanitized name is rendered by `new Label(slot.Nickname.ToString() + …)`,
  and `TextElement.enableRichText` defaults to **true** in UI Toolkit, so tags such as
  `<size=…>`, `<color=…>` and `<sprite …>` are interpreted rather than displayed.
- **How a scripted attacker performs it**: type a name containing a markup tag in the menu — no
  modified client required. 16 characters is enough for `<size=400%>x`.
- **Why it matters**: it is precisely the hazard the code's own remark claims to have closed —
  "The cap exists so a name cannot become a payload — for the UI to overflow, or for another
  player's screen" (`ConnectionApproval.cs:26-27`). Impact is cosmetic griefing of the lobby
  roster, not compromise, which is why this is Major and not Critical. **Confidence is Medium** on
  one point only: I did not run the editor to confirm `enableRichText` is left at its default in
  `MainMenu.uxml` — the labels are constructed in C# (`MainMenuController.cs:314,318`) with no
  `enableRichText = false`, so the default applies, but a project-wide USS or `PanelSettings`
  override was not exhaustively ruled out.
- **Recommendation**: either set `name.enableRichText = false` on the two labels built in
  `RefreshRoster`, or extend `SanitizeNickname` to drop `<` and `>` alongside control characters.
  The second is better — it keeps the invariant at the trust boundary where the rest of it lives,
  and it protects every future consumer of `PlayerSlot.Nickname` (scoreboard, end screen) rather
  than just this one. Add a `ConnectionApprovalTests` case for it.
- **Effort**: S

### F-A7-7 — `NetDebugOverlay` ships in release builds

- **Severity**: Minor
- **Type**: Process / Maintainability
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/UI/NetDebugOverlay.cs:39-48`;
  `Assets/_Project/Scripts/UI/Snackdown.UI.asmdef` (no `defineConstraints`, no `includePlatforms`);
  `Assets/_Project/Scenes/Arena01.unity` (contains guid `e78a8fc9c6a97474c8adf9348101bbab`);
  `ProjectSettings/EditorBuildSettings.asset:15`; grep for `#if DEVELOPMENT_BUILD` / `#if UNITY_EDITOR`
  across `Assets/_Project/Scripts` returns **zero** hits
- **What it is**: confirmed, not assumed. The overlay component is instanced in `Arena01.unity`,
  that scene is in Build Settings, the assembly has no platform or define constraints, and there is
  no preprocessor gate anywhere in the first-party source. It therefore renders and its F1–F4
  handlers are live in a release player. `Unity.Multiplayer.Tools.NetworkSimulator.Runtime`
  (referenced at `Snackdown.UI.asmdef`) likewise has `includePlatforms: []` in its own asmdef, so it
  compiles into the player too.
- **What an attacker or ordinary player can actually do with it** — deliberately understated,
  because the honest answer is "very little":
  - **F1** flips `PredictedPlayer.PredictionEnabled`, a plain `static bool`
    (`PredictedPlayer.cs:158`). The server never reads it; with prediction off the owner still
    samples and still sends input (`PredictedPlayer.cs:312-334`). It makes your own character feel
    worse. **Not a cheat.**
  - **F2**/**F3** are local rendering toggles. **F4** writes a CSV to a fixed path under
    `Application.persistentDataPath`.
  - The only genuine leak is minor **information disclosure**: the panel prints every peer's
    `OwnerClientId` and role, and the server's per-player input-queue depth and starved-tick count
    (`NetDebugOverlay.cs:150,180-181`). It does **not** print other players' positions or life, so
    there is no wallhack.
- **Why it matters**: it is a polish and framing issue, not a security one — a shipped build with a
  permanent IMGUI debug panel reads as unfinished, and the pre-commit checklist in `CLAUDE.md`
  ("no leftovers") arguably already covers it. Reporting it as a cheat surface would be inaccurate.
- **Recommendation**: wrap `Update`/`OnGUI` in `#if DEVELOPMENT_BUILD || UNITY_EDITOR`, or add
  `"defineConstraints": ["UNITY_EDITOR || DEVELOPMENT_BUILD"]`-equivalent gating. Given the overlay
  *is* the demo (`README.md:57` tells a reviewer to press F1), keeping it in development builds and
  stripping it only from a hypothetical release build is the right trade — say so in `README.md`
  rather than deleting it.
- **Effort**: S

### F-A7-8 — `com.coplaydev.unity-mcp` tracks a git branch, not a release tag

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `Packages/manifest.json:3` (`…/unity-mcp.git?path=/MCPForUnity#main`);
  `Packages/packages-lock.json` (entry carries `"hash": "c14de1e6dc01ab42d2bb358730cff954bce0ce6b"`,
  and the file is tracked — confirmed via `git ls-files Packages/`);
  `Library/PackageCache/com.coplaydev.unity-mcp@a4c2d0a84573/Editor/…`
- **What it is**: an editor-automation package resolved from `#main`. Recon flagged it as a
  supply-chain observation; ranking it honestly requires two corrections in **opposite** directions.
  - *Downward*: `packages-lock.json` **is committed and does carry a commit hash**, so a fresh clone
    resolves the pinned commit rather than branch HEAD. The floating ref is not live on every
    checkout. This is materially less bad than "unpinned".
  - *Upward*: the package's capability surface is broad. Its Editor assembly spawns terminal
    processes (`Editor/Services/Server/TerminalLauncher.cs`), manages listening ports
    (`Editor/Helpers/PortManager.cs`), writes MCP client configuration into the user's home
    directory (`Editor/Helpers/McpConfigurationHelper.cs`, plus per-client configurators), touches
    OS credential stores (`Editor/Security/SecureKeyStore/{MacKeychain,LinuxSecretTool,EncryptedFile}KeyStore.cs`)
    and makes outbound HTTP requests (`Editor/Services/AssetGen/Http/UnityWebRequestTransport.cs`).
    Any deliberate package update pulls whatever is on `main` at that moment, with no version diff
    to review.
  - Separately, `Runtime/MCPForUnity.Runtime.asmdef` has `includePlatforms: []`, so eight helper
    files from this vendor compile into player builds. They are benign (compat shims, a screenshot
    utility, JSON converters) but they are third-party code in the shipping binary for no runtime
    reason.
- **Why it matters**: the exposure is developer-machine, not player-facing, and `target_ccu: 0`
  means there is no build pipeline to poison. It is a Minor process finding, ranked below every
  gameplay finding above.
- **Recommendation**: change the manifest entry from `#main` to the tag matching the currently
  resolved version (`10.1.0` per `package.json`), so the intent is visible in the manifest and not
  only in a generated lock file. Never re-run "Update" on it without reading the diff.
- **Effort**: S

### F-A7-9 — Input starvation repeats the last command forever, with no cap

- **Severity**: Minor
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:443-451`
- **What it is**: when the queue is empty the server reuses `_lastConsumedInput` indefinitely. The
  design rationale in the comment is sound (a hiccup should not freeze a running player), but there
  is no bound on how long the repeat continues, and `_highestReceivedInputTick` is never reset —
  not on round end, not on `ServerTeleport`, not on `ServerReset`.
- **How a scripted attacker performs it**: send one packet with the desired command and then stop
  transmitting entirely. The character keeps executing it at zero upstream cost. Also the mechanism
  that makes F-A7-1's freeze attack persistent rather than momentary.
- **Why it matters**: on its own it is not much of an advantage — the repeated command is still a
  legal one, and `StunStep` still zeroes input while stunned. It matters as an amplifier for
  F-A7-1 and as a small honesty gap in a system whose comments claim a hostile caller model.
- **Recommendation**: cap the repeat — after N starved ticks (say 15, half a second) decay to
  `default(InputCommand)` rather than repeating. `StarvedTicks` is already being counted
  (`PredictedPlayer.cs:448`), so the state is there.
- **Effort**: S

### F-A7-10 — A multi-byte nickname throws outside the `try` and hangs the join

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/Connection/DirectConnectionProvider.cs:114-119,255-259`;
  `Assets/_Project/Scripts/Connection/RelayConnectionProvider.cs:115-120,246-250`;
  `Assets/_Project/Scripts/Connection/ConnectionPayload.cs:29`;
  `Assets/_Project/UI/MainMenu.uxml:18` (`max-length="16"`)
- **What it is**: both providers truncate the nickname to `MaxNicknameLength = 16` **characters**
  and assign it to a `FixedString32Bytes`, which holds 29 **bytes** of UTF-8. Sixteen CJK characters
  (48 bytes) or emoji (64 bytes) overflow it, and `FixedString32Bytes`'s implicit conversion throws
  rather than truncating. The `ConnectionData` assignment sits *outside* the `try` in both files, so
  the exception escapes `JoinAsync` into `async void OnJoinClicked` — `SetBusy(false)` never runs
  and the menu is left permanently spinning.
- **Why it matters**: self-inflicted, not an attack, and it does not reach the server. It is worth
  fixing because it is a hang with no error message, in the first screen a reviewer sees.
  Confidence is Medium because I did not execute it; the reasoning is from the `Unity.Collections`
  contract and the code's own remark at `DirectConnectionProvider.cs:249-253`, which already
  identifies the throw but sizes the guard in characters.
- **Recommendation**: truncate by encoded byte length (`Encoding.UTF8.GetByteCount`) rather than by
  `string.Length`, and move the payload construction inside the `try`.
- **Effort**: S

### F-A7-11 — `ConnectionApprovalTests` covers neither of the two decisions approval actually makes

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `Assets/Tests/EditMode/ConnectionApprovalTests.cs` (9 tests, all against
  `SanitizeNickname` and `ConnectionPayload.TryRead`); `Assets/_Project/Scripts/Connection/ConnectionApproval.cs:145-184`
- **What it is**: the suite is honest about its own scope (`:9-13` — "the approval callback itself
  needs a live NetworkManager, so what is covered here is the pure half"). But the effect is that
  **character-index clamping, the player cap and the version gate have zero test coverage** —
  the roadmap's claim that the index is clamped is true by inspection (`ConnectionApproval.cs:183`,
  and again at `CharacterCatalog.cs:41`), but nothing in the repo verifies it.
- **Recommendation**: extract the three decisions into a pure static — `Decide(payload, connectedCount, maxPlayers, gameVersion, characterCount) -> (bool approved, string reason, string nickname, int index)`
  — and have `Approve` be a thin adapter over it. That makes all three testable without a
  `NetworkManager` and is the smallest seam that buys the coverage.
- **Effort**: S

### F-A7-12 — `CharacterCount` is never assigned and no character-selection UI exists

- **Severity**: Nit
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Connection/ConnectionApproval.cs:76` (`= 4`, no writer
  anywhere — grep for `CharacterCount` returns only the declaration and its two read sites);
  `Assets/_Project/Scripts/UI/MainMenuController.cs:186,197` (calls `ConnectionRequest.Host/Join`
  without a character index); `Assets/_Project/Scripts/Connection/ConnectionRequest.cs:24,31`
  (`characterIndex = 0` default)
- **What it is**: the clamp is real and correct, but currently vacuous — the shipped client always
  sends `CharacterIndex = 0`, and the clamp bound is a hardcoded 4 rather than
  `CharacterCatalog.Count`. Only a modified client ever exercises it. Noted here because the brief
  asked whether the roadmap's clamping claim holds: **it does**, and defence-in-depth at
  `CharacterCatalog.Get` means even a wrong `CharacterCount` cannot produce an out-of-range read.
- **Recommendation**: wire `_approval.CharacterCount = catalog.Count` where the approval is
  constructed (`MainMenuController.cs:135`) so the two cannot drift. The missing selection UI is
  A1's finding, not mine.
- **Effort**: S

### F-A7-13 — Version gate and UGS identifiers: what they are and are not

- **Severity**: Nit
- **Type**: Security
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Connection/ConnectionApproval.cs:172-178`;
  `ProjectSettings/ProjectSettings.asset:147,960,964`;
  `ProjectSettings/UnityConnectSettings.asset`; `ProjectSettings/Packages/com.unity.services.*/Settings.json`
- **What it is**: two things worth stating explicitly so they are not mistaken for findings later.
  - The version check compares a client-supplied string against `Application.version` (`0.1.0`). A
    modified client sets any string it likes, so this is **not** a security control — it is an
    anti-desync courtesy, which is exactly how the code and `docs` describe it. No action.
  - `cloudProjectId: 82acd7e3-…` and `organizationId: lucavalentini25` are committed in
    `ProjectSettings.asset`. These are **identifiers, not credentials** — Unity requires them in the
    build and they are normally tracked. The one real consequence, given anonymous sign-in
    (`RelayConnectionProvider.cs:181-184`), is that anyone who can build this repository can consume
    Relay/Lobby quota on the owner's UGS org. At `target_ccu: 0` and free-tier usage this is
    noise; it is worth knowing if the repository is public.
  - Genuinely clean: `com.unity.services.core/Settings.json` holds an empty environment,
    `com.unity.services.vivox/Settings.json` holds four empty strings (no `tokenKey`),
    `UnityConnectSettings.asset` has analytics, ads, crash reporting and performance reporting all
    `m_Enabled: 0`, and no `.dll`/`.exe`/`.so`/`.sh`/`.ps1` exists anywhere under `Assets/`.
- **Recommendation**: none required. If the GitHub repository is public, note in `README.md` that
  the UGS project id is the owner's and that reviewers should link their own.
- **Effort**: S

---

## Quantified Estimates

| # | Figure | Formula and inputs | Assumptions | Tag |
|---|---|---|---|---|
| 1 | Speed-hack ceiling: **127× move speed**, reached in **9.9 s** | `target = MoveX × MoveSpeed = 127 × 7 = 889 u/s`. `MoveTowards` climbs at `GroundAcceleration × dt = 90 × (1/30) = 3 u/s` per tick → `889 / 3 = 296 ticks = 9.9 s`. At 1 s: `30 × 3 = 90 u/s = 12.9×` normal. | `MovementConfig.asset` values (`MoveSpeed: 7`, `GroundAcceleration: 90`), tick 30 Hz, grounded, unobstructed. Terrain `BoxCast` still clamps actual displacement. | ESTIMATED (from code + committed asset values) |
| 2 | Forged-snapshot allocation amplification: **~10⁷×** | `PlayerSnapshot` ≈ 48 B managed (`ulong 8` + `PlayerState` 32 with padding + `uint 4` + `bool` 4). Attacker writes a 4-byte `count`; total crafted RPC ≈ 40 B. `count = 2²⁴` → `2²⁴ × 48 B = 805 MB` allocated per received packet. Ratio `805 MB / 40 B ≈ 2 × 10⁷`. | `SnapshotFrame.cs:68` allocates before reading elements; `RpcMessageHelpers.Handle` wraps the invoke in `try/catch`, so an `OutOfMemoryException` is caught and logged rather than crashing — the damage is sustained GC pressure and log spam, escalating with repetition. | ESTIMATED |
| 3 | Remote-freeze cost to the attacker: **~20 B/s** | `MaxInputTickLead = 64` ticks ÷ 30 Hz = **2.13 s** of victim input rejected per forged packet. Sustaining = `1 / 2.13 = 0.47 packets/s`; `InputPacket` = 3 × 6 B = 18 B payload + RPC header ≈ 40 B → `0.47 × 40 ≈ 19 B/s`. | `PredictedPlayer.cs:99,404,407`. Victim is fully controlled for the whole window because `ServerSimulateTick` falls to the starvation branch every tick. | ESTIMATED |
| 4 | Ready-flood: **~640 list events/s → ~7,700 `VisualElement` allocations/s per peer** | UTP reliable pipeline window = 32 in flight; at 50 ms RTT → `32 / 0.05 = 640 msg/s`. Each accepted call = 1 `m_DirtyEvents` entry = 1 `OnListChanged` per peer = 1 `RefreshRoster()`. Each rebuild allocates `3 elements × 4 rows = 12` → `640 × 12 ≈ 7,700/s`. | UTP reliable window of 32 assumed (not read from config); 4-player roster; 50 ms RTT. `NetworkList` coalesces the *packets* (one delta per tick) but **not** the events — `NetworkList.cs:97-100` writes all `m_DirtyEvents` and each is dispatched individually on receipt. | ESTIMATED |
| 5 | Steady-state snapshot receive allocation (legitimate traffic) | `4 players × 48 B = 192 B` per `new PlayerSnapshot[count]`, 30 times/s = **5.8 KB/s** of garbage per client. | Belongs to A4/A5; recorded here only because the same line is finding F-A7-2's attack vector. | ESTIMATED |

No profiler capture, bandwidth measurement, load test or CI workflow exists in the repository —
confirmed by searching for `.github/`, `*.prof`, `*.raw`, and any recorded metrics CSV. Every figure
above is derived from reading code and committed asset values. None is MEASURED.

---

## What is genuinely good here

Specific, cited, and not padding — the gameplay-authority half of this project is better than most
hobby netcode I could point at.

1. **No `NetworkTransform` anywhere.** Verified by resolving every `m_Script` guid in
   `Assets/_Project/Prefabs/Player.prefab`: `NetworkObject`, `InputReader`, `PredictedPlayer`,
   `CharacterAppearance`, `PlayerLife`, `VisualSmoother`. Position genuinely never crosses the wire
   in a client-writable component. This is the single most common way a "server-authoritative"
   Unity project is not one, and it is absent here.

2. **Peer collision is predicted without ever trusting the prediction.** `SweepPeers`
   (`PlayerMotor.cs:210-251`) operates on `PeerBody` values copied from a per-tick ring buffer, not
   on live references, and it only *limits the mover* — it never displaces the other body. The
   server captures its own world snapshot (`PredictedPlayer.cs:452`) and its answer overrides via
   reconciliation. The result is that the answer to "can a modified client shove another player?" is
   a clean **no**, and the design reasoning at `PlayerMotor.cs:200-209` is correct.

3. **The input ingest path already has three of the four checks it needs.** `EnqueueIfNew`
   (`PredictedPlayer.cs:400-416`) rejects replays and duplicates from the redundancy window, bounds
   future ticks against clock skew, and caps the queue with drop-oldest. `MaxQueueCapacity`'s
   remark (`:77-85`) correctly distinguishes latency-bounding from memory-bounding — a distinction
   most projects never notice. The missing fourth check is ownership, which is F-A7-1.

4. **`SetReadyRpc` reads the sender from `RpcParams`, and that is genuinely safe.** I verified the
   claim rather than accepting it: `RpcMessage.Handle` (`RpcMessages.cs:203-222`) explicitly
   discards the wire-supplied `SenderClientId` when the receiver is the server and substitutes the
   transport id. The remark at `SessionRoster.cs:128-132` is accurate.

5. **Every match-deciding write is authority-guarded, without exception.** `PlayerLife.ServerAdd`
   / `ServerReset` (`:181,194`), `Fruit.ServerSetKind` (`:48`), `PredictedPlayer.ServerApplyStun`
   / `ServerBounce` / `ServerTeleport` (`:628,635,684`), `MatchDirector.ServerStartMatch` /
   `ServerEndMatch` / `ServerReturnToLobby` (`:139,241,256`), `SessionRoster.ServerClearReady`
   (`:151`), `FruitSpawner.ServerDespawnAll` (`:138`). Not one is reachable from an RPC.

6. **`RoundReferee` replicates a verdict, not ingredients** (`:132-141`, and the reasoning at
   `:15-17`). A client cannot reach a different conclusion from the same numbers because it is never
   given the numbers to conclude from.

7. **Connection approval treats the payload as hostile in the ways it does check.**
   `ConnectionPayload.TryRead` (`:54-69`) is a total function over arbitrary bytes; the character
   index is clamped twice, independently (`ConnectionApproval.cs:183` and `CharacterCatalog.cs:41`);
   the nickname is truncated rather than rejected, with the reasoning written down; refusal reasons
   are written for a player rather than for a log (`:195-203`). `CharacterAppearance` reads the
   already-validated roster rather than replicating a second, disagreeable copy (`:10-14`).

8. **`MovementConfig` is never replicated** (`MovementConfig.cs:9-12`). The server's tuning values
   are its own, so a client editing its local asset changes nothing but its own prediction accuracy.

9. **Config and logging hygiene is clean.** Eleven `Debug.Log*` calls in 6,200 LOC, all on error or
   one-shot paths, none on a per-tick path, none logging player-identifying data. Analytics, ads,
   crash reporting and performance reporting are all disabled. No credentials in the tree. UGS
   anonymous sign-in with a per-instance profile derived from `Application.dataPath`
   (`RelayConnectionProvider.cs:218-222`) — a thoughtful solution to a real Multiplayer Play Mode
   problem, and it does not leak anything.

10. **Both third-party art packs are inert.** `find` over `Assets/Pixel Adventure 1` and
    `Assets/DEVNIK 2D` returns zero `.cs`, `.dll`, `.exe`, `.so`, `.sh`, `.ps1`, `.py` or `.jar`
    files. No editor scripts, no post-processors, nothing executable. `manifest.json` has exactly
    one non-registry source (F-A7-8) and no scoped registries.

**Over-engineering counter-check (this domain).** I looked and found **none**. There is no security
abstraction in this project at all — no validator interface, no anti-cheat strategy switch, no rate
limiter framework. Every check is an inline conditional at the point where the untrusted value
arrives, which is the correct shape at this scale. The failure mode here is entirely the opposite
one: **under-engineering**, at three precise points (F-A7-1, F-A7-2, F-A7-3), each of which is a
missing one-liner rather than a missing layer. Do not add a layer to fix them.

**On the honesty of the `PredictedPlayer.cs:82` acknowledgement.** The brief asked for a judgement.
The comment reads: *"Nothing here is protected by 'no real client would do that' — a ServerRpc
assumes the caller is hostile."* It is **honest about the hazard it names** — unbounded queue growth
— and the mitigation for that hazard genuinely exists and is correct (`MaxQueueCapacity`, checked at
`:413`). But the sentence is written as a general statement of the threat model, and as a general
statement it is **not backed**: the same RPC never checks *who* the caller is (F-A7-1) and never
checks *what the payload says* (F-A7-3). It is the right instinct, stated more broadly than the code
earns. After the two one-line fixes, the sentence becomes true and should be left exactly as
written.

---

## Open questions for the team

1. **Is the GitHub repository public?** It changes nothing about the findings above except the
   framing of F-A7-13 — a public repo means the committed `cloudProjectId` / `organizationId` let
   anyone build a client that consumes the owner's UGS Relay quota. I could not determine this
   offline.
2. **Should the netcode layer demonstrate anti-cheat, or just server authority?** A portfolio piece
   that fixes F-A7-1/2/3 and then *writes down* in `docs/02-netcode.md` what a hostile client can and
   cannot do — with the exact attribute that closes each hole — is a stronger interview artefact than
   one that is merely correct. That is a scope decision, not an audit finding.
3. **Is `docs/02-netcode.md:15-16` ("the worst a cheater can do is move legally") meant as a
   statement of the design goal or of the current implementation?** It reads as the latter. If the
   three fixes land it becomes true; if they do not, that sentence needs rewording in the same
   commit, per the `CLAUDE.md` rule that a doc disagreeing with the code is worse than no doc.
4. **Does the demo need `NetDebugOverlay` in a release build?** `README.md:57` instructs a reviewer
   to press F1, so stripping it from *all* builds would break the documented review path. My
   recommendation assumes development builds are what reviewers run — confirm that.
5. **Is there any intent to ship to Mobile or WebGL** (listed in `target_platforms`)? It would not
   change any finding here, but WebGL forces WebSocket transport, which changes the shape of the
   connection front door — worth knowing before F-A7-5 is fixed.
