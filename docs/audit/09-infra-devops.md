# Build, Infra & DevOps Audit

**Agent:** A9 · **Branch:** `dev` · **Commit:** `10a2a13` · **Date:** 2026-08-08

Scope reframed per the brief: `hosting: none`, `target_ccu: 0`. There is no fleet, no orchestration
and no production environment to audit, and a dedicated server is a settled non-negotiable. The
question answered here is: **can one person build, verify and demo this reliably, and what would a
reviewer notice is missing?**

## Verdict

The *source-control* half of the infrastructure is genuinely good and better than most solo
portfolio repos — a hand-written `.gitattributes` routing 18 Unity YAML types with the reasoning
inline, a PR template that asks the right question, `packages-lock.json` committed, 19 merge-commit
PRs and a correctly gated test assembly. The *build* half does not exist. There is **no CI of any
kind** (confirmed: `.github/` holds only a PR template, no workflows, no hooks, no build script
anywhere in the tree), **no player build has ever been produced** (confirmed four independent ways),
`main` still sits at Phase 0 with **zero tags** while the roadmap declares Phases 1–3 complete, and a
build made today would ship the network simulator, the F1–F4 debug overlay and the ability for any
player to switch off client prediction. The single most demo-threatening item is not infrastructure
at all: every carefully-written failure diagnostic in the connection layer — including the version
mismatch message and "is the project linked and Relay enabled?" — is routed into a field the UI
deliberately never renders, and a built game has no runtime way to fall back from Relay to LAN.

## Scorecard

| Dimension | Score /5 | Note |
|---|---|---|
| CI / automated verification | 0 | Nothing. 68 EditMode tests, zero automated runs. |
| Build path & reproducibility | 2 | Scenes/order correct, lockfile committed; never built, one floating git dep. |
| Release-build hygiene | 1 | Simulator + debug overlay + prediction toggle ship in the player build. |
| Release process (as documented vs as executed) | 1 | `docs/04` describes tags and attached builds; `main` is at Phase 0, no tags. |
| Session orchestration (Relay/Lobby) | 3 | Correct and idiomatic; single UGS project, no runtime LAN fallback in a build. |
| Versioning / protocol compatibility | 3 | Wired to `Application.version` — real. Marketing version used as protocol version. |
| Observability | 2 | 11 disciplined log calls; crash reporting off, disconnects invisible to the player. |
| Source-control infrastructure | 5 | `.gitattributes`, `.gitignore`, PR template, lockfile, merge driver — all correct. |

---

## Findings

### F-A9-1 — No CI exists; the 68 EditMode tests are never run automatically

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `.github/` contains exactly one file, `PULL_REQUEST_TEMPLATE.md` (759 bytes). No
  `.github/workflows/`, no `.gitlab-ci.yml`, no `Jenkinsfile`, no `Makefile`, no `*.ps1`/`*.sh`
  build script outside `Library/`. `.git/hooks/` contains only Git's stock `*.sample` files — no
  installed hook. `find Assets -type d -name Editor` returns nothing, so there is no editor build
  script either. Test surface that goes unverified: `Assets/Tests/EditMode/` — 8 files, 68
  `[Test]`/`[TestCase]` attributes (note: `docs/03-roadmap.md:89` claims "36 unit tests"; the
  attribute count is 68 — A8 owns that discrepancy).
- **What it is**: Every check in `CLAUDE.md`'s pre-commit list ("compiles, console clean, no
  leftovers") is executed by hand, on one machine, by the one person who wrote the change. The PR
  template's three checkboxes are self-attested with no machine backing them.
- **Why it matters**: For a portfolio project the cost is not broken builds — it is the reviewer's
  read. A repo with a hand-written `.gitattributes` and 19 PRs, and *no* workflow file, reads as a
  developer who knows process but has not done the last step. Concretely: nothing detects that a
  refactor broke `PlayerMotorTests` until someone opens the editor, and nothing proves the project
  compiles on a machine other than this one — where `Library/` has been warm since 25 July.
- **Recommendation**: One GitHub Actions workflow, ~35 lines, using `game-ci/unity-test-runner@v4`
  pinned to `6000.3.14f1`, triggered on PRs into `dev`. It should validate exactly two things and no
  more: **(a)** the project compiles from a cold clone, **(b)** the EditMode suite passes. Do not add
  a build job, a PlayMode job or a matrix — none of them earn their keep here, and a red CI on a
  solo repo that nobody can fix is worse than none. Requires a `UNITY_LICENSE` secret (Personal
  licence activation via `game-ci/unity-activate`). Cost table below.
- **Effort**: S

### F-A9-2 — No player build has ever been produced

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: Confirmed four independent ways — (1) `docs/03-roadmap.md:94` lists "a runnable
  build" as an **unchecked** Phase 5 item; (2) no `Build/`, `Builds/` or `build/` directory exists,
  and `.gitignore:9-10` reserves them; (3) `Library/` contains **no** `PlayerScriptAssemblies`, no
  `PlayerDataCache`, no `il2cpp*` directory, and `Library/Bee/` holds exactly one dag
  (`1900b0aE.dag`, the editor script-compilation dag) — a player build creates additional dags and
  a player assembly folder; (4) `Library/BuildProfiles/SharedProfile.asset` has `m_BuildTarget: -2`
  (NoTarget) and the five `PlatformProfile.*.asset` files were all written 2026-08-08 21:54, i.e.
  auto-created by the editor, not configured.
