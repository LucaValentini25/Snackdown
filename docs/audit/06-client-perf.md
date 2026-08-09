# Client Performance Audit

**Agent:** A6 · **Branch:** `dev` · **Commit:** `10a2a13` · **Date:** 2026-08-08

## Verdict

The gameplay hot path is genuinely well built — allocation-free simulation, ring buffers instead of
lists, a snapshot scratch array reused across ticks, and a reconciliation replay that costs about
**0.03 ms at 150 ms RTT**, or 0.2% of a 16.6 ms frame. Nothing in the simulation, the physics load
or the `Update` count threatens 60 fps on any plausible device. The real finding in this domain is
not a frame-budget one: **two of the three named target platforms do not exist.** WebGL cannot open
a connection at all (`m_UseWebSockets: 0`, Relay allocated without a `wss` connection type, and the
LAN provider calls `Dns.GetHostAddressesAsync`, which WebGL has no sockets for), and Mobile has no
input path whatsoever (`InputReader` builds Keyboard and Gamepad bindings in code and nothing else).
There are **zero preprocessor directives in the entire first-party codebase**, no build profiles, no
CI, and `docs/03-roadmap.md` still lists "a runnable build" as unchecked — so no build has ever been
produced for anything. The one item that *is* a frame-budget risk is `NetDebugOverlay`: it ships to
players on every platform with no stripping, and its IMGUI pass costs an estimated **~5 KB of garbage
per `OnGUI` event and 1–3 ms of text generation per repaint on a mid-range phone**.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| Hot-path allocation discipline | 5 | `PredictionBuffer`, `WorldSnapshotBuffer`, `_snapshotScratch`, `_worldScratch`, `Fruit._overlaps` — all pre-allocated and reused, with the reasoning written down |
| Frame-time headroom on PC | 5 | ~30 `Update`/`LateUpdate` callbacks, ≤14 physics queries per frame, one replay of ≤5 ticks per correction |
| Frame-time headroom on the weakest named target | 2 | Not measurable — no mobile or WebGL build exists. The only identified risk (IMGUI overlay) is unstripped |
| Mobile readiness | 0 | No touch input, no on-screen controls, no build profile, no platform gating, no aspect/DPI work |
| WebGL readiness | 0 | Transport cannot connect; `Dns`/`Task.Delay` code paths are WebGL-hostile; no `wss` Relay type |
| Asset pipeline (compression, atlasing, streaming) | 2 | No sprite atlas, textures uncompressed at `maxTextureSize: 2048`; harmless today at 0.70 MB of used sprites, unmanaged as content grows |
| Release-build hygiene | 1 | Debug overlay + `NetworkSimulator` tools component ship in every build; player settings are unedited URP-template defaults carrying `Final Redes` leftovers |
| Measurement | 1 | No profiler capture, no frame-time data, no memory capture anywhere in the repo. Every number below is `ESTIMATED` |

## Findings

### F-A6-1 — Mobile and WebGL are named targets with no evidence of ever having been built

- **Severity**: Blocker
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: No `*.buildprofile` asset anywhere in the tree (searched, none found); `.github/`
  contains only `PULL_REQUEST_TEMPLATE.md` — no workflows; `ProjectSettings/ProjectSettings.asset:846`
  `scriptingDefineSymbols: {}`; **zero** `#if` directives across `Assets/_Project` and `Assets/Tests`
  (grepped for `#if|#endif|UNITY_WEBGL|UNITY_ANDROID|UNITY_IOS|DEVELOPMENT_BUILD|Conditional` —
  no hits); `docs/03-roadmap.md:105` lists "Final architecture diagrams; **a runnable build**" as
  unchecked; `docs/03-roadmap.md` contains no mobile, touch, WebGL or platform item in any phase.
- **What it is**: The owner names PC, Mobile and WebGL as target platforms. The repository contains
  no build profile, no platform-conditional code, no CI build job, and a roadmap that never mentions
  either non-PC platform. `ProjectSettings.asset` is the unedited URP-2D-template default —
  `companyName: DefaultCompany` (line 15), `Android: com.UnityTechnologies.com.unity.template.urpblank`
  (line 170), `metroPackageName: Final Redes` / `metroApplicationDescription: Final Redes`
  (lines 882, 889), which are leftovers from a different project entirely.
- **Why it matters**: Two thirds of the stated platform surface is aspiration, not implementation.
  In an interview the gap is the question that gets asked ("you said WebGL — where's the build?"),
  and the honest answer today is that nobody has tried. Every downstream finding in this file
  (F-A6-2 through F-A6-6) is a specific instance of the same absence.
- **Recommendation**: Either drop Mobile and WebGL from the stated targets and say so in `README.md`
  — a PC-only portfolio netcode project is entirely defensible — or add one item per platform to
  `docs/03-roadmap.md` Phase 5 and produce one build for each. Do not leave the claim standing with
  nothing behind it.
- **Effort**: S to drop the claim; L to make WebGL real; XL to make Mobile real.

### F-A6-2 — A WebGL build cannot establish a connection by either provider

