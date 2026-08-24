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
        [Tooltip("Port used for direct connections.")]
        [SerializeField] ushort _port = 7777;

        [Tooltip("Players allowed in one session. Enforced by connection approval.")]
        [SerializeField] int _maxPlayers = 4;

        [Tooltip("Start on Relay (join by code) instead of direct LAN (join by address).")]
        [SerializeField] bool _useRelay = true;

        [Tooltip("Players needed before a match can start. Set to 1 to test the arena alone.")]
        [Min(1)]
        [SerializeField] int _minPlayersToStart = 2;

        UIDocument _document;
        IConnectionProvider _provider;
        ConnectionApproval _approval;
        SessionRoster _roster;
        CancellationTokenSource _attempt;

        VisualElement _menuScreen;
        VisualElement _lobbyScreen;
        TextField _nickname;
        TextField _target;
        Button _host;
        Button _join;
        Button _cancel;
        Button _ready;
        Button _start;
        Button _leave;
        Label _menuStatus;
        Label _lobbyStatus;
        Label _lobbySubtitle;
        Label _joinCode;
        Button _copyCode;
        VisualElement _rosterList;

        /// <summary>The bare code, without the sentence wrapped around it for display.</summary>
        string _rawJoinTarget = string.Empty;

        bool _watchingDisconnects;

        static string GameVersion => Application.version;

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _menuScreen = root.Q<VisualElement>("menu-screen");
            _lobbyScreen = root.Q<VisualElement>("lobby-screen");
            _nickname = root.Q<TextField>("nickname-field");
            _target = root.Q<TextField>("target-field");
            _host = root.Q<Button>("host-button");
            _join = root.Q<Button>("join-button");
            _cancel = root.Q<Button>("cancel-button");
            _ready = root.Q<Button>("ready-button");
            _start = root.Q<Button>("start-button");
            _leave = root.Q<Button>("leave-button");
            _menuStatus = root.Q<Label>("menu-status");
            _lobbyStatus = root.Q<Label>("lobby-status");
            _lobbySubtitle = root.Q<Label>("lobby-subtitle");
            _joinCode = root.Q<Label>("join-code");
            _copyCode = root.Q<Button>("copy-code-button");
            _rosterList = root.Q<VisualElement>("roster-list");

            _nickname.value = DefaultNickname();

            // Deferred a frame rather than read here: the provider needs NetworkManager.Singleton,
            // which is assigned in its own Awake with no ordering against this one. Without the
            // delay the field kept its LAN label until the first click built the provider — so the
            // menu asked for an address while the game was about to ask Relay for a code.
            _document.rootVisualElement.schedule.Execute(ApplyProviderLabels);

            _host.clicked += OnHostClicked;
            _join.clicked += OnJoinClicked;
            _cancel.clicked += OnCancelClicked;
            _ready.clicked += OnReadyClicked;
            _start.clicked += OnStartClicked;
            _leave.clicked += OnLeaveClicked;
            _copyCode.clicked += OnCopyCodeClicked;

            AttachDisconnectWatch();

            ShowMenu();
        }

        void OnDisable()
        {
            DetachDisconnectWatch();

            _host.clicked -= OnHostClicked;
            _join.clicked -= OnJoinClicked;
            _cancel.clicked -= OnCancelClicked;
            _ready.clicked -= OnReadyClicked;
            _start.clicked -= OnStartClicked;
            _leave.clicked -= OnLeaveClicked;
            _copyCode.clicked -= OnCopyCodeClicked;

            DetachRoster();

            _attempt?.Cancel();
            _attempt?.Dispose();
        }

        /// <remarks>
        /// Built on first use, not in <c>Awake</c>: <see cref="NetworkManager.Singleton"/> is
        /// assigned during the NetworkManager's own <c>Awake</c> and nothing orders the two.
        /// </remarks>
        IConnectionProvider Provider
        {
            get
            {
                if (_provider == null && NetworkManager.Singleton != null)
                {
                    _approval = new ConnectionApproval(NetworkManager.Singleton, GameVersion, _maxPlayers);

                    // The one line that knows there is more than one way to connect. Everything
                    // below this point talks to the interface and cannot tell them apart.
                    _provider = _useRelay
                        ? new RelayConnectionProvider(NetworkManager.Singleton, _approval, GameVersion, _maxPlayers)
                        : new DirectConnectionProvider(NetworkManager.Singleton, _port, _approval, GameVersion);
                }

                return _provider;
            }
        }

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

            _target.label = _provider.JoinsByCode ? "Code" : "Address";

            if (_provider.JoinsByCode)
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

            BeginAttempt("Starting…");
            ConnectionResult result = await Provider.HostAsync(
                ConnectionRequest.Host(_nickname.value), _attempt.Token);

            EndAttempt(result, hosting: true);
        }

        async void OnJoinClicked()
        {
            if (Provider == null) return;

            BeginAttempt("Connecting…");
            ConnectionResult result = await Provider.JoinAsync(
                ConnectionRequest.Join(_target.value, _nickname.value), _attempt.Token);

            EndAttempt(result, hosting: false);
        }

        void OnCancelClicked() => _attempt?.Cancel();

        void BeginAttempt(string message)
        {
            _attempt?.Dispose();
            _attempt = new CancellationTokenSource();

            SetBusy(true);
            SetStatus(_menuStatus, message, isError: false);
        }

        void EndAttempt(ConnectionResult result, bool hosting)
        {
            SetBusy(false);

            if (!result.Success)
            {
                SetStatus(_menuStatus, result.PlayerFacingMessage, isError: true);

                if (!string.IsNullOrEmpty(result.Diagnostic))
                    Debug.LogWarning($"[Snackdown] Connection failed ({result.Failure}): {result.Diagnostic}");

                return;
            }

            SetStatus(_menuStatus, string.Empty, isError: false);
            ShowLobby(hosting, result.JoinTarget);
        }

        void SetBusy(bool busy)
        {
            // Disabled rather than hidden: a button that vanishes mid-click reads as a crash, and
            // starting a second attempt over a half-open one fails in ways nobody can explain.
            _host.SetEnabled(!busy);
            _join.SetEnabled(!busy);
            _cancel.EnableInClassList("hidden", !busy);
        }

        // ==================================================================================
        //  Lobby
        // ==================================================================================

        void ShowMenu()
        {
            DetachRoster();

            _menuScreen.RemoveFromClassList("hidden");
            _lobbyScreen.AddToClassList("hidden");
            SetBusy(false);
        }

        void ShowLobby(bool hosting, string joinTarget)
        {
            _menuScreen.AddToClassList("hidden");
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

            _lobbySubtitle.text = _roster.Count == 1
                ? "Waiting for players…"
                : $"{_roster.Count} of {_maxPlayers} players";

            _ready.text = IsLocalReady() ? "Not ready" : "Ready";

            // Serialized rather than hardcoded to 2 so the arena can be walked around alone while
            // developing. It gates a button, not the match: MatchDirector does not care how many
            // players there are, so lowering it cannot put the session into a state the server
            // disagrees with.
            _start.SetEnabled(_roster.EveryoneReady && _roster.Count >= _minPlayersToStart);
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

            ShowMenu();
            SetStatus(_lobbyStatus, string.Empty, isError: false);
        }

        // ==================================================================================

        static void SetStatus(Label label, string message, bool isError)
        {
            label.text = message ?? string.Empty;
            label.EnableInClassList("status--error", isError && !string.IsNullOrEmpty(message));
        }

        /// <summary>A name that is not "Player", so a lobby of four is not four identical rows.</summary>
        static string DefaultNickname()
        {
            string device = SystemInfo.deviceName;
            return string.IsNullOrWhiteSpace(device) || device == "<unknown>" ? "Player" : device;
        }
    }
}