- **What it is**: Every claim in `README.md` and `docs/` has only ever been observed inside the
  Unity editor, on one machine, largely through Multiplayer Play Mode.
- **Why it matters**: Three specific things are unverified as a direct consequence, and all three
  are things that break *only* in a player build: (a) `NetworkSimulationLoop` / `PlayerMotor`
  behaviour under managed stripping (`ProjectSettings/ProjectSettings.asset:854`, `stripEngineCode: 1`
  at line 185) — reflection-driven NGO serialization is the classic stripping casualty; (b) whether
  `AppBootstrap._firstScene = "Lobby"` resolves by name in a build, which depends on the scene being
  in `EditorBuildSettings` (it is — verified) but is never exercised; (c) whether two *separate
  processes on separate machines* connect, as opposed to two virtual players in one editor. It also
  means the project cannot be handed to anyone — an interviewer, a friend for a fourth player —
  without them installing Unity 6000.3.14f1.
- **Recommendation**: Produce one Windows x64 non-development build by hand, run a real two-machine
  Relay session against it, and record what broke. This is a one-afternoon task that converts three
  assumptions into three facts. Do not automate it in CI yet — a build job triples CI time and the
  artefact has no consumer.
- **Effort**: S

### F-A9-3 — The documented release process has never executed; `main` is still at Phase 0

- **Severity**: Major
- **Type**: Process
- **Confidence**: High
- **Evidence**: `git tag -l` returns **empty**. `git log --oneline main` ends at `8074fe6
  "Environment: disable the legacy Input Manager"` — 5 commits, all Phase 0 scaffolding, with the
  newest being repo setup. `main..dev` is 55 commits. `docs/04-workflow.md:64-70` states "`dev` →
  `main` when a phase lands and the result is demonstrable", "each completed phase bumps MINOR",
  "`1.0.0` is Phase 3 complete and playable over Relay", "Cut a GitHub Release from the tag, with
  … (from Phase 3 on) a playable build attached", and "Keep `bundleVersion` in `ProjectSettings` in
  sync with the tag". `ProjectSettings/ProjectSettings.asset:147` reads `bundleVersion: 0.1.0`.
  `docs/03-roadmap.md:10-12` marks Phases 1, 2 **and** 3 ✅.
- **What it is**: Half of the branching model is real and working — 19 PRs, all merged with merge
  commits, feature branches correctly named per `docs/04:27-34`, branches deleted or retained
  consistently. The *release* half of the same document has produced zero tags, zero releases and
  zero merges to `main` across three completed phases.
- **Why it matters**: This is a documentation-versus-reality contradiction in the file that *is* the
  deliverable, and it is the kind a reviewer checks in ten seconds (`git tag`, then look at `main`).
  It also makes `bundleVersion: 0.1.0` meaningless as a compatibility token (see F-A9-6): by the
  document's own rule it should read `0.3.0`.
- **Recommendation**: Either execute it once — merge `dev` → `main`, `git tag -a v0.3.0`, bump
  `bundleVersion` to match, attach the build from F-A9-2 — or amend `docs/04-workflow.md` to say
  releases start at Phase 4. The first is a morning's work and closes the contradiction properly;
  the second is honest but forfeits the story. Do not leave both standing.
- **Effort**: S

### F-A9-4 — Development-only tooling ships in the player build

- **Severity**: Major
- **Type**: Maintainability / Correctness
- **Confidence**: High
- **Evidence**: Four facts, each confirmed:
  1. `Assets/_Project/Scenes/Bootstrap.unity:495` instantiates
     `Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulator` on a scene object.
     Bootstrap is **build index 0** (`ProjectSettings/EditorBuildSettings.asset:9`), so it loads in
     every session. Its serialized preset (lines 509-516) is all zeros — no impairment — but the
     component and its transport pipeline hook are present.
  2. `Assets/_Project/Scripts/UI/NetDebugOverlay.cs` (216 LOC) is a plain `MonoBehaviour` in the
     **runtime** `Snackdown.UI` assembly, placed in `Assets/_Project/Scenes/Arena01.unity`.
  3. `Assets/_Project/Scripts/UI/Snackdown.UI.asmdef` references
     `Unity.Multiplayer.Tools.NetworkSimulator.Runtime`. That package assembly has
     `"includePlatforms": []` and no `DEVELOPMENT_BUILD` define constraint
     (`Library/PackageCache/com.unity.multiplayer.tools@acc3b25185bb/NetworkSimulator/Runtime/…asmdef`),
     so it compiles into every platform and every build configuration.
  4. `grep -rn "DEVELOPMENT_BUILD|UNITY_EDITOR|#if" Assets/_Project/Scripts --include=*.cs` returns
     **zero matches**. There is no conditional compilation anywhere in first-party runtime code.
- **What it is**: In a release build a player would get a permanent GUI panel in the top-right of the
  arena, and four global hotkeys: **F1 turns client-side prediction off for the person pressing it**
  (`NetDebugOverlay.cs:44` → `PredictedPlayer.PredictionEnabled`), F2 turns visual smoothing off,
  F3 hides the panel, F4 writes a CSV to `Application.persistentDataPath/metrics`
  (`NetDebugOverlay.cs:77`).