- **Severity**: Critical
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scenes/Bootstrap.unity:407-408` — `m_ProtocolType: 0`,
  `m_UseWebSockets: 0`; `Assets/_Project/Scripts/Connection/RelayConnectionProvider.cs:74-79` —
  `new SessionOptions { MaxPlayers = _maxPlayers }.WithRelayNetwork()` with no connection-type
  argument; `Assets/_Project/Scripts/Connection/DirectConnectionProvider.cs:232` —
  `await System.Net.Dns.GetHostAddressesAsync(target)`;
  `Assets/_Project/Scripts/Connection/DirectConnectionProvider.cs:147` — `Task.Delay(...)`.
- **What it is**: Three independent blockers, all in the connection path.
  1. `UnityTransport.UseWebSockets` is `false` in the only scene that carries a `NetworkManager`.
     A browser cannot open a raw UDP socket, so this alone makes every WebGL connection fail.
  2. `WithRelayNetwork()` with no argument allocates a `dtls`/`udp` Relay connection. WebGL requires
     `WithRelayNetwork("wss")`. Relay is not optional on WebGL — it is the *only* way a browser peer
     can reach a host — so this is on the critical path, not a fallback.
  3. `DirectConnectionProvider` is unusable on WebGL regardless: `Dns.GetHostAddressesAsync` has no
     implementation in the browser, and UDP hosting is impossible. LAN play is PC-only by
     construction, which is fine — but nothing in the code or the UI says so, so a WebGL player would
     be offered a "join by address" field that throws.
- **Why it matters**: "One identical join flow over LAN and over the internet" is core pillar 3.
  On WebGL neither half of that flow works, and the failure is a runtime exception in an `async`
  method rather than a message a player can act on.
- **Recommendation**: Three small changes, in this order. Set `UseWebSockets` from code in
  `RelayConnectionProvider` rather than in the scene (the scene value is shared with the Direct
  provider, which must not use WebSockets); pass `WithRelayNetwork("wss")` when the build is WebGL;
  and hide or disable the Direct provider on WebGL. All three want the platform gating that F-A6-1
  says does not exist yet.
- **Effort**: M

### F-A6-3 — Mobile has no input path at all

- **Severity**: Critical
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Input/InputReader.cs:29-48` — the only bindings created are
  `<Keyboard>/a`, `<Keyboard>/d`, `<Keyboard>/leftArrow`, `<Keyboard>/rightArrow`,
  `<Gamepad>/leftStick/x`, `<Gamepad>/dpad/left|right`, `<Keyboard>/space`, `<Keyboard>/w`,
  `<Keyboard>/upArrow`, `<Gamepad>/buttonSouth`. `InputReader.cs:16-18` states outright that actions
  are built in code and no `.inputactions` asset is used. `Assets/InputSystem_Actions.inputactions`
  does contain a `<Touchscreen>/primaryTouch/tap` binding (line 281) — but only on the template's
  unused `Attack` action, and gameplay never reads that asset.
  `Assets/_Project/Scripts/UI/NetDebugOverlay.cs:41-47` reads `Keyboard.current` directly for F1–F4.
- **What it is**: On a phone, `Keyboard.current` and `Gamepad.current` are both `null`.
  `InputReader.MoveX` returns 0 and `JumpHeld` returns false forever; the character stands still
  while its life timer drains. There is no on-screen stick, no `OnScreenButton`, no touch composite,
  and no UI Toolkit touch control anywhere in `Assets/_Project/UI/`.
- **Why it matters**: The game is not merely uncomfortable on mobile — it is unplayable, and the
  failure mode is silent. The player joins a match, watches themselves lose, and has no indication
  why.
- **Recommendation**: This is the largest single item behind the mobile claim. If mobile stays a
  target, an on-screen control layer plus a `Touchscreen` binding set is a phase of its own; the
  `InputReader` seam is already in the right place for it (`InputReader.cs:17-18` explicitly says so),
  so the change is contained. If mobile is dropped, this finding disappears with F-A6-1.
- **Effort**: L

### F-A6-4 — `NetDebugOverlay` and the `NetworkSimulator` tools component ship to players

- **Severity**: Major
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scenes/Arena01.unity:498-507` — `NetDebugOverlay` is a
  `MonoBehaviour` with `m_Enabled: 1` on a scene GameObject;
  `Assets/_Project/Scenes/Bootstrap.unity:495` — `Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulator`
  is a component in the boot scene; `Assets/_Project/Scripts/UI/Snackdown.UI.asmdef` references
  `Unity.Multiplayer.Tools.NetworkSimulator.Runtime`;
  `Library/PackageCache/com.unity.multiplayer.tools@acc3b25185bb/NetworkSimulator/Runtime/Unity.Multiplayer.Tools.NetworkSimulator.Runtime.asmdef`
  declares `"includePlatforms": []` and `"excludePlatforms": []` with no `DEVELOPMENT_BUILD`
  constraint; `NetDebugOverlay.cs` contains no `#if` of any kind; `NetDebugOverlay.cs:25` —
  `bool _visible = true`.
- **What it is**: Nothing strips either. The overlay is enabled by default, the F1–F4 hotkeys are
  live, and `Unity.Multiplayer.Tools.NetworkSimulator.Runtime` is compiled into the player on every
  build target. `PredictedPlayer.PredictionEnabled` and `VisualSmoother.SmoothingEnabled` are public
  static fields (`PredictedPlayer.cs:158`, `VisualSmoother.cs:30`) that any player can toggle with a
  keypress; F4 writes a CSV to `Application.persistentDataPath`.
- **Why it matters**: Two separate consequences. For a *demo* build this is arguably correct and
  `README.md:57-60` documents the hotkeys as instructions to the person watching — that is a defensible
  choice and I am not calling it a mistake. For a *player-facing* build it is a debug HUD nobody can
  turn off, a tools package in the shipping binary, and a global kill switch for prediction bound to
  a function key. The problem is that there is currently no way to have one without the other, because
  there is one build configuration and it is unnamed.
- **Recommendation**: Wrap the overlay's `Update`, `OnGUI` and the `NetworkSimulator` reference in
  `#if DEVELOPMENT_BUILD || UNITY_EDITOR`, or move `NetDebugOverlay` into its own assembly with
  `defineConstraints: ["DEVELOPMENT_BUILD"]`. The second is cleaner and removes the
  `Snackdown.UI → Multiplayer.Tools` reference the recon flagged. Keep the overlay on in the demo
  build — that is the point of it — and make "demo build" a thing that exists.
- **Effort**: S

### F-A6-5 — The overlay's `OnGUI` pass allocates every frame and generates text every repaint

