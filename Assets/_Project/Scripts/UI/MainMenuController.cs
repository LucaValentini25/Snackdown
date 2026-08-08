using System.Threading;
using Snackdown.Connection;
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
        VisualElement _rosterList;

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
            _rosterList = root.Q<VisualElement>("roster-list");

            _nickname.value = DefaultNickname();
            _target.value = "127.0.0.1";

            _host.clicked += OnHostClicked;
            _join.clicked += OnJoinClicked;
            _cancel.clicked += OnCancelClicked;
            _ready.clicked += OnReadyClicked;
            _start.clicked += OnStartClicked;
            _leave.clicked += OnLeaveClicked;

            ShowMenu();
        }

        void OnDisable()
        {
            _host.clicked -= OnHostClicked;
            _join.clicked -= OnJoinClicked;
            _cancel.clicked -= OnCancelClicked;
            _ready.clicked -= OnReadyClicked;
            _start.clicked -= OnStartClicked;
            _leave.clicked -= OnLeaveClicked;

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
                    _provider = new DirectConnectionProvider(NetworkManager.Singleton, _port, _approval, GameVersion);
                }

                return _provider;
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

            _joinCode.text = string.IsNullOrEmpty(joinTarget)
                ? string.Empty
                : $"Others join at  {joinTarget}";
            _joinCode.EnableInClassList("hidden", string.IsNullOrEmpty(joinTarget));

            // Only the host can start, so only the host is offered the button. Hiding it elsewhere
            // beats disabling it: a permanently greyed button invites clicking to find out why.
            _start.EnableInClassList("hidden", !hosting);

            AttachRoster();
            RefreshRoster();
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

            for (int i = 0; i < _roster.Count; i++)
            {
                PlayerSlot slot = _roster[i];
                bool isYou = slot.ClientId == localId;

                var row = new VisualElement();
                row.AddToClassList("roster-row");

                var name = new Label(slot.Nickname.ToString() + (isYou ? "  (you)" : string.Empty));
                name.AddToClassList("roster-row__name");
                if (isYou) name.AddToClassList("roster-row__you");

                var state = new Label(slot.IsReady ? "READY" : "waiting");
                state.AddToClassList("roster-row__state");
                if (slot.IsReady) state.AddToClassList("roster-row__state--ready");

                row.Add(name);
                row.Add(state);
                _rosterList.Add(row);
            }

            _lobbySubtitle.text = _roster.Count == 1
                ? "Waiting for players…"
                : $"{_roster.Count} of {_maxPlayers} players";

            _ready.text = IsLocalReady() ? "Not ready" : "Ready";
            _start.SetEnabled(_roster.EveryoneReady && _roster.Count > 1);
        }

        bool IsLocalReady()
        {
            if (_roster == null || NetworkManager.Singleton == null) return false;

            for (int i = 0; i < _roster.Count; i++)
                if (_roster[i].ClientId == NetworkManager.Singleton.LocalClientId)
                    return _roster[i].IsReady;

            return false;
        }

        void OnReadyClicked() => _roster?.ToggleReady();

        void OnStartClicked()
            => SetStatus(_lobbyStatus, "Match start lands with Phase 3.", isError: false);

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