- **Why it matters**: F1 is the sharp edge — a global static that disables prediction is exactly the
  switch that should never exist outside a development build, and an accidental F1 during a demo
  makes the project look like its headline feature is broken. Beyond that, `NetDebugOverlay` uses
  `OnGUI`, which allocates and runs twice per frame (Layout + Repaint) and is a legitimately odd
  thing to find in shipped code. This is a *release hygiene* finding, not a correctness one: the
  overlay itself is excellent and is the whole demo (see "What is genuinely good"). It just should
  not be reachable by someone who did not ask for it.
- **Recommendation**: Smallest change that resolves it, in order of cost:
  1. Wrap `NetDebugOverlay`'s `Update`/`OnGUI` bodies in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, and
     the same around the `NetworkSimulator` lookup in `DescribeConditions()`. One file, ~4 lines.
  2. Better, and only slightly more: move `NetDebugOverlay.cs` into its own
     `Snackdown.UI.Debug.asmdef` with `"defineConstraints": ["UNITY_EDITOR || DEVELOPMENT_BUILD"]`,
     and move the `Unity.Multiplayer.Tools.NetworkSimulator.Runtime` reference off `Snackdown.UI`
     onto it. That removes the tools dependency from the runtime assembly graph entirely, which is
     the thing a reviewer reading the asmdefs will actually notice.
  3. The `NetworkSimulator` component in `Bootstrap.unity` is a scene object, so it needs a scene
     edit (via the Unity MCP per `CLAUDE.md`) or a `[RuntimeInitializeOnLoadMethod]` that destroys
     it outside development builds. Lowest priority of the three — with an all-zero preset it is
     inert, it is just dead weight and a puzzling thing to find in a shipped scene.
- **Effort**: S

### F-A9-5 — `com.coplaydev.unity-mcp` is pinned to a floating branch and is editor automation in a portfolio manifest

- **Severity**: Major
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `Packages/manifest.json:3` —
  `"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"`.
  `#main` is a branch, not a tag or commit. It is the only non-registry dependency in the manifest.
  `.mcp.json` at the repo root wires it to `http://127.0.0.1:8080/mcp`.
- **What it is** (with the nuance that matters): `Packages/packages-lock.json` **does** record a
  resolved commit — `"hash": "c14de1e6dc01ab42d2bb358730cff954bce0ce6b"` — and that lockfile is
  tracked (`git ls-files` confirms). So a fresh clone with the lockfile intact resolves the *same*
  code, and `docs/04-workflow.md:103-104` is correct when it says the lockfile "pins the exact
  package versions". Reproducibility is therefore **not** currently broken. The exposure is
  narrower and more specific:
  - Any operation that re-resolves the entry — deleting the lockfile, a Package Manager "Update",
    a manifest edit that invalidates it — silently pulls whatever is at the tip of `main` that day,
    with no version number to notice the change by and nothing in the diff but a hash.
  - Package resolution requires reaching `github.com`. A clone on a machine without network access,
    behind a proxy, or after the upstream repo is renamed/made private, **fails to open the project
    at all** — Unity stops on an unresolvable dependency. Every registry package in the manifest is
    served by Unity's own CDN and has no such dependency.
  - It is editor-automation tooling with broad project access sitting in the dependency list of a
    repo whose stated purpose is to be read by an interviewer. It is not part of the game. (A7 owns
    the supply-chain/security angle; this entry is about reproducibility and what the manifest
    communicates.)
- **Why it matters**: The failure mode is precisely the one this project cannot afford — "I cloned it
  and it wouldn't open." That happens to the reviewer, not to the author, and it happens silently.
- **Recommendation**: Pin the fragment to the resolved commit —
  `…/MCPForUnity#c14de1e6dc01ab42d2bb358730cff954bce0ce6b` — or to an upstream tag if one exists.
  That is a one-line manifest edit that removes the floating-tip risk while keeping the tool. If the
  goal is a manifest a reviewer reads as pure game dependencies, remove the entry entirely and add
  the package locally per-machine; it contributes nothing to the built game. Either way the GitHub
  availability dependency remains — accept it knowingly or drop the package.
- **Effort**: S

### F-A9-6 — Every connection diagnostic is written for a human and then discarded

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High (LAN path) / Medium (Relay path)
- **Evidence**: The full chain, end to end:
  - `ConnectionApproval.cs:172-177` composes, deliberately and with a comment explaining why:
    `$"Different game version — yours is {…}, this game is {_gameVersion}."` → `Refuse(response, reason)`.
  - `ConnectionApproval.cs:202` puts it in `response.Reason`, commented "Travels to the rejected
    client and is shown to them, so it is written for a player rather than for a log."
  - `DirectConnectionProvider.cs:132-135` receives it as `_networkManager.DisconnectReason` and
    passes it as the **second** argument of `ConnectionResult.Failed(ConnectionFailure.Rejected, …)`.
  - `ConnectionResult.cs:66-67` — that second parameter is `diagnostic`.
  - `ConnectionResult.cs:52` — `Diagnostic`'s own XML doc: *"Detail for the log. **Never rendered to
    a player as-is.**"*
  - `MainMenuController.cs:219` renders `result.PlayerFacingMessage`, which for `Rejected` is the
    fixed string `"That game turned us away."` (`ConnectionResult.cs:75`). The real reason goes to
    `Debug.LogWarning` at `MainMenuController.cs:222`.