- **Severity**: Major
- **Type**: Performance
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/UI/NetDebugOverlay.cs:123-208`. Interpolated strings at lines
  133, 134, 135, 136, 139, 154, 155, 161, 162, 163, 170, 171, 172, 175, 190, 191, 197 — 18 of them for
  a 4-player client. `NetDebugOverlay.cs:203` — `_content.text = _text.ToString()` allocates the full
  panel string. `NetDebugOverlay.cs:204` — `_style.CalcHeight(_content, LabelWidth)` runs a text
  generation pass. Guarded only by `_visible` and `IsListening` (line 126), both true for all of
  gameplay. `ProjectSettings/ProjectSettings.asset:876` — `gcIncremental: 1`.
- **What it is**: The author clearly tried — `_text` is a reused `StringBuilder` (line 26) and
  `_content` a reused `GUIContent` (line 29), with a comment saying why. But every line inside is
  `$"..."`, which under Unity 6's C# 9 compiler lowers to `string.Format(string, object[])`: an
  `object[]` per call plus a box per value-type argument plus the result string. Unity dispatches
  `OnGUI` at least twice per frame (`Layout` then `Repaint`).
- **Why it matters**: See the Quantified Estimates table — an estimated **~5 KB per `OnGUI` event,
  ~600 KB/s at 60 fps**. On PC that is noise. On WebGL, where Unity's incremental GC is not available
  and the heap starts at `webGLInitialMemorySize: 32` MB (`ProjectSettings.asset:837`), that is a
  stop-the-world Boehm collection every few seconds, each one a visible hitch. The text-generation
  pass in `CalcHeight` plus `GUI.Label` is the larger cost on a phone: IMGUI has no batching and
  regenerates ~700 glyph quads per repaint.
- **Recommendation**: Two lines of defence, both cheap. Rebuild `_text` only on `Event.current.type
  == EventType.Repaint` instead of on every event — that halves everything immediately. Then rebuild
  it at a fixed 4–10 Hz rather than per frame; the numbers on it are not readable faster than that
  anyway. If F-A6-4 is done, none of this ships to players and the severity drops to a demo-build
  concern.
- **Effort**: S

### F-A6-6 — The documented `runInBackground` fix does not exist on Mobile or WebGL

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `docs/05-validation.md:39` — *"Stutter that follows whichever window you are not
  using | `runInBackground` off — Unity throttles an unfocused window, so the other peer stops
  ticking | **Now on.** See [ProjectSettings]."*; `ProjectSettings/ProjectSettings.asset:86` —
  `runInBackground: 1`; `Assets/_Project/Scenes/Bootstrap.unity` NetworkManager — `RunInBackground: 1`.
  No `Application.runInBackground` assignment exists anywhere in `Assets/_Project` (grepped).
- **What it is**: `Application.runInBackground` is a desktop-standalone setting. Android and iOS
  suspend a backgrounded app regardless of it; WebGL is throttled by the browser, which stops firing
  `requestAnimationFrame` in a hidden tab and cannot be overridden from inside the page. The
  validation doc records this as a solved problem, and on PC it is. On the other two named platforms
  the underlying failure is fully intact and unmitigated.
- **Why it matters**: The topology is client-host and settled — so the host is a player's device. A
  phone host that takes a call, or a browser host whose tab goes to the background, stops ticking:
  every client's input queue starves (`PredictedPlayer.cs:448` `StarvedTicks++`), the server repeats
  the last input it had, and everyone's prediction diverges until the host comes back. This is a
  documented remedy that silently covers one platform of three, which is worse than an undocumented
  gap because nobody will look at it again.
- **Recommendation**: Add a sentence to `docs/05-validation.md` scoping the fix to standalone builds,
  and — if either platform survives F-A6-1 — decide what a suspended host means. A "host paused"
  overlay driven by `OnApplicationPause`/`OnApplicationFocus`, with the match clock held, is the
  smallest honest answer.
- **Effort**: S for the doc; M for the behaviour.

### F-A6-7 — A slow or stuck client blocks the countdown for everyone, with no timeout of its own

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Match/MatchDirector.cs:189-201` — `OnLoadComplete`
  adds the reporting client and returns early unless *every* entry in
  `NetworkManager.ConnectedClientsIds` is in `_loaded`; `MatchDirector.cs:207-218` — the only escape
  is `OnClientLeft`, i.e. an actual disconnect; `Assets/_Project/Scenes/Bootstrap.unity` —
  `LoadSceneTimeOut: 120`.
- **What it is**: The gate is deliberate and the reasoning in `MatchDirector.cs:18-23` is correct —
  starting on the *server's* load would hand the host a head start. But the only two ways out of
  `MatchPhase.Loading` are all clients reporting, or a client disconnecting. A client that is merely
  *slow* — not gone — holds the other three on the loading screen indefinitely. NGO's
  `LoadSceneTimeOut: 120` raises `OnLoadEventCompleted` with a timed-out client list; this code
  subscribes only to the per-client `OnLoadComplete` (`MatchDirector.cs:105`), which does not fire
  for a client that timed out.
- **Why it matters**: This is where mobile and WebGL load times land. A phone with a cold shader
  cache, or a browser still compiling wasm, is exactly the "slow but not gone" case. On PC-to-PC the
  spread is small enough that nobody has hit it; across the three named platforms it is the normal
  case, not the edge case.
- **Confidence note**: Medium because I am reasoning about NGO 2.11's `OnLoadComplete` /
  `OnLoadEventCompleted` semantics from the API contract rather than from a test in this repo. The
  code-level fact — that only two paths leave `Loading` — is High confidence.
- **Recommendation**: Subscribe to `NetworkManager.SceneManager.OnLoadEventCompleted` alongside
  `OnLoadComplete` and start the countdown when it fires, treating timed-out clients as either
  disconnected or spectators. Independently, a server-side wall-clock deadline on `MatchPhase.Loading`
  would make the failure bounded regardless of what NGO reports.
