using System.Collections.Generic;
using System.Threading;
using Snackdown.Connection;
using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Drives the menu and lobby screens: collects what the player types, hands it to an
    /// <see cref="IConnectionProvider"/>, and shows whatever comes back.
    /// </summary>
    /// <remarks>
    /// <para>Holds no connection logic of its own. It cannot say whether an address is valid, why a
    /// join failed, or who is in the session — it asks. That separation is the point of
    /// <see cref="IConnectionProvider"/>: when the Relay provider lands, this file changes only
    /// where it says "Address" versus "Code".</para>
    /// <para>The screens are swapped by <c>display</c> rather than by loading documents, so element
    /// references stay valid for the lifetime of the component and a connection attempt can outlive
    /// the screen that started it.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("Players needed before a match can start. Set to 1 to test the arena alone.")]
        [Min(1)]
        [SerializeField] int _minPlayersToStart = 2;

        UIDocument _document;
        SessionRoster _roster;
        CancellationTokenSource _attempt;

        VisualElement _menuScreen;
        VisualElement _joinScreen;
        VisualElement _lobbyScreen;
        TextField _nickname;
        TextField _target;
        Button _host;
        Button _join;
        Button _cancel;
        VisualElement _browseSection;
        VisualElement _browseList;
        Button _refresh;
        Button _joinConfirm;
        Button _joinCancel;
        Button _back;
        Label _joinStatus;
        Button _ready;
        Button _start;
        Button _leave;
        Label _menuStatus;
        Label _lobbyStatus;
        Label _lobbySubtitle;
        Label _joinCode;
        Button _copyCode;
        VisualElement _rosterList;

        VisualElement _wardrobeRow;

        VisualElement _settingsPanel;
        VisualElement _presetRow;
        DropdownField _preset;
        FloatField _startingLife;
        FloatField _maxLife;
        FloatField _drain;
        FloatField _roundSeconds;

        MatchDirector _director;

        /// <summary>
        /// True while the fields are being filled from the replicated numbers.
        /// </summary>
        /// <remarks>
        /// Setting <c>value</c> on a UI Toolkit field raises its change event, so writing what the
        /// server just sent would send it straight back — and with two peers editing, bounce between
        /// them. This is the flag that tells a change apart from an echo of one.
        /// </remarks>
        bool _fillingFields;

        /// <summary>The bare code, without the sentence wrapped around it for display.</summary>
        string _rawJoinTarget = string.Empty;

        bool _watchingDisconnects;

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _menuScreen = root.Q<VisualElement>("menu-screen");
            _joinScreen = root.Q<VisualElement>("join-screen");
            _lobbyScreen = root.Q<VisualElement>("lobby-screen");
            _nickname = root.Q<TextField>("nickname-field");
            _target = root.Q<TextField>("target-field");
            _host = root.Q<Button>("host-button");
            _join = root.Q<Button>("join-button");
            _cancel = root.Q<Button>("cancel-button");
            _browseSection = root.Q<VisualElement>("browse-section");
            _browseList = root.Q<VisualElement>("browse-list");
            _refresh = root.Q<Button>("refresh-button");
            _joinConfirm = root.Q<Button>("join-confirm-button");
            _joinCancel = root.Q<Button>("join-cancel-button");
            _back = root.Q<Button>("back-button");
            _joinStatus = root.Q<Label>("join-status");
            _ready = root.Q<Button>("ready-button");
            _start = root.Q<Button>("start-button");
            _leave = root.Q<Button>("leave-button");
            _menuStatus = root.Q<Label>("menu-status");
            _lobbyStatus = root.Q<Label>("lobby-status");
            _lobbySubtitle = root.Q<Label>("lobby-subtitle");
            _joinCode = root.Q<Label>("join-code");
            _copyCode = root.Q<Button>("copy-code-button");
            _rosterList = root.Q<VisualElement>("roster-list");

            _wardrobeRow = root.Q<VisualElement>("wardrobe-row");

            _settingsPanel = root.Q<VisualElement>("settings-panel");
            _presetRow = root.Q<VisualElement>("preset-row");
            _preset = root.Q<DropdownField>("preset-field");
            _startingLife = root.Q<FloatField>("starting-life-field");
            _maxLife = root.Q<FloatField>("max-life-field");
            _drain = root.Q<FloatField>("drain-field");
            _roundSeconds = root.Q<FloatField>("round-seconds-field");

            _nickname.value = NicknamePreference.Offered;

            // Deferred a frame rather than read here: the provider needs NetworkManager.Singleton,
            // which is assigned in its own Awake with no ordering against this one. Without the
            // delay the field kept its LAN label until the first click built the provider — so the
            // menu asked for an address while the game was about to ask Relay for a code.
            _document.rootVisualElement.schedule.Execute(ApplyProviderLabels);

            _host.clicked += OnHostClicked;
            _join.clicked += OnJoinClicked;
            _cancel.clicked += OnCancelClicked;
            _refresh.clicked += OnRefreshClicked;
            _joinConfirm.clicked += OnJoinConfirmClicked;
            _joinCancel.clicked += OnCancelClicked;
            _back.clicked += OnBackClicked;
            _ready.clicked += OnReadyClicked;
            _start.clicked += OnStartClicked;
            _leave.clicked += OnLeaveClicked;
            _copyCode.clicked += OnCopyCodeClicked;

            _preset.RegisterValueChangedCallback(OnPresetPicked);
            _startingLife.RegisterValueChangedCallback(OnSettingEdited);
            _maxLife.RegisterValueChangedCallback(OnSettingEdited);
            _drain.RegisterValueChangedCallback(OnSettingEdited);
            _roundSeconds.RegisterValueChangedCallback(OnSettingEdited);

            AttachDisconnectWatch();

            ShowWhicheverScreenTheSessionCallsFor();
        }

        /// <summary>
        /// Opens on the lobby when there is a session to be in, and on the menu when there is not.
        /// </summary>
        /// <remarks>
        /// This runs every time the scene is loaded, and the scene is loaded every time a match
        /// ends — so assuming the menu, which is what this used to do, meant <i>Return to lobby</i>
        /// put a player back on the host-or-join screen with their session still running. Nobody was
        /// ever disconnected by it; the screen simply forgot.
        /// </remarks>
        void ShowWhicheverScreenTheSessionCallsFor()
        {
            if (Session != null && Session.InSession)
            {
                ShowLobby(Session.IsHosting, Session.JoinTarget);
                return;
            }

            ShowMenu();
        }

        void OnDisable()
        {
            DetachDisconnectWatch();

            _host.clicked -= OnHostClicked;
            _join.clicked -= OnJoinClicked;
            _cancel.clicked -= OnCancelClicked;
            _refresh.clicked -= OnRefreshClicked;
            _joinConfirm.clicked -= OnJoinConfirmClicked;
            _joinCancel.clicked -= OnCancelClicked;
            _back.clicked -= OnBackClicked;
            _ready.clicked -= OnReadyClicked;
            _start.clicked -= OnStartClicked;
            _leave.clicked -= OnLeaveClicked;
            _copyCode.clicked -= OnCopyCodeClicked;

            _preset.UnregisterValueChangedCallback(OnPresetPicked);
            _startingLife.UnregisterValueChangedCallback(OnSettingEdited);
            _maxLife.UnregisterValueChangedCallback(OnSettingEdited);
            _drain.UnregisterValueChangedCallback(OnSettingEdited);
            _roundSeconds.UnregisterValueChangedCallback(OnSettingEdited);

            DetachDirector();
            DetachRoster();

            _attempt?.Cancel();
            _attempt?.Dispose();
        }

        /// <remarks>
        /// Built on first use, not in <c>Awake</c>: <see cref="NetworkManager.Singleton"/> is
        /// assigned during the NetworkManager's own <c>Awake</c> and nothing orders the two.
        /// </remarks>
        /// <summary>
        /// The connection this screen is a view of, or null when the bootstrap scene is absent.
        /// </summary>
        /// <remarks>
        /// Owned by <see cref="SessionConnection"/> in the bootstrap scene rather than built here.
        /// This scene is unloaded whenever a match runs, and everything it held privately went with
        /// it — which is why returning from a match used to land on the host-or-join screen with the
        /// session still running underneath.
        /// </remarks>
        static SessionConnection Session => SessionConnection.Current;

        IConnectionProvider Provider => Session != null ? Session.Provider : null;

        /// <summary>
        /// Relabels the address field for whichever provider is in use.
        /// </summary>
        /// <remarks>
        /// This is the entire visible difference between playing over a LAN and playing over Relay:
        /// one field is called "Address" and holds an IP, the other is called "Code" and holds six
        /// characters. If adding Relay had required more of this file than a label and a constructor
        /// call, <see cref="IConnectionProvider"/> would not have been worth having.
        /// </remarks>
        void ApplyProviderLabels()
        {
            // Touches the property so the provider is built if it can be; called on a schedule from
            // OnEnable, by which point NetworkManager has woken.
            if (Provider == null || _target == null) return;

            _target.label = Provider.JoinsByCode ? "Code" : "Address";

            // Hidden whole rather than left empty: an empty list with a Refresh button that can
            // never find anything is a broken feature, not an absent one.
            _browseSection.EnableInClassList("hidden", !Provider.CanBrowse);

            if (Provider.JoinsByCode)
            {
                _target.value = string.Empty;
                _target.textEdition.placeholder = "ABC123";
            }
            else if (string.IsNullOrEmpty(_target.value))
            {
                _target.value = "127.0.0.1";
            }
        }

        // ==================================================================================
        //  Menu
        // ==================================================================================

        async void OnHostClicked()
        {
            if (Provider == null) return;

            BeginAttempt(_menuStatus, "Starting…");
            ConnectionResult result = await Provider.HostAsync(
                ConnectionRequest.Host(_nickname.value), _attempt.Token);

            EndAttempt(result, _menuStatus, hosting: true);
        }

        /// <remarks>
        /// Opens the join screen rather than connecting. The code field used to sit on the front
        /// screen next to Host, where it means nothing — a host never types one — and a player
        /// arriving at the game was asked for something they may not have before being told what it
        /// was for.
        /// </remarks>
        void OnJoinClicked() => ShowJoinScreen();

        void OnBackClicked() => ShowMenu();

        async void OnJoinConfirmClicked()
        {
            if (Provider == null) return;

            BeginAttempt(_joinStatus, "Connecting…");
            ConnectionResult result = await Provider.JoinAsync(
                ConnectionRequest.Join(_target.value, _nickname.value), _attempt.Token);

            EndAttempt(result, _joinStatus, hosting: false);
        }

        /// <remarks>
        /// The listing is captured by the row that was clicked, so what gets joined is what was on
        /// screen. Reading a selected index instead would join whichever game had slid into that
        /// position by the time the click landed.
        /// </remarks>
        async void OnListingClicked(SessionListing listing)
        {
            if (Provider == null) return;

            BeginAttempt(_joinStatus, $"Joining {listing.Name}…");
            ConnectionResult result = await Provider.JoinAsync(
                ConnectionRequest.JoinListed(listing, _nickname.value), _attempt.Token);

            EndAttempt(result, _joinStatus, hosting: false);
        }

        void OnCancelClicked() => _attempt?.Cancel();

        void BeginAttempt(Label status, string message)
        {
            _attempt?.Dispose();
            _attempt = new CancellationTokenSource();

            SetBusy(true);
            SetStatus(status, message, isError: false);
        }

        /// <param name="status">
        /// The status line of the screen that started this, so a join that failed says so on the
        /// join screen rather than on a front screen the player is no longer looking at.
        /// </param>
        void EndAttempt(ConnectionResult result, Label status, bool hosting)
        {
            SetBusy(false);

            if (!result.Success)
            {
                SetStatus(status, result.PlayerFacingMessage, isError: true);

                if (!string.IsNullOrEmpty(result.Diagnostic))
                    Debug.LogWarning($"[Snackdown] Connection failed ({result.Failure}): {result.Diagnostic}");

                return;
            }

            SetStatus(status, string.Empty, isError: false);

            // Remembered on the way in rather than as it is typed: a name abandoned at the menu is
            // not a choice, and greeting the player with it next time would be one.
            NicknamePreference.Remember(_nickname.value);

            if (Session != null) Session.Remember(result);

            ShowLobby(hosting, result.JoinTarget);
        }

        /// <remarks>
        /// Both Cancel buttons are toggled although only one screen is visible. Tracking which one
        /// to touch would mean this method knowing where the player is, and the cost of getting it
        /// wrong — a Cancel that does not appear during an attempt — is worse than the cost of
        /// showing a button nobody can see.
        /// </remarks>
        void SetBusy(bool busy)
        {
            // Disabled rather than hidden: a button that vanishes mid-click reads as a crash, and
            // starting a second attempt over a half-open one fails in ways nobody can explain.
            _host.SetEnabled(!busy);
            _join.SetEnabled(!busy);
            _joinConfirm.SetEnabled(!busy);
            _refresh.SetEnabled(!busy);
            _back.SetEnabled(!busy);
            _browseList.SetEnabled(!busy);

            _cancel.EnableInClassList("hidden", !busy);
            _joinCancel.EnableInClassList("hidden", !busy);
        }

        void ShowMenu()
        {
            DetachDirector();
            DetachRoster();

            _menuScreen.RemoveFromClassList("hidden");
            _joinScreen.AddToClassList("hidden");
            _lobbyScreen.AddToClassList("hidden");
            SetBusy(false);
        }

        // ==================================================================================
        //  Join
        // ==================================================================================

        /// <remarks>
        /// The browse starts with the screen rather than waiting for a Refresh. Opening onto an
        /// empty list with a button that would fill it asks the player to do the one thing the
        /// screen exists to do for them.
        /// </remarks>
        void ShowJoinScreen()
        {
            _menuScreen.AddToClassList("hidden");
            _joinScreen.RemoveFromClassList("hidden");
            _lobbyScreen.AddToClassList("hidden");

            SetBusy(false);
            SetStatus(_joinStatus, string.Empty, isError: false);

            if (Provider != null && Provider.CanBrowse) RefreshListings();
        }

        void OnRefreshClicked() => RefreshListings();

        /// <remarks>
        /// <para>Nothing polls. The service rate-limits queries and a timer writing into a list the
        /// player may have left would be spending someone's quota on a screen nobody is looking
        /// at.</para>
        /// <para>The result is dropped if the screen has been left in the meantime, which is what
        /// the visibility check is doing at the end: this is an <c>async void</c> whose await can
        /// outlive the reason it was started.</para>
        /// </remarks>
        async void RefreshListings()
        {
            if (Provider == null || !Provider.CanBrowse) return;

            _refresh.SetEnabled(false);
            SetStatus(_joinStatus, "Looking for games…", isError: false);

            BrowseResult result = await Provider.BrowseAsync();

            if (_joinScreen == null || _joinScreen.ClassListContains("hidden")) return;

            _refresh.SetEnabled(true);

            if (!result.Success)
            {
                SetStatus(_joinStatus, result.PlayerFacingMessage, isError: true);

                if (!string.IsNullOrEmpty(result.Diagnostic))
                    Debug.LogWarning($"[Snackdown] Browsing failed ({result.Failure}): {result.Diagnostic}");

                return;
            }

            SetStatus(_joinStatus, string.Empty, isError: false);
            RenderListings(result.Sessions);
        }

        /// <remarks>
        /// Rebuilt rather than diffed. The roster below is diffed because a row disappearing under a
        /// cursor mid-match matters; this list is redrawn only when the player asked for it to be.
        /// </remarks>
        void RenderListings(IReadOnlyList<SessionListing> sessions)
        {
            _browseList.Clear();

            if (sessions.Count == 0)
            {
                var empty = new Label("Nobody is hosting right now. Start one, or type a code.");
                empty.AddToClassList("browse__empty");
                _browseList.Add(empty);
                return;
            }

            for (int i = 0; i < sessions.Count; i++)
            {
                SessionListing listing = sessions[i];

                // A Button rather than a clickable div, so it is reachable by keyboard and reads as
                // a control to anything inspecting the panel.
                var row = new Button(() => OnListingClicked(listing));
                row.AddToClassList("browse__row");
                row.text = string.Empty;

                var name = new Label(listing.Name);
                name.AddToClassList("browse__name");

                var slots = new Label($"{listing.Players}/{listing.MaxPlayers}");
                slots.AddToClassList("browse__slots");

                if (listing.IsFull)
                {
                    row.SetEnabled(false);
                    row.AddToClassList("browse__row--full");
                }

                row.Add(name);
                row.Add(slots);
                _browseList.Add(row);
            }
        }

        // ==================================================================================
        //  Lobby
        // ==================================================================================

        void ShowLobby(bool hosting, string joinTarget)
        {
            _menuScreen.AddToClassList("hidden");
            _joinScreen.AddToClassList("hidden");
            _lobbyScreen.RemoveFromClassList("hidden");

            _rawJoinTarget = joinTarget ?? string.Empty;

            _joinCode.text = string.IsNullOrEmpty(joinTarget)
                ? string.Empty
                : $"Others join at  {joinTarget}";

            // The whole row goes, not just the label: a lone Copy button with nothing beside it
            // would offer to copy nothing.
            bool hasTarget = !string.IsNullOrEmpty(joinTarget);
            _joinCode.EnableInClassList("hidden", !hasTarget);
            _copyCode.EnableInClassList("hidden", !hasTarget);

            // Only the host can start, so only the host is offered the button. Hiding it elsewhere
            // beats disabling it: a permanently greyed button invites clicking to find out why.
            _start.EnableInClassList("hidden", !hosting);

            // Tried again here as well as on enable. The manager lives in the bootstrap scene and
            // this document in the lobby one, and the ordering between them is not something either
            // can promise — the same reason the provider labels are applied a frame late. Reaching
            // the lobby is the point past which there is certainly a session to lose.
            AttachDisconnectWatch();

            AttachDirector();
            AttachRoster();
            RefreshRoster();
        }

        /// <summary>
        /// Watches for this machine losing the session, so the lobby does not outlive it.
        /// </summary>
        /// <remarks>
        /// Written for being kicked and it fixes more than that: until now a client whose host quit
        /// sat in a lobby screen listing players who were gone, with a Ready button that reached
        /// nobody. Any end to the connection lands here.
        /// </remarks>
        void AttachDisconnectWatch()
        {
            if (_watchingDisconnects || NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientDisconnectCallback += OnSomebodyDisconnected;
            _watchingDisconnects = true;
        }

        void DetachDisconnectWatch()
        {
            if (!_watchingDisconnects || NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientDisconnectCallback -= OnSomebodyDisconnected;
            _watchingDisconnects = false;
        }

        /// <remarks>
        /// <para>The host is told about everyone who leaves and this is not about them — their
        /// roster row disappears on its own. Only this machine losing its own connection sends the
        /// screen back.</para>
        /// <para><c>DisconnectReason</c> is whatever the server passed to <c>DisconnectClient</c>,
        /// which for a kick is <see cref="SessionRoster.KickReason"/> and for a host that quit is
        /// nothing at all. Empty is the ordinary case, not an error, so it gets a sentence of its
        /// own rather than a blank status line.</para>
        /// </remarks>
        void OnSomebodyDisconnected(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsServer) return;
            if (clientId != manager.LocalClientId) return;

            string reason = manager.DisconnectReason;

            ShowMenu();
            SetStatus(_menuStatus,
                string.IsNullOrWhiteSpace(reason) ? "The session ended." : reason,
                isError: true);
        }

        // ==================================================================================
        //  Rules
        // ==================================================================================

        void AttachDirector()
        {
            if (_director != null) return;

            _director = FindFirstObjectByType<MatchDirector>();
            if (_director == null) return;

            _director.SettingsChanged += OnSettingsReplicated;
            FillPresetChoices();
            RefreshSettings();
        }

        void DetachDirector()
        {
            if (_director == null) return;

            _director.SettingsChanged -= OnSettingsReplicated;
            _director = null;
        }

        void OnSettingsReplicated(MatchSettings settings) => RefreshSettings();

        /// <remarks>
        /// The presets are authored content, so the list is built once from whatever the catalog
        /// holds rather than hardcoded here. A session with no catalog assigned hides the row
        /// instead of showing an empty dropdown that does nothing when opened.
        /// </remarks>
        void FillPresetChoices()
        {
            _preset.choices.Clear();

            for (int i = 0; i < _director.PresetCount; i++)
                _preset.choices.Add(_director.PresetName(i));

            _presetRow.EnableInClassList("hidden", _director.PresetCount == 0);
        }

        /// <summary>
        /// Shows the numbers in force, and lets the host move them only when a move would take.
        /// </summary>
        /// <remarks>
        /// <para>Everyone sees them, which is the point of replicating them at all: a player agreeing
        /// to ready up should be able to see what they are agreeing to. Only the host gets fields
        /// that respond, and the server refuses anything else regardless.</para>
        /// <para>Disabled rather than hidden for a client, unlike the Start button. A control that is
        /// absent says "this does not exist"; one that is greyed says "this is not yours", and here
        /// the second is true and worth saying — the numbers are the rules of the match they are
        /// about to play.</para>
        /// </remarks>
        void RefreshSettings()
        {
            if (_director == null || _settingsPanel == null) return;

            MatchSettings settings = _director.Rules;

            bool hosting = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            // Between matches only. The server refuses a change once a match is under way, so a
            // field that still accepted typing would be a control that silently does nothing.
            bool changeable = hosting
                              && (_director.Phase == MatchPhase.Lobby || _director.Phase == MatchPhase.Ended);

            _preset.SetEnabled(changeable);
            _startingLife.SetEnabled(changeable);
            _maxLife.SetEnabled(changeable);
            _drain.SetEnabled(changeable);
            _roundSeconds.SetEnabled(changeable);

            _fillingFields = true;

            _startingLife.value = settings.StartingLife;
            _maxLife.value = settings.MaxLife;
            _drain.value = settings.DrainPerSecond;
            _roundSeconds.value = settings.RoundSeconds;

            _fillingFields = false;
        }

        void OnPresetPicked(ChangeEvent<string> change)
        {
            if (_fillingFields || _director == null) return;

            int index = _preset.choices.IndexOf(change.newValue);
            if (index < 0) return;

            _director.RequestPreset(index);
        }

        /// <remarks>
        /// Sends all four rather than the one that moved. The struct is the unit the server clamps
        /// and replicates, and a per-field message would have to be reassembled against a copy that
        /// may already have moved underneath it.
        /// </remarks>
        void OnSettingEdited(ChangeEvent<float> change)
        {
            if (_fillingFields || _director == null) return;

            _director.RequestSettings(new MatchSettings
            {
                StartingLife = _startingLife.value,
                MaxLife = _maxLife.value,
                DrainPerSecond = _drain.value,
                RoundSeconds = _roundSeconds.value,

                // Not offered in the lobby: it is a bandwidth trade, not a rule, and nothing a
                // player would recognise. Carried through so editing a field cannot reset it.
                LifeReplicationHz = _director.Rules.LifeReplicationHz
            });
        }

        void AttachRoster()
        {
            if (_roster != null) return;

            _roster = FindFirstObjectByType<SessionRoster>();
            if (_roster != null) _roster.Changed += RefreshRoster;
        }

        void DetachRoster()
        {
            if (_roster == null) return;

            _roster.Changed -= RefreshRoster;
            _roster = null;
        }

        void RefreshRoster()
        {
            _rosterList.Clear();

            if (_roster == null)
            {
                SetStatus(_lobbyStatus, "No roster in this scene.", isError: true);
                return;
            }

            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            // Asked of the connection rather than remembered from the button that was clicked: the
            // host is the server, and one of those two facts cannot drift.
            bool hosting = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            for (int i = 0; i < _roster.Count; i++)
            {
                PlayerSession player = _roster[i];
                bool isYou = player.OwnerClientId == localId;

                var row = new VisualElement();
                row.AddToClassList("roster-row");

                var name = new Label(player.Nickname + (isYou ? "  (you)" : string.Empty));
                name.AddToClassList("roster-row__name");
                if (isYou) name.AddToClassList("roster-row__you");

                var state = new Label(player.IsReady ? "READY" : "waiting");
                state.AddToClassList("roster-row__state");
                if (player.IsReady) state.AddToClassList("roster-row__state--ready");

                row.Add(name);
                row.Add(state);

                // Offered only where it can be used, and never against yourself — a host kicking
                // itself would end the session for everyone, which is quitting with extra steps.
                if (hosting && !isYou)
                {
                    ulong target = player.OwnerClientId;

                    var kick = new Button(() => _roster?.RequestKick(target)) { text = "Kick" };
                    kick.AddToClassList("roster-row__kick");
                    row.Add(kick);
                }

                _rosterList.Add(row);
            }

            int maxPlayers = Session != null ? Session.MaxPlayers : _roster.Count;

            _lobbySubtitle.text = _roster.Count == 1
                ? "Waiting for players…"
                : $"{_roster.Count} of {maxPlayers} players";

            _ready.text = IsLocalReady() ? "Not ready" : "Ready";

            // Serialized rather than hardcoded to 2 so the arena can be walked around alone while
            // developing. It gates a button, not the match: MatchDirector does not care how many
            // players there are, so lowering it cannot put the session into a state the server
            // disagrees with.
            _start.SetEnabled(_roster.EveryoneReady && _roster.Count >= _minPlayersToStart);

            // The rules panel rides the same redraw. Whether the host may still change a number
            // depends on the phase, and nothing else here is watching for that.
            RefreshSettings();

            // So does the wardrobe: which skins are free is a fact about the roster, and the roster
            // is what just changed.
            RefreshWardrobe();
        }

        // ==================================================================================
        //  Wardrobe
        // ==================================================================================

        /// <summary>
        /// Draws one button per skin, showing which are taken and which is yours.
        /// </summary>
        /// <remarks>
        /// <para>Rebuilt rather than patched, like the roster and for the same reason: there are at
        /// most a handful of buttons, and a full rebuild cannot drift out of step with who is
        /// wearing what.</para>
        /// <para>Taken skins are drawn and disabled rather than hidden. A wardrobe that removed
        /// costumes as people took them would change length while you were reaching for one, and
        /// leave a player unable to tell "somebody has it" from "it does not exist".</para>
        /// <para>The buttons are a courtesy. The server refuses a taken skin whatever the screen
        /// says, which is what makes this safe to be wrong about for a frame.</para>
        /// </remarks>
        void RefreshWardrobe()
        {
            if (_wardrobeRow == null) return;

            _wardrobeRow.Clear();

            CharacterCatalog catalog = Session != null ? Session.Skins : null;
            PlayerSession me = _roster != null ? _roster.Local : null;

            // Nothing to choose from, or nobody to choose for. An empty row rather than an empty
            // frame around nothing.
            if (catalog == null || catalog.Count == 0 || me == null)
            {
                _wardrobeRow.AddToClassList("hidden");
                return;
            }

            _wardrobeRow.RemoveFromClassList("hidden");

            bool changeable = _director == null
                              || _director.Phase == MatchPhase.Lobby
                              || _director.Phase == MatchPhase.Ended;

            for (int index = 0; index < catalog.Count; index++)
            {
                int skin = index;
                bool mine = me.CharacterIndex == skin;
                bool taken = !mine && IsSkinTaken(skin);

                var button = new Button(() => _roster?.Local?.RequestCharacter(skin));
                button.AddToClassList("wardrobe__skin");
                button.EnableInClassList("wardrobe__skin--yours", mine);
                button.EnableInClassList("wardrobe__skin--taken", taken);

                CharacterCatalog.Entry entry = catalog.Get(skin);
                button.tooltip = entry.DisplayName;

                if (entry.Portrait != null) button.style.backgroundImage = new StyleBackground(entry.Portrait);
                else button.text = entry.DisplayName;

                button.SetEnabled(changeable && !taken && !mine);

                _wardrobeRow.Add(button);
            }
        }

        /// <remarks>
        /// Read off the roster rather than asked of the server. Approval owns the answer and refuses
        /// anything else, so this only has to be right often enough to keep a player from clicking
        /// something that will bounce.
        /// </remarks>
        bool IsSkinTaken(int skin)
        {
            if (_roster == null) return false;

            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].CharacterIndex == skin) return true;
            }

            return false;
        }

        bool IsLocalReady()
        {
            PlayerSession local = _roster == null ? null : _roster.Local;
            return local != null && local.IsReady;
        }

        void OnReadyClicked() => _roster?.Local?.ToggleReady();

        /// <summary>
        /// Puts the bare join code on the clipboard, so it can be pasted into a chat.
        /// </summary>
        /// <remarks>
        /// Copies <see cref="_rawJoinTarget"/> rather than the label's text, which reads "Others
        /// join at ABC123" — pasting that sentence into a code field would fail, and the player
        /// would blame the code.
        /// </remarks>
        void OnCopyCodeClicked()
        {
            if (string.IsNullOrEmpty(_rawJoinTarget)) return;

            GUIUtility.systemCopyBuffer = _rawJoinTarget;
            SetStatus(_lobbyStatus, $"Copied  {_rawJoinTarget}", isError: false);
        }

        /// <remarks>
        /// Only the host reaches this — the button is hidden for everyone else — and the director
        /// checks server authority again anyway. The UI hiding a control is a courtesy; it is not
        /// what stops a client from starting a match.
        /// </remarks>
        void OnStartClicked()
        {
            MatchDirector director = MatchDirector.Current;

            if (director == null)
            {
                SetStatus(_lobbyStatus, "No match director in the session.", isError: true);
                return;
            }

            SetStatus(_lobbyStatus, "Loading the arena…", isError: false);
            director.ServerStartMatch(0);
        }

        async void OnLeaveClicked()
        {
            _attempt?.Cancel();

            if (Provider != null) await Provider.LeaveAsync();

            if (Session != null) Session.Forget();

            ShowMenu();
            SetStatus(_lobbyStatus, string.Empty, isError: false);
        }

        // ==================================================================================

        static void SetStatus(Label label, string message, bool isError)
        {
            label.text = message ?? string.Empty;
            label.EnableInClassList("status--error", isError && !string.IsNullOrEmpty(message));
        }

    }
}