- **What it is**: The message written specifically so the player knows *what to do* reaches a log
  file the player will never open, and the screen shows a sentence that says nothing. The same
  chain swallows `RelayConnectionProvider.cs:198-199`'s *"Unity Services unavailable — is the project
  linked and Relay enabled?"* — the single most useful string in the connection layer, produced by
  the exact failure most likely to occur during a live demo, rendered as *"Something went wrong
  connecting."*
- **Why it matters**: Two comments in the codebase state an intent the code contradicts, which is
  the sharpest kind of finding in a repo whose comments are the deliverable. Practically: when the
  demo fails in front of someone, the screen gives the author nothing to work with either.
- **Recommendation**: Give `ConnectionResult` a third, explicitly player-safe channel — the approval
  layer already sanitises what it writes, so `Refuse`'s reason qualifies. Concretely: add
  `public readonly string PlayerDetail`, have `PlayerFacingMessage` append it when present
  (`"That game turned us away — Different game version: yours is 0.1.0, this game is 0.3.0."`), and
  keep `Diagnostic` for the SDK exception text that genuinely should not be shown. Roughly 15 lines
  across `ConnectionResult.cs` and the two providers. **Note the trust boundary**: the reason string
  arrives from a remote server, so it must be length-capped and control-character-stripped exactly
  as `ConnectionApproval.SanitizeNickname` already does for nicknames — do not render it raw.
- **Effort**: S

### F-A9-7 — Relay is a single point of failure with no runtime fallback in a build

- **Severity**: Major
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `MainMenuController.cs:32-33` — `[Tooltip("Start on Relay (join by code) instead of
  direct LAN (join by address).")] [SerializeField] bool _useRelay = true;`. It is read once, at
  `MainMenuController.cs:139-141`, to choose the provider, and the provider is cached in `_provider`
  for the component's lifetime. There is no UI control bound to it — `OnEnable` (lines 71-104) wires
  seven buttons, none of which touch it. `ApplyProviderLabels()` (lines 157-174) only *reads*
  `_provider.JoinsByCode` to relabel the field.
- **What it is**: LAN versus Relay is an Inspector checkbox baked into `Lobby.unity` at build time.
  In a player build there is no way to reach it.
- **Why it matters**: This is the realistic demo failure. If UGS is unreachable, the project is
  unlinked, anonymous sign-in fails, or the free-tier bandwidth is exhausted mid-session,
  `PrepareAsync` returns a failure (`RelayConnectionProvider.cs:194-200`), the menu prints
  "Something went wrong connecting." (F-A9-6), and **the excellent, always-works LAN path that
  `DirectConnectionProvider`'s own XML doc calls "the provider that always works — no account, no
  internet, no quota" is unreachable from the running game.** Two laptops on the same conference
  wifi could have played; the build will not let them. The `IConnectionProvider` abstraction exists
  precisely to make this switch cheap, and nothing spends it.
- **Recommendation**: A toggle in the menu UXML bound to `_useRelay`, resetting `_provider` and
  `_approval` to null and re-running `ApplyProviderLabels()` on change. That is ~20 lines and it is
  the single highest-value change in this report — it turns the project's own headline abstraction
  ("one identical join flow over LAN and over the internet") into something a viewer can *see*
  demonstrated by clicking, rather than take on faith. Guard it against being flipped mid-session
  (`NetworkManager.Singleton.IsListening`).
- **Effort**: S

### F-A9-8 — `Application.version` is used as the wire protocol version

- **Severity**: Minor
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `MainMenuController.cs:65` — `static string GameVersion => Application.version;`,
  fed to both the approval (`:135`) and both providers (`:140-141`).
  `Application.version` is `bundleVersion` = `0.1.0` (`ProjectSettings/ProjectSettings.asset:147`).
  The check at `ConnectionApproval.cs:172` is **string equality** on that value.
  `docs/04-workflow.md:66-70` mandates that `bundleVersion` track the release tag, and that patches
  bump PATCH.
- **What it is**: The good news first, since the brief asked whether this is wired to anything real:
  **it is.** It is not a hardcoded constant and not a stubbed literal — it reads the actual project
  version, travels in `ConnectionPayload.GameVersion` (a `FixedString32Bytes`, so it is length-safe
  on the wire), and is validated on the server before a player object is allocated. That is more
  than most projects at this stage. The issue is which number it reads. Coupling the wire
  compatibility check to the *marketing* version means a release that changes nothing but a UI
  colour — `0.3.0` → `0.3.1`, exactly what `docs/04:67` instructs — hard-refuses every player still
  on `0.3.0`, with the message "Different game version".
- **Why it matters**: In practice, with no builds distributed (F-A9-2), a v1-client-meets-v2-host
  scenario has never occurred and cannot until a second build exists. What a v1 client *would*
  experience today: on LAN, refusal at approval and the screen showing "That game turned us away."
  with the real reason lost (F-A9-6); on Relay, the Sessions join throws and `Classify()`
  (`RelayConnectionProvider.cs:230-244`) has no case for an approval refusal, so it falls to
  `_ => ConnectionFailure.Error` — "Something went wrong connecting." The player is told nothing
  about versions at all on the path that matters most.