- **Effort**: S

### F-A6-8 — `EndScreenController.Update` runs a scene-wide object search and allocates strings every frame

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/UI/EndScreenController.cs:61` — `_title.text = TitleFor(referee)`
  called from `Update`; `EndScreenController.cs:76` — `$"{NameOf(referee.WinnerClientId)} wins"`;
  `EndScreenController.cs:94` — `NameOf` calls `FindFirstObjectByType<SessionRoster>()`;
  `EndScreenController.cs:99` — `roster[i].Nickname.ToString()` converts a `FixedString` to a managed
  string. All of it runs once per frame for the entire duration of `MatchPhase.Ended`.
- **What it is**: The reconciler pattern here is right (`EndScreenController.cs:13-16` explains why a
  listener would be wrong), but the *result* is recomputed from scratch every frame instead of when
  the phase or the winner changes. `FindFirstObjectByType` walks the loaded object graph;
  `FixedString.ToString()` and the interpolation each allocate.
- **Why it matters**: Small in absolute terms (~200 bytes/frame, ~12 KB/s, plus one object-graph
  walk) and confined to the end screen — but the end screen is a static image the player stares at
  for several seconds, which is the one moment where a per-frame scene search buys literally nothing.
  It is also the only place in the project that does this; everywhere else the author was careful
  (`PlayerLife.cs:76-81` explicitly rejects `FindObjectsByType` at frame rate, with the reasoning
  written out).
- **Recommendation**: Cache the winner's display name and the resolved `SessionRoster` when the phase
  first becomes `Ended`, and reassign only when `RoundReferee.Current`'s winner changes. Keep the
  per-frame *visibility* reconcile — that part is doing real work.
- **Effort**: S

### F-A6-9 — Fruit is instantiated and destroyed rather than pooled

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Gameplay/Fruits/FruitSpawner.cs:91` —
  `NetworkObject instance = Instantiate(_fruitPrefab, point.position, Quaternion.identity)`;
  `FruitSpawner.cs:99` — `instance.Spawn()`; `Assets/_Project/Scripts/Gameplay/Fruits/Fruit.cs:85` —
  `NetworkObject.Despawn()`. No `INetworkPrefabInstanceHandler` implementation exists in the project
  (grepped); `FruitSpawner.cs:112` also allocates `new List<Transform>(_spawnPoints.Length)` on every
  spawn attempt.
- **What it is**: Every fruit is a fresh `GameObject` + `NetworkObject` registration on the server and
  a fresh instantiate-on-spawn-message on every client, and a `Destroy` on collection. NGO's
  `INetworkPrefabInstanceHandler` is the supported pooling seam and is unused.
- **Why it matters**: Bounded and small — `_interval = 4f` and `_maxActive = 6` (`FruitSpawner.cs:31,34`)
  cap this at one instantiate every 4 seconds. See the estimates table: **~0.25 spawns/s, ~15 spawn
  hitches per minute of ~0.2–0.5 ms each on a phone**. That is not a 60 fps problem. It is listed
  because it is the one place where the "no allocation on the hot path" discipline visible everywhere
  else in this codebase does not apply, and because the same shape at a shorter interval or a larger
  arena would become one.
- **Recommendation**: Leave it. At 0.25 spawns/s the pool is not earned, and adding one would be
  over-engineering by rubric #7. Revisit only if `_interval` drops below ~0.5 s or fruit count rises.
  The `new List<Transform>` in `FreeSpawnPoint` can become a reused field for free if it is ever
  touched for another reason.
- **Effort**: S (and my recommendation is not to spend it)

### F-A6-10 — Every received snapshot allocates a `PlayerSnapshot[]` on the client

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scripts/Netcode/SnapshotFrame.cs:70` —
  `if (serializer.IsReader) Players = new PlayerSnapshot[count];`
- **What it is**: The *send* side is allocation-free — `NetworkSimulationLoop.cs:104-108` reuses
  `_snapshotScratch` and only reallocates when the player count changes, with the intent clearly
  deliberate. The *receive* side allocates a fresh array on every deserialization, 30 times a second
  per client, because `INetworkSerializable` gives the reader no place to put a pooled buffer.
- **Why it matters**: ~216 bytes per snapshot × 30 Hz = **~6.3 KB/s** of garbage on each client. This
  is genuinely small — an order of magnitude below the overlay in F-A6-5 — and on PC it is invisible.
  It is worth naming only because it is the one asymmetry in an otherwise symmetric allocation-free
  design, and because it is the kind of thing an interviewer notices.
- **Recommendation**: Keep a `PlayerSnapshot[]` field on the reading side and only reallocate when
  `count` exceeds its length, mirroring what `NetworkSimulationLoop` already does for sending. Four
  lines. Alternatively document the asymmetry as an accepted cost of `INetworkSerializable`, which is
  also a defensible answer.
- **Effort**: S

### F-A6-11 — `PredictionBuffer.Capacity = 1024` sizes the worst-case replay at 1024 ticks in one frame

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/Netcode/PredictionBuffer.cs:21` — `public const int Capacity = 1024`
  with the comment *"~34 seconds at 30 Hz. Far more than any recoverable desync needs."*;
  `Assets/_Project/Scripts/Gameplay/Player/PredictedPlayer.cs:542-551` — replay is skipped only when
  `pendingTicks > PredictionBuffer.Capacity`, so the largest replay actually executed is exactly
  `Capacity` ticks; `PredictedPlayer.cs:565-573` — the replay loop calls `PlayerMotor.Simulate`,
  which issues up to 2 `Physics2D.BoxCast` per tick (`PlayerMotor.cs:137, 162, 188`).
  For contrast `WorldSnapshotBuffer.cs:26` uses `Capacity = 128` with the reasoning that replays only
  span a fraction of a second — the two constants disagree about the same thing.
