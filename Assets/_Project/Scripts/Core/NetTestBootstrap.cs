using System.Threading;
using Snackdown.Connection;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Core
{
    /// <summary>
    /// A deliberately ugly Host / Join launcher for the test scene, now driving
    /// <see cref="IConnectionProvider"/> instead of the transport directly.
    /// </summary>
    /// <remarks>
    /// Phase 2 replaces this with a real menu. It is kept in the meantime because it is the only
    /// caller exercising the connection abstraction, and an abstraction with no caller is a guess.
    /// Every button maps to a provider call and renders whatever the provider reports — which is
    /// what the menu will do, with better art.
    /// </remarks>
    public class NetTestBootstrap : MonoBehaviour
    {
        [SerializeField] string _address = "127.0.0.1";
        [SerializeField] ushort _port = 7777;
        [SerializeField] string _nickname = "Player";

        IConnectionProvider _provider;
        CancellationTokenSource _attempt;

        bool _busy;
        string _status = string.Empty;
        string _joinTarget = string.Empty;

        /// <summary>
        /// Built on first use rather than in <c>Awake</c>.
        /// </summary>
        /// <remarks>
        /// <see cref="NetworkManager.Singleton"/> is assigned during the NetworkManager's own
        /// <c>Awake</c>, and nothing orders that against this one. Constructing the provider here
        /// threw a null argument on whichever script happened to wake first — which is the kind of
        /// bug that reproduces on one machine and not the next.
        /// </remarks>
        IConnectionProvider Provider
        {
            get
            {
                if (_provider == null && NetworkManager.Singleton != null)
                    _provider = new DirectConnectionProvider(NetworkManager.Singleton, _port);

                return _provider;
            }
        }

        void OnDestroy()
        {
            _attempt?.Cancel();
            _attempt?.Dispose();
        }

        void OnGUI()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            GUILayout.BeginArea(new Rect(12f, 12f, 260f, 260f));

            if (!networkManager.IsClient && !networkManager.IsServer) DrawOfflineControls();
            else DrawOnlineControls(networkManager);

            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);

            GUILayout.EndArea();
        }

        void DrawOfflineControls()
        {
            GUILayout.Label($"Snackdown — {Provider.DisplayName}");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(40f));
            _nickname = GUILayout.TextField(_nickname);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Provider.JoinsByCode ? "Code" : "IP", GUILayout.Width(40f));
            _address = GUILayout.TextField(_address);
            GUILayout.EndHorizontal();

            // Disabled rather than hidden while an attempt is in flight: a button that vanishes
            // reads as a crash, and a second StartClient over a half-open one fails obscurely.
            GUI.enabled = !_busy;

            if (GUILayout.Button("Host")) Attempt(forHosting: true);
            if (GUILayout.Button("Join")) Attempt(forHosting: false);

            GUI.enabled = true;

            if (_busy && GUILayout.Button("Cancel")) _attempt?.Cancel();
        }

        void DrawOnlineControls(NetworkManager networkManager)
        {
            string role = networkManager.IsHost ? "Host" : networkManager.IsServer ? "Server" : "Client";
            GUILayout.Label($"{role} — {networkManager.ConnectedClients.Count} connected");

            if (!string.IsNullOrEmpty(_joinTarget)) GUILayout.Label($"Others join at: {_joinTarget}");

            if (GUILayout.Button("Leave")) Leave();
        }

        /// <remarks>
        /// <c>async void</c> is confined to this handler and nothing awaits it. The work it starts
        /// is a Task that is awaited properly, and every failure inside comes back as a
        /// <see cref="ConnectionResult"/> rather than as an exception crossing this boundary —
        /// which is the distinction the original project got wrong.
        /// </remarks>
        async void Attempt(bool forHosting)
        {
            _attempt?.Dispose();
            _attempt = new CancellationTokenSource();

            _busy = true;
            _status = forHosting ? "Starting…" : "Connecting…";
            _joinTarget = string.Empty;

            ConnectionRequest request = forHosting
                ? ConnectionRequest.Host(_nickname)
                : ConnectionRequest.Join(_address, _nickname);

            ConnectionResult result = forHosting
                ? await Provider.HostAsync(request, _attempt.Token)
                : await Provider.JoinAsync(request, _attempt.Token);

            _busy = false;
            _status = result.Success ? string.Empty : result.PlayerFacingMessage;
            if (result.Success) _joinTarget = result.JoinTarget;

            if (!result.Success && !string.IsNullOrEmpty(result.Diagnostic))
                Debug.LogWarning($"[Snackdown] Connection failed ({result.Failure}): {result.Diagnostic}");
        }

        async void Leave()
        {
            _attempt?.Cancel();
            await Provider.LeaveAsync();

            _status = string.Empty;
            _joinTarget = string.Empty;
        }
    }
}