- **Recommendation**: Introduce `public const string ProtocolVersion` on `ConnectionApproval`, bumped
  only when the wire format changes (`InputPacket`, `SnapshotFrame`, `ConnectionPayload`), and send
  that instead. Keep `Application.version` for display. Two lines plus one constant, and it makes
  the check defensible in an interview rather than merely present. Optionally add `SessionError`
  cases to `Classify()` so a Relay-side refusal maps to `Rejected` rather than `Error`.
- **Effort**: S

### F-A9-9 — Nothing tells a player the session ended

- **Severity**: Minor
- **Type**: Correctness
- **Confidence**: High
- **Evidence**: `grep -rn "OnTransportFailure|OnClientStopped|OnServerStopped" Assets/_Project/Scripts`
  returns **zero matches**. `OnClientDisconnectCallback` is subscribed in four places, all
  server-side bookkeeping or attempt-scoped: `ConnectionApproval.cs:120` (forget the nickname),
  `SessionRoster.cs:58` (remove the roster row), `MatchDirector.cs:106` (`OnClientLeft`), and
  `DirectConnectionProvider.cs:138`, which unsubscribes at `:167` when the join attempt resolves.
  Neither `LoadingScreenController.cs` nor `EndScreenController.cs` contains any of
  `Disconnect`/`Shutdown`/`IsListening`. `RelayConnectionProvider` never subscribes to a disconnect
  callback at all.
- **What it is**: When the host quits, crashes, or the Relay allocation drops, a connected client's
  `NetworkManager` shuts down and no code reacts. The player is left in the arena with a frozen
  world and no message, no return-to-menu, no reconnect.
- **Why it matters**: A 4-player demo where one machine is the host means this happens the moment
  the host alt-F4s. It is the most visible unfinished edge in the runtime, and it is cheap.
- **Recommendation**: Subscribe to `OnClientStopped` (NGO 2.x) in `LoadingScreenController` — which
  already owns menu/arena transitions — and on fire, return to the menu with a status line. ~15
  lines. Also worth hooking `OnTransportFailure` for the Relay-drop case specifically.
- **Effort**: S

### F-A9-10 — `Snackdown.slnx` is a generated file, tracked, and permanently dirty

- **Severity**: Minor
- **Type**: Process
- **Confidence**: High
- **Evidence**: `git status --porcelain` → ` M Snackdown.slnx` (this is the dirty file recon noted).
  `.gitignore:37-39` ignores `*.csproj`, `*.unityproj` and `*.sln` — the eight `Snackdown.*.csproj`
  files at the repo root are correctly untracked (`git check-ignore -v Assembly-CSharp.csproj` →
  `.gitignore:37`). `*.slnx` is not covered; `Snackdown.slnx` appears in `git ls-files`. The diff is
  pure reordering — Unity regenerates the `<Project>` list in nondeterministic order:
  `4 insertions(+), 4 deletions(-)` with identical content.
- **What it is**: The upstream `github/gitignore` Unity template predates .NET's `.slnx` format, so
  the rule at line 39 misses it. The file regenerates on every asmdef change and rewrites its own
  line order, producing a dirty working tree that never goes away.
- **Why it matters**: Small, but it is friction on every single commit, and a permanently dirty tree
  trains you to ignore `git status` — which is how a real change gets committed by accident. It also
  contradicts `CLAUDE.md`'s "no leftovers" pre-commit rule and `docs/04-workflow.md:102-104`'s claim
  that only what is needed to open the project cleanly is tracked (an IDE solution file is not).
- **Recommendation**: Add `*.slnx` next to `*.sln` in `.gitignore` and `git rm --cached
  Snackdown.slnx`. `.vscode/settings.json` sets `"dotnet.defaultSolution": "Snackdown.slnx"`, which
  keeps working — Unity regenerates the file locally.
- **Effort**: S

### F-A9-11 — No environment separation, and the UGS environment is unset

- **Severity**: Minor
- **Type**: Process
- **Confidence**: Medium
- **Evidence**: `ProjectSettings/ProjectSettings.asset:960-964` binds the project:
  `cloudProjectId: 82acd7e3-cf6d-4787-bac4-e0e9af66004e`, `projectName: Snackdown`,
  `organizationId: lucavalentini25` — tracked in git, so a clone is bound with no manual step. That
  part is right. But `ProjectSettings/Packages/com.unity.services.core/Settings.json` reads
  `{"EnvironmentName": "", "EnvironmentId": "00000000-0000-0000-0000-000000000000"}` — never
  configured. `RelayConnectionProvider.cs:176-177` calls `UnityServices.InitializeAsync()` with **no
  `InitializationOptions`**, so no environment is selected in code either; the SDK falls back to its
  default (`production`). `SessionOptions` at `:74-77` sets only `MaxPlayers` and `.WithRelayNetwork()`
  — **no region argument**, so Relay picks a region automatically.