- **What it is**: The comment is right that 34 s of history is more than any replay needs, but the
  capacity is not just history — it is also the ceiling on how much work one correction can do inside
  one frame. A client that stalls for up to 34 seconds and resumes will replay up to 1024 ticks
  synchronously inside `SnapshotRpc`, on the main thread.
- **Why it matters**: See the estimates table: **~20 ms on a mid-range phone, ~4 ms on PC** for the
  worst case — one dropped frame, bounded, non-recurring. This is genuinely not severe; the design
  already caps it, which is more than most implementations do. The finding is that the cap is set by
  a constant chosen for a different purpose, and `WorldSnapshotBuffer` (128) has already concluded the
  right number is ~8× smaller.
- **Recommendation**: Either reduce `Capacity` to 128–256 to match `WorldSnapshotBuffer` — beyond
  ~4 seconds the replay is fiction anyway, since `WorldSnapshotBuffer` returns
  `SimulationContext.Empty` for ticks it no longer holds (`WorldSnapshotBuffer.cs:63`), so replays
  past 128 ticks already re-simulate against an empty world — or, better, keep the capacity and add a
  separate `MaxReplayTicks` constant so the two concerns are named separately. The second option is
  the more legible one for a portfolio, and the XML doc writes itself.
- **Effort**: S

### F-A6-12 — Sprites are uncompressed, unatlased, and capped at 2048; harmless today, unmanaged

- **Severity**: Nit
- **Type**: Performance
- **Confidence**: High
- **Evidence**: 224 of 233 `platformSettings` entries across the two art packs specify
  `textureCompression: 0` (Uncompressed); all specify `maxTextureSize: 2048` and
  `crunchedCompression: 0` (e.g. `Assets/Pixel Adventure 1/Assets/Background/Blue.png.meta`).
  No `*.spriteatlas` or `*.spriteatlasv2` asset exists anywhere in the project (searched);
  every `.png.meta` has `spritePackingTag:` empty. `ProjectSettings/ProjectSettings.asset:55` —
  `mipStripping: 0`; QualitySettings has `streamingMipmapsActive: 0` on both quality levels.
- **What it is**: 114 textures across the two packs. **Only 12 are referenced by any first-party
  scene, prefab, `.asset`, `.uxml` or `.uss`** — the 8 fruit sprites and 4 character idle sheets.
  Uncompressed, those 12 total **0.70 MB of RGBA32**. The other 102 are unreferenced and therefore
  will **not** be included in a player build — Unity only ships assets reachable from a scene in
  build settings, a `Resources/` folder, or an addressable group, and this project has none of the
  latter two (`Assets/TextMesh Pro/Resources` is the only `Resources` directory and is package
  content). I checked this rather than assuming it, because "3.3 MB + 1.5 MB of art ships to a phone"
  is the finding one would expect here and it is not true.
- **Why it matters**: Almost not at all, today. 0.70 MB of VRAM and ~12 extra draw calls from
  unatlased sprites is nothing on any target. The costs that are real are editor-side: 637 files under
  `Pixel Adventure 1/` and 6 under `DEVNIK 2D/` are tracked in git for 12 used sprites, which is
  import time and clone size, not runtime.
- **Recommendation**: Nothing urgent. When Phase 4 adds a second arena and animation frames, create
  one `SpriteAtlas` for gameplay sprites and one for UI before the count grows, and set a Mobile/WebGL
  texture override to compressed (ETC2/ASTC on Android, DXT on WebGL) with `maxTextureSize` at 512 —
  uncompressed is the right call for pixel art on PC and the wrong one for a phone's memory budget.
  Deleting the 102 unused textures would shrink the repo but is a judgement call about keeping the
  pack intact.
- **Effort**: S

### F-A6-13 — `targetFrameRate = 60` with `vSyncCount = 0` is a desktop policy applied globally

- **Severity**: Minor
- **Type**: Performance
- **Confidence**: Medium
- **Evidence**: `Assets/_Project/Scripts/Core/FrameRatePolicy.cs:28-33` — a
  `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` that sets `QualitySettings.vSyncCount = 0` and
  `Application.targetFrameRate = 60` unconditionally on every platform.
  `ProjectSettings/QualitySettings.asset` — `vSyncCount: 0` on both the `Mobile` and `PC` levels
  already.
- **What it is**: The reasoning in the XML docs (`FrameRatePolicy.cs:8-22`) is one of the better
  comments in the repo and the *diagnosis* it records is real — `docs/05-validation.md:40` traces a
  65× correction-rate inflation to two uncapped renderers on one CPU. But the policy is applied
  everywhere, and the two settings behave differently off desktop.
  - `QualitySettings.vSyncCount` has no effect on Android, iOS or WebGL; the platform's own
    presentation controls the interval. Clearing it there is a no-op, not a fix.
  - `Application.targetFrameRate = 60` on a 90 Hz or 120 Hz phone display does not divide evenly into
    the refresh interval, producing uneven frame pacing — visibly worse than either 60 on a 60 Hz
    panel or an uncapped 90.
  - On WebGL, `targetFrameRate` is ignored in favour of `requestAnimationFrame` unless explicitly
    opted out; the cap simply does not apply.
- **Why it matters**: Modest. The comment's core claim — "60 is double the 30 Hz tick, so every step
  is presented at least twice" — remains sound, and 60 fps on a phone for a game this light is not a
  meaningful battery concern. The issue is that a carefully justified desktop decision is presented
  as a universal one, and on two of the three named platforms one half of it is inert and the other
  half is mildly harmful.
- **Recommendation**: Scope it. `Application.targetFrameRate = Mathf.Max(60, Screen.currentResolution.refreshRateRatio)`
  rounded to a divisor of the panel rate on mobile, and leave WebGL to the browser. One `#if` and
  three lines, and the XML doc gains the one paragraph that makes it complete.