- **What it is**: One UGS project, one implicit environment, no dev/prod split, no region pinning.
- **Why it matters**: At `target_ccu: 0` with one developer, this is correct and I am not
  recommending otherwise — a staging environment for a portfolio demo would be over-engineering by
  rubric #4 (a configuration switch with one live value). It is listed because a reviewer may ask,
  and because two consequences are worth being able to answer: (a) automatic region selection is the
  right default and means a demo across continents still works, but it also means the observed RTT
  in a recorded run depends on where Unity placed the allocation — relevant when defending
  `docs/05-validation.md`'s numbers; (b) the environment name being blank rather than explicitly
  `"production"` is the kind of thing that becomes confusing later, not now.
- **Recommendation**: Nothing structural. One sentence in `docs/02-netcode.md` stating that Relay
  region selection is automatic and the environment is the default `production`, so the choice reads
  as deliberate rather than unexamined.
- **Effort**: S

### F-A9-12 — WebGL and Mobile are stated targets with no supporting configuration

- **Severity**: Minor
- **Type**: Scope-drift
- **Confidence**: High
- **Evidence**: `Assets/_Project/Scenes/Bootstrap.unity:408` — `m_UseWebSockets: 0` on the
  `UnityTransport`. WebGL cannot use UDP; it requires WebSockets (and `wss` for Relay). Line 409 —
  `m_UseEncryption: 0` (DTLS off; A7's domain, noted only because WebGL forces the question).
  `ProjectSettings/ProjectSettings.asset:849-850` sets a scripting backend for `Android` only
  (IL2CPP); `:854-869` sets `managedStrippingLevel` to Minimal for every non-desktop platform, all
  Unity defaults, none configured. No touch input: `Assets/InputSystem_Actions.inputactions` is the
  stock asset and `Snackdown.Input` (2 files) has no touch path. No Addressables, no platform
  `defineConstraints` on any asmdef.
- **What it is**: The brief lists `["PC (Windows)", "Mobile", "WebGL"]` as target platforms. The
  repository supports PC. Notably, **no doc in the repo claims otherwise** — `README.md:30` says
  "Host (listen server), up to 4 players" and nothing about mobile or web — so this is a gap against
  an aspiration, not a false claim in the deliverable.
- **Why it matters**: WebGL is the one that would change the project's reach the most (a playable
  link in a portfolio beats a downloadable zip), and it is also the one with a hard blocker: the
  transport flag above, plus the fact that a WebGL client cannot *host*, only join — which collides
  with the listen-server topology for any browser-only session.
- **Recommendation**: Decide and write it down. If WebGL matters, the honest scope is
  "WebGL client, desktop host" and it starts with `m_UseWebSockets: 1` + `m_UseEncryption: 1` on the
  transport and a Relay `wss` connection type — a real chunk of work, not a checkbox. If it does not,
  say so in `README.md` so the platform list stops being an open question. Given the roadmap ends at
  Phase 5 with "a runnable build", scoping to PC is the defensible answer.
- **Effort**: L (if pursued) / S (to document the decision)

### F-A9-13 — Project identity is still the URP template's

- **Severity**: Nit
- **Type**: Maintainability
- **Confidence**: High
- **Evidence**: `ProjectSettings/ProjectSettings.asset:15` `companyName: DefaultCompany`; `:169-172`
  `applicationIdentifier: { Android: com.UnityTechnologies.com.unity.template.urpblank, Standalone:
  com.Unity-Technologies.com.unity.template.urp-blank, iPhone: … }` with
  `overrideDefaultApplicationIdentifier: 1`. `ProjectSettings/EditorSettings.asset:22` —
  `m_ProjectGenerationRootNamespace: Network.Platformer`, a leftover from the legacy project this
  one rebuilds. `:20` `m_ShowUnitySplashScreen: 1`.
- **What it is**: Cosmetic, but it has one functional edge: `Application.persistentDataPath` derives
  from `companyName`/`productName`, so `NetDebugOverlay`'s CSV exports land under
  `…/DefaultCompany/Snackdown/metrics` — which is where the validation runs in
  `docs/05-validation.md` came from.
- **Why it matters**: The first thing visible when someone runs the build. "DefaultCompany" and a
  `com.unity.template.urp-blank` bundle ID undercut a project whose case is care.
- **Recommendation**: Set `companyName` and a real reverse-DNS identifier before the build in
  F-A9-2. Two fields. Fix the stale root namespace in the same commit.
- **Effort**: S

---

## Over-engineering check (this domain)

Applying the rubric to build/infra specifically: **zero hits.** There is no build infrastructure to
over-engineer — no custom build pipeline, no bespoke CI harness, no config-driven deploy system, no
abstraction layer over Unity's build API. The failure mode here is entirely the opposite one:

- **Under-engineering, confirmed**: no CI (F-A9-1), no build (F-A9-2), no release execution
  (F-A9-3), no `DEVELOPMENT_BUILD` gating anywhere (F-A9-4 — literally zero `#if` directives in
  6.2k LOC of runtime code), no disconnect handling (F-A9-9).