- **Effort**: S

### F-A6-14 — No profiler capture, frame-time measurement or memory capture exists in the repository

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `git ls-files` matching `\.csv|\.raw|profil|metric|benchmark|perf` returns only
  `Assets/Settings/DefaultVolumeProfile.asset` and `Assets/Settings/SampleSceneProfile.asset` —
  URP volume profiles, not profiler data. No `.data`/`.raw` capture anywhere in the tree.
  `.gitignore` ignores `/[Mm]emoryCaptures/` and `/[Rr]ecordings/` but neither directory exists.
  `Assets/_Project/Scripts/UI/NetDebugOverlay.cs:77` writes run CSVs to
  `Application.persistentDataPath`, outside the repository. `docs/05-validation.md` measures
  reconciliation and RTT only — its `Scenario C` is explicitly labelled *observed, not measured*.
  `ProjectSettings/ProjectSettings.asset:583` — `enableInternalProfiler: 0`.
- **What it is**: I confirmed the absence rather than assuming it. The project has real measurement
  discipline in the *network* domain — `RunRecorder`, `ReconciliationStats`, a validation doc with
  network profiles and a table of what turned out to be measuring the wrong thing. None of it touches
  frame time, GC, or memory.
- **Why it matters**: Every number in this file is `ESTIMATED`, and it has to be. It also means the
  claim in `FrameRatePolicy.cs` and `docs/05-validation.md:40` that uncapped rendering starved the
  tick was diagnosed from correction counts rather than from a profiler capture — the conclusion is
  almost certainly right, but the evidence for it is indirect.
- **Recommendation**: One Profiler capture of a 4-peer Multiplayer Play Mode session, saved to
  `docs/`, would convert most of this file from estimate to measurement and is maybe an hour of work.
  The GC Alloc column alone would settle F-A6-5 and F-A6-10 outright.
- **Effort**: S

## Quantified Estimates

Every row is `ESTIMATED` — no profiler data exists in the repository (F-A6-14). Device assumptions,
stated once and used throughout:

- **PC**: desktop x86, the machine the project is developed on.
- **Mid-range phone**: ARM, 2021–2023 mid-tier, IL2CPP/ARM64, assumed **~3× the per-operation cost of
  the PC** for physics queries and text generation.
- **WebGL**: wasm, single-threaded (`ProjectSettings.asset:832` `webGLThreadsSupport: 0`), assumed
  **~2× PC cost** for engine-native work.
- **Tick**: 30 Hz → 33.33 ms/tick (`Bootstrap.unity` `TickRate: 30`).
- **Frame budget at 60 fps**: 16.6 ms (`FrameRatePolicy.TargetFrameRate = 60`).
- **`Physics2D.BoxCast` in a sparse 2D scene**: 3 µs PC / 10 µs phone / 6 µs WebGL.

### Reconciliation replay cost

Formula: `pendingTicks = ceil(RTT_ms / 33.33)`; `cost = pendingTicks × 2 BoxCasts × castCost`.
Source: `PredictedPlayer.cs:540` computes `pendingTicks` exactly this way from
`_latestPredictedTick - ackTick`; `PredictedPlayer.cs:565-573` is the replay loop;
`PlayerMotor.cs:137,162` are the two casts per `Simulate`.

| Scenario | pendingTicks | PC | Mid-range phone | WebGL | % of 16.6 ms (phone) | Tag |
|---|---:|---:|---:|---:|---:|---|
| 50 ms RTT (LAN / fiber) | 2 | 0.012 ms | 0.040 ms | 0.024 ms | 0.24% | ESTIMATED |
| **150 ms RTT (the brief's case)** | **5** | **0.030 ms** | **0.100 ms** | **0.060 ms** | **0.60%** | ESTIMATED |
| 367 ms RTT (measured in `docs/05-validation.md:78`) | 12 | 0.072 ms | 0.240 ms | 0.144 ms | 1.4% | ESTIMATED |
| 520 ms RTT (Mobile 2G profile) | 16 | 0.096 ms | 0.320 ms | 0.192 ms | 1.9% | ESTIMATED |
| Worst case the code permits (`PredictionBuffer.Capacity`) | 1024 | 6.1 ms | 20.5 ms | 12.3 ms | 123% | ESTIMATED |

**Answer to the brief's question: at 150 ms RTT a correction costs ~0.1 ms on a mid-range phone and
fits inside a 16.6 ms budget with 165× headroom.** Reconciliation is not a frame-time risk at any
realistic latency. The only case that exceeds a frame is the 1024-tick ceiling in F-A6-11, which
requires a ~34-second stall to reach, happens once, and is already bounded by design.

### Garbage generation per client

| Source | Formula | Rate | Tag |
|---|---|---:|---|
| `NetDebugOverlay.OnGUI` | 18 interpolated strings × (~40 B `object[]` + ~24 B/box + ~90 B result) + `StringBuilder.ToString()` ~1.8 KB ≈ **5 KB/event**; ×2 events/frame (`Layout`+`Repaint`) ×60 fps | **~600 KB/s** | ESTIMATED |
| `SnapshotFrame` deserialization | `new PlayerSnapshot[4]` = 24 B header + 4 × 48 B = 216 B, ×30 Hz | ~6.3 KB/s | ESTIMATED |
| `EndScreenController.Update` | `FixedString.ToString()` + `$"{name} wins"` ≈ 200 B/frame × 60 fps, end screen only | ~12 KB/s | ESTIMATED |
| `LoadingScreenController.ShowProgress` | `$"{n} of {m} players ready"` ≈ 100 B/frame × 60 fps, loading phase only | ~6 KB/s | ESTIMATED |
| **Total during normal play (overlay on)** | | **~610 KB/s** | ESTIMATED |
| **Total during normal play (overlay stripped)** | | **~6 KB/s** | ESTIMATED |

The 100× difference between those last two rows is the entire GC story. With the overlay stripped,
this client allocates almost nothing during a match — which is a genuinely good result and is the
direct consequence of the ring buffers and scratch arrays throughout `Snackdown.Netcode`.

### Per-frame CPU load, 4 players + 6 fruit

| Item | Count | Source | Tag |
|---|---:|---|---|
| `Update` callbacks | 4 × (`PredictedPlayer` + `InputReader` + `PlayerLife`) + 6 × `Fruit` + `FruitSpawner` + `MatchDirector` + `RoundReferee` + `PlayerSpawnPoints` + `LoadingScreenController` + `EndScreenController` + `NetDebugOverlay` = **24** | Prefab composition (`Player.prefab`) + the 14 `Update`/`LateUpdate`/`OnGUI` methods found | ESTIMATED |
| `LateUpdate` callbacks | 4 × `VisualSmoother` + `SpectatorCamera` = **5** | `VisualSmoother.cs:51`, `SpectatorCamera.cs:55` | ESTIMATED |
| `Physics2D.OverlapCircle` per frame (host) | 6 (one per live fruit) | `Fruit.cs:73`, runs in `Update` not on the tick | ESTIMATED |
| `Physics2D.BoxCast` per **tick** (host) | 4 players × 2 = 8 | `PlayerMotor.cs:137,162` | ESTIMATED |
| Total physics queries/second (host, 60 fps / 30 Hz) | 6×60 + 8×30 = **600/s** | | ESTIMATED |
| Physics cost as % of one core (phone, 10 µs/query) | 600 × 10 µs = 6 ms/s = **0.6%** | | ESTIMATED |
| Draw calls | ≤ ~20 (4 characters + ≤6 fruit + tilemap batches + 1 UI Toolkit panel + IMGUI overlay) | 12 distinct sprite textures, no atlas (F-A6-12) | ESTIMATED |

### What will not hold 60 fps on the weakest named target

| Candidate | Verdict | Reasoning |
|---|---|---|
| Simulation + reconciliation | **Holds comfortably** | 0.1 ms per correction at 150 ms RTT; 0.6% of a core for all physics |
| `Update` count | **Holds comfortably** | 29 callbacks/frame; the threshold where this matters is in the thousands |
| Rendering | **Holds comfortably** | ≤20 draw calls, URP 2D, no post-processing, 0.70 MB of sprite VRAM |
| Fruit spawn hitches | **Holds** | 0.25 spawns/s; ~0.2–0.5 ms each on a phone; 15 hitches per minute of gameplay |
| UI Toolkit menus | **Holds** | 349 lines of UXML+USS total; `TextElement.text` no-ops on an unchanged value, so the per-frame assignments in `LoadingScreenController`/`EndScreenController` do not repaint |
| **`NetDebugOverlay` IMGUI pass** | **At risk on phone and WebGL** | ~1–3 ms/repaint of text generation for ~700 glyph quads with no batching, **plus** the ~600 KB/s GC rate. 6–18% of a 16.6 ms budget on a phone, and on WebGL each Boehm collection is a stop-the-world pause on a 32 MB heap with no incremental GC |
| **1024-tick worst-case replay** | **One dropped frame, bounded** | 20.5 ms on a phone; requires a ~34 s stall to trigger |

**Nothing in the gameplay code threatens 60 fps on a mid-range phone.** The only frame-budget risk
I can substantiate is the debug overlay — and F-A6-4's recommendation removes it from player builds
entirely, at which point this table has no entries in the "at risk" column. The honest headline is
that the client cannot hold 60 fps on two of its three named platforms because it cannot *run* on
them (F-A6-2, F-A6-3), not because it is too slow for them.

### WebGL first-load estimate (the figure that actually gates joining)

| Input | Value | Source |
|---|---|---|
| First-party managed code | ~6.2k LOC across 7 runtime assemblies | recon |
| Packages in the build | NGO 2.11, UGS Multiplayer 2.1.3, URP 17.3, Input System 1.19, Cinemachine 3.1.2, Multiplayer Tools 2.2.8 (unstripped, F-A6-4) | `Packages/manifest.json` |
| Referenced art | 0.70 MB RGBA32 / ~0.1 MB on disk | measured above |
| `webGLCompressionFormat` | `0` (Brotli) | `ProjectSettings.asset:826` |
| `webGLExceptionSupport` | `1` (full with stack traces — the slow, large setting) | `ProjectSettings.asset:819` |
| **Estimated compressed download** | **~8–14 MB**, dominated by engine + IL2CPP output, not by content | ESTIMATED |
| **Estimated first load on 4G (~10 Mbps)** | **~10–25 s** download + wasm compile | ESTIMATED |

This is the number that feeds F-A6-7: a WebGL peer taking 20 s to reach the arena holds three other
players on a loading screen with no timeout. Setting `webGLExceptionSupport` to `3` (explicitly
thrown only) would cut both size and runtime cost materially and is the single highest-value WebGL
player setting to change once a build exists.

## What is genuinely good here

This is the section I expected to write shortest and ended up writing longest. The performance
discipline in the netcode layer is real, deliberate, and documented.

- **The hot path allocates nothing, on purpose, with the reasoning written down.**
  `PredictionBuffer` (`PredictionBuffer.cs:31`) is a fixed ring indexed by `tick % Capacity` with a
  per-slot tick stamp so a wrapped entry cannot masquerade as fresh. `WorldSnapshotBuffer`
  (`WorldSnapshotBuffer.cs:38-39`) is a flat `PeerBody[Capacity * MaxBodies]` rather than an array of
  arrays. `SimulationContext` (`SimulationContext.cs:28-44`) is a `readonly struct` holding a
  *borrowed* array, with an XML remark (lines 25-27) stating explicitly that this keeps reconciliation
  allocation-free while it builds one per replayed tick. `PredictedPlayer._worldScratch`
  (`PredictedPlayer.cs:61`) and `NetworkSimulationLoop._snapshotScratch`
  (`NetworkSimulationLoop.cs:104-105`) are reused buffers. `Fruit._overlaps` and `Fruit._filter`
  (`Fruit.cs:29-32`) are `static readonly` so the overlap query is non-allocating. That is six
  independent decisions all pointing the same way — not an accident.

- **`PlayerMotor` never touches `Time`, `Transform` or `Rigidbody2D`.** `PlayerMotor.cs:12-14` states
  the rule and the code keeps it: `dt` is a parameter, the world arrives as a `SimulationContext`, and
  collisions use `Physics2D.BoxCast` queries rather than `Physics2D.Simulate`
  (`PlayerMotor.cs:15-18` explains why, correctly: stepping the physics world is not reproducible and
  would make replay lie). This is the property that makes the whole replay model cheap *and* correct,
  and it is enforced structurally by the `Snackdown.Simulation` assembly boundary.

- **The reconciliation replay is bounded.** `PredictedPlayer.cs:542-551` refuses to replay past
  `PredictionBuffer.Capacity` and hard-snaps instead, with a comment explaining that replaying past
  the ring would land on a state neither side ever computed. Most implementations of this pattern have
  no ceiling at all and discover the problem as a frame-time spike in production. F-A6-11 argues the
  constant is larger than it needs to be — but the ceiling *exists*, which is the part that matters.

- **`PlayerLife` explicitly rejects the naive approach on performance grounds.**
  `PlayerLife.cs:76-81`: a static registry maintained on spawn/despawn *instead of* `FindObjectsByType`,
  with the remark "at that rate is the kind of cost that does not show up until a profiler is opened."
  The author knew about the cost and designed around it. `PlayerLife.cs:16-21` does the same for
  bandwidth: the value drains every frame server-side but publishes at ~1 Hz with clients
  interpolating between, replacing 60 writes/s with 1.

- **`VisualSmoother` uses frame-rate-independent exponential decay.** `VisualSmoother.cs:60` —
  `_offset *= Mathf.Exp(-_decayRate * Time.deltaTime)` with an epsilon cutoff at line 61, and an XML
  remark (lines 18-19) explaining that a fixed-speed slide would make the result depend on frame rate.
  That is the correct formulation and it is uncommon to see it written this way.

- **`FrameRatePolicy` clears vSync before setting the cap.** `FrameRatePolicy.cs:31-32`, with the
  remark at lines 23-25 noting that vSync takes precedence and a cap that looks configured would
  otherwise do nothing. That is a real trap and most people hit it. F-A6-13's complaint is about
  platform scope, not about the desktop logic, which is right.

- **`SnapshotFrame` is one message per tick, not one per player.** `SnapshotFrame.cs:53-56` gives the
  reason: four states sharing one timestamp is what lets the interpolator line remote characters up
  against a common clock. That is a bandwidth *and* a correctness argument, and both hold.

- **`InputCommand` is 6 bytes and quantized at the source.** `InputCommand.cs:9-12` explains that an
  analog axis would round differently on the two machines — the right reason, stated in the right place.

**Over-engineering counter-check.** I looked for premature performance work in this domain (rubric #7)
and found essentially none. The buffers are sized generously — `PredictionBuffer.Capacity = 1024` for
replays that span <1 s, `WorldSnapshotBuffer.MaxBodies = 8` for a 4-player ceiling — but the total
cost is ~49 KB and ~26 KB per player respectively, which does not rise to the level of a finding. The
only one I raised (F-A6-11) is about a *behavioural* consequence, not about memory. Conversely, the
one place where an engine feature is *not* replaced with custom infrastructure is telling: fruit uses
plain `Instantiate`/`Spawn` rather than a hand-rolled pool (F-A6-9), and at 0.25 spawns/s that is the
correct call — building a pool there would be exactly the premature optimization the rubric warns
about. The under-engineering in this domain is entirely on the platform side (F-A6-1 through F-A6-3,
F-A6-6), not in the simulation.

## Open questions for the team

1. **Are Mobile and WebGL real targets, or aspiration?** Everything in F-A6-1 through F-A6-3 and
   F-A6-6 turns on this answer. A PC-only portfolio project with `README.md` saying so is completely
   defensible and costs nothing; a three-platform claim with no build for two of them is the weakest
   thing in the repository. This is a scope decision only Luca can make.

2. **Is the shipping build the demo build?** `README.md:53-60` documents F1–F4 as instructions to
   whoever is watching, which implies the overlay is *meant* to be there. If there is only ever one
   build and it is the demo, F-A6-4 becomes a non-finding and F-A6-5's severity drops. If a
   player-facing build is planned, both stand. The project currently has no way to express the
   difference.

3. **Has anyone opened the Profiler on this?** F-A6-14 says the repo contains no capture. The
   `FrameRatePolicy` reasoning implies someone diagnosed a CPU contention problem — was that from a
   profiler session, or inferred from the correction counts in `docs/05-validation.md:40`? If a capture
   exists locally, committing it would convert most of this file's estimates into measurements.

4. **Was `PredictionBuffer.Capacity = 1024` chosen for history or for the replay ceiling?** The
   comment says history; the code uses it as both (`PredictedPlayer.cs:542`). `WorldSnapshotBuffer`
   independently concluded 128 was right for replay span. Knowing the intent decides whether F-A6-11
   is a constant change or a second constant.

5. **What should happen when one client is slow to load?** F-A6-7 has no answer today except "wait
   forever or disconnect". The fair-start reasoning in `MatchDirector.cs:18-23` is sound and should be
   preserved — but it needs a third outcome, and which one (start without them, drop them to
   spectator, disconnect) is a design call, not a technical one.