- **Counter-check, complexity correctly earned**: `.gitattributes` is the clearest example in the
  repo. Eighteen Unity YAML types routed to `unityyamlmerge`, plus a comment block explaining that
  the driver is local Git config that does not travel with a clone, plus a deliberate,
  *justified* decision **not** to use Git LFS (`.gitattributes:46-48`: "this project's art is pixel
  art measured in kilobytes, and LFS would add bandwidth quotas and a smudge/clean step for no
  benefit. Revisit if audio or large textures ever land here"). That is a documented rejected
  alternative on a decision most solo projects get wrong in the other direction. The merge driver is
  also **actually configured** on this machine — `git config --get merge.unityyamlmerge.driver`
  returns the full `UnityYAMLMerge.exe` command, so `docs/04-workflow.md:72-96` is not aspirational.

---

## Quantified Estimates

### Cost of adding the minimal CI from F-A9-1

| Input | Value | Source |
|---|---|---|
| Workflow file | ~35 lines YAML | ESTIMATED — `game-ci/unity-test-runner@v4` standard EditMode job |
| Editor version to pin | `6000.3.14f1` | MEASURED — `ProjectSettings/ProjectVersion.txt` |
| Test files / assertions covered | 8 files / 68 `[Test]`+`[TestCase]` | MEASURED — `grep -c` over `Assets/Tests/EditMode/*.cs` |
| Setup work (licence secret + first green run) | 2–4 h | ESTIMATED |
| First run wall time (cold: image pull + full import) | 15–25 min | ESTIMATED — GameCI editor images are 4–6 GB; project asset payload ≈ 5 MB of art |
| Cached run wall time (`Library/` cached via `actions/cache`) | 5–10 min | ESTIMATED |
| GitHub Actions minutes cost | **$0** if the repo is public (unlimited); 2,000 min/mo free if private | ESTIMATED — GitHub Actions free tiers |
| Recurring maintenance | ~0 — no matrix, no build job, no artefacts | ESTIMATED |

**Assumption**: Unity Personal licence activation via `game-ci/unity-activate` and a `UNITY_LICENSE`
repo secret. If that proves unworkable, the fallback that still catches most regressions is a
`dotnet build Snackdown.slnx` compile-only job with no Unity licence — but note that requires the
generated `.csproj` files, which are correctly *not* tracked (F-A9-10), so it is not free.

### Relay free-tier limits — the constraint that actually applies

| Limit | Value | Tag |
|---|---|---|
| Free average monthly CCU | 50 | ESTIMATED — Unity UGS pricing page, figures published Aug 2023, unchanged in later revisions |
| Equivalent connectivity minutes | 2,160,000 / 30-day month | ESTIMATED — same source |
| Free bandwidth | 3 GiB per CCU, capped at 150 GiB/month combined | ESTIMATED — same source |
| Overage, US + EU | $0.09 / GiB | ESTIMATED — same source |
| Overage, Asia + Australia | $0.16 / GiB | ESTIMATED — same source |
| Overage per additional average CCU | $0.16 | ESTIMATED — same source |

**Interpretation for this project**, which is the only figure worth defending: a demo is 4
concurrent players. Against a 50-CCU free allowance, the session count is not the binding
constraint — 50 average monthly CCU is roughly 12 simultaneous 4-player matches running
continuously all month. The binding constraint would be **bandwidth**, and only in a scenario that
does not exist here. `docs/01-architecture.md:107` already claims 4 players "fits Relay's free tier";
that claim is **correct** and survives checking.

Per-session byte figures are **deferred to A4** — I have not measured the snapshot payload and will
not invent it. What A9 can state: the traffic model is one unreliable input RPC up and one
unreliable snapshot RPC down per tick at 30 Hz per client
(`PredictedPlayer.cs:380`, `NetworkSimulationLoop.cs:119`), all of it relayed, so **Relay egress is
2× the raw game traffic** — every packet traverses Unity twice (peer → Relay → peer). A4's
per-session number should be doubled before comparing it to the quota above.

### Evidence that no build exists — the four independent checks

| Check | Result | Tag |
|---|---|---|
| `Build/`, `Builds/`, `build/` directories | absent | MEASURED |
| `Library/PlayerScriptAssemblies`, `Library/PlayerDataCache`, `Library/il2cpp*` | absent | MEASURED |
| `Library/Bee/*.dag` count | 1 (editor compilation only) | MEASURED |
| `Library/BuildProfiles/SharedProfile.asset` `m_BuildTarget` | `-2` (NoTarget), `m_Development: 0` | MEASURED |
| `git tag -l` | empty | MEASURED |
| `docs/03-roadmap.md:94` "a runnable build" | unchecked `[ ]` | MEASURED |

---

## What is genuinely good here

Cited, and none of it is faint praise — the source-control layer of this repo is better than the
overwhelming majority of solo Unity projects.

1. **`.gitattributes` is the best file in the repository.** 18 Unity YAML types routed to
   `unityyamlmerge` (`:12-29`), with a header comment explaining the actual failure — "Git happily
   produces a syntactically valid file that Unity refuses to open" — and a deliberate, reasoned
   rejection of Git LFS (`:46-48`). Rejected alternatives with reasons are exactly what an
   interviewer probes for, and it is here in a file most people copy-paste without reading.

2. **The merge driver is real, not documented-and-forgotten.**
   `git config --get merge.unityyamlmerge.driver` returns the configured `UnityYAMLMerge.exe`
   command, matching `docs/04-workflow.md:80-82` exactly, including the `recursive binary` line and
   the explanation of *why* that third line matters (multiple merge bases). The doc includes its own
   verification command (`:89-93`). This is process infrastructure that was set up **and** written
   down **and** is currently working.

3. **Scenes-in-build are correct and in the right order.**
   `ProjectSettings/EditorBuildSettings.asset:8-16` — `Bootstrap` at index **0**, then `Lobby`, then
   `Arena01`, all three enabled. `Bootstrap` at 0 is load-bearing:
   `AppBootstrap.cs:22` loads `_firstScene = "Lobby"` on top of it, and `:60-66` installs
   `VerifySceneBeforeLoading` to keep the menu out of NGO's scene synchronization — with a `<remarks>`
   describing the exact bug that motivated it (two menus, two cameras, a failed handshake). I checked
   for the classic error here (Bootstrap not at index 0) and it is not present.

4. **The test assembly is correctly gated and genuinely cannot ship.**
   `Assets/Tests/EditMode/Snackdown.Tests.EditMode.asmdef` carries `"includePlatforms": ["Editor"]`,
   `"defineConstraints": ["UNITY_INCLUDE_TESTS"]` and `"autoReferenced": false` — all three, which
   is the complete correct answer and rarer than it should be. It is also the *only* asmdef in the
   project with any platform gating, which is precisely the finding in F-A9-4: the author clearly
   knows the mechanism and simply has not applied it to the debug overlay.

5. **Logging is disciplined.** Eleven `Debug.*` calls in 6.2k LOC of runtime code, every one
   prefixed `[Snackdown]`, nine of them passing `this` as context so the Inspector highlights the
   offending object, and exactly **one** plain `Debug.Log` — the F4 export confirmation. Zero
   commented-out logs, zero leftover `print(`, zero `Debug.Log("here")`. `CLAUDE.md`'s "no leftover
   Debug.Log" rule is being followed literally.

6. **`packages-lock.json` is committed**, which is what makes the floating git dependency in F-A9-5
   a manageable risk rather than an actual reproducibility break, and `docs/04-workflow.md:103-104`
   explicitly explains why it is tracked. Every other dependency is a pinned registry version — no
   ranges, no `latest`, no wildcards anywhere in `Packages/manifest.json`.

7. **The PR flow is real.** 19 merge commits into `dev`, sequentially numbered PRs #1–#19, branch
   names conforming to `docs/04-workflow.md:27-34` (`feature/*`, `bugfix/*`, `docs/*`), merge
   commits rather than squashes where the individual commits carry meaning — exactly the rule at
   `docs/04:58-60`. The PR template (`.github/PULL_REQUEST_TEMPLATE.md`) asks "How it was verified"
   with the instruction *"'Tested' says nothing; 'host + 1 client under 150 ms simulated latency,
   corrections visible in the overlay, none felt' says everything. List what you did NOT verify too."*
   That is a better verification prompt than most professional teams use.

8. **`FrameRatePolicy`** (`Core/FrameRatePolicy.cs`) is 35 lines that cap the render loop at 60 —
   twice the tick — via `[RuntimeInitializeOnLoadMethod]`, clearing `vSyncCount` first because it
   silently overrides the target. The `<remarks>` cites the measurement that motivated it (122
   starved ticks, 1.66 corrections/s under 2% loss, none of them the network's fault). Small,
   correct, and it works identically in a build and in the editor — which is more than can be said
   for most of the surrounding infrastructure.

---

## Open questions for the team

1. **Is a distributable build in scope at all?** `docs/03-roadmap.md:99` defines "portfolio-ready"
   as Phases 0–3, which are done — and a build is a Phase 5 item. But `docs/04-workflow.md:69` says
   releases carry "(from Phase 3 on) a playable build attached". These two documents disagree.
   Which one is the intent? Everything in F-A9-2, F-A9-3, F-A9-4 and F-A9-13 hinges on the answer.

2. **Is `com.coplaydev.unity-mcp` meant to be visible to a reviewer reading `manifest.json`?** It is
   the only non-registry dependency and the only one that is not part of the game. Pinning it
   (F-A9-5) and removing it are both one-line changes; which one is wanted is a judgement about how
   the repo should read, not a technical question.

3. **Should CI exist for its own sake, or to be seen?** For a one-person project the practical value
   of the workflow in F-A9-1 is modest — the author runs the tests in the editor anyway. Its value
   as a signal to whoever reads the repo is large. If the second is the goal, the workflow should be
   added *and* mentioned in `README.md`; a green badge nobody links to buys nothing.

4. **Are Mobile and WebGL live targets or aspirations?** No document in the repo claims them, so
   there is currently no contradiction — but the answer determines whether `m_UseWebSockets` and
   touch input are backlog items or non-goals (F-A9-12).

5. **Has anyone other than the author ever cloned and opened this project?** Every "it works" in
   this repo has been observed on one machine with a warm `Library/` since 25 July. A cold clone on
   a second machine is the cheapest possible test of F-A9-1, F-A9-2 and F-A9-5 simultaneously, and
   it has apparently never been run.

---

## Sources

- [Unity Gaming Services pricing](https://unity.com/products/gaming-services/pricing) — Relay free-tier CCU and bandwidth figures
- [Unity Relay limitations](https://docs.unity.com/ugs/manual/relay/manual/limitations) — consulted; the page did not return substantive content at audit time, so no limit is cited from it
