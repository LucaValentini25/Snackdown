using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace Snackdown.Connection
{
    /// <summary>
    /// Connects two peers over a plain UDP socket: a LAN address and a port, no service in between.
    /// </summary>
    /// <remarks>
    /// <para>This is the provider that always works — no account, no internet, no quota — which
    /// makes it the one to develop and test against. It is also the honest fallback when a relay is
    /// unavailable, rather than a debug-only path that quietly rots.</para>
    /// <para>Hosting completes as soon as the socket binds, but joining does not: NGO's
    /// <c>StartClient</c> returns before the connection is approved, so success has to be awaited on
    /// the connect/disconnect callbacks. Reporting success at <c>StartClient</c> would mean the menu
    /// advances and the player lands in a lobby they were never admitted to.</para>
    /// </remarks>
    public class DirectConnectionProvider : IConnectionProvider
    {
        /// <summary>How long to wait for the server to approve or refuse us.</summary>
        /// <remarks>
        /// An unreachable address produces no answer at all rather than a refusal, so without a
        /// deadline the join would hang forever behind a spinner.
        /// </remarks>
        public const float JoinTimeoutSeconds = 10f;

        /// <summary>How long to wait for a deferred shutdown to actually finish.</summary>
        public const float ShutdownTimeoutSeconds = 5f;

        readonly NetworkManager _networkManager;
        readonly ushort _port;
        readonly ConnectionApproval _approval;
        readonly string _gameVersion;

        public string DisplayName => "Direct / LAN";
        public bool JoinsByCode => false;

        /// <summary>Who was admitted under what name. Null when hosting without approval.</summary>
        public ConnectionApproval Approval => _approval;

        public DirectConnectionProvider(
            NetworkManager networkManager,
            ushort port = 7777,
            ConnectionApproval approval = null,
            string gameVersion = "")
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));

            _port = port;
            _approval = approval;
            _gameVersion = gameVersion ?? string.Empty;
        }

        public Task<ConnectionResult> HostAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
        {
            if (_networkManager.IsListening)
                return Task.FromResult(ConnectionResult.Failed(ConnectionFailure.Error, "already listening"));

            if (!TryGetTransport(out UnityTransport transport))
                return Task.FromResult(ConnectionResult.Failed(ConnectionFailure.Error, "transport is not UnityTransport"));

            // Bind to every interface so peers on the LAN can reach us, while telling the caller the
            // address a human would type. Binding to the reported address alone would work over
            // loopback and fail for everyone else.
            transport.SetConnectionData("0.0.0.0", _port, "0.0.0.0");

            // Vetting has to be armed before the socket opens: NGO reads ConnectionApproval when
            // the server starts, so enabling it afterwards would let the first joiner in unchecked.
            // The host's own details go with it, since they never travel as a payload.
            _approval?.SetLocalPlayer(request.Nickname, request.CharacterIndex);
            _approval?.Enable();

            NetworkConfigReport.Log(_networkManager, "host");

            if (!_networkManager.StartHost())
            {
                _approval?.Disable();
                return Task.FromResult(ConnectionResult.Failed(ConnectionFailure.Error, "StartHost refused; is the port already bound?"));
            }

            return Task.FromResult(ConnectionResult.Ok(LocalAddress()));
        }

        public async Task<ConnectionResult> JoinAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
        {
            // Argument before state: an empty address is the caller's mistake regardless of what
            // this peer happens to be doing, and reporting it as "already listening" sends whoever
            // is debugging in the wrong direction.
            if (string.IsNullOrWhiteSpace(request.Target))
                return ConnectionResult.Failed(ConnectionFailure.NotFound, "no address given");

            if (_networkManager.IsListening)
                return ConnectionResult.Failed(ConnectionFailure.Error, "already listening");

            if (!TryGetTransport(out UnityTransport transport))
                return ConnectionResult.Failed(ConnectionFailure.Error, "transport is not UnityTransport");

            (bool resolved, string address) = await ResolveAsync(request.Target.Trim(), _port);
            if (!resolved)
                return ConnectionResult.Failed(ConnectionFailure.InvalidAddress, $"'{request.Target}' is not an address this transport accepts");

            transport.SetConnectionData(address, _port);

            // Must match the host, or the request message this client sends and the one the server
            // expects to read are different shapes. See ConnectionApproval.EnableOnClient.
            ConnectionApproval.EnableOnClient(_networkManager);

            // Travels with the handshake, so the server can refuse us before we cost it anything.
            _networkManager.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = _gameVersion,
                Nickname = Truncate(request.Nickname, ConnectionApproval.MaxNicknameLength),
                CharacterIndex = request.CharacterIndex
            }.ToBytes();

            NetworkConfigReport.Log(_networkManager, "client");

            var outcome = new TaskCompletionSource<ConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnConnected(ulong clientId)
            {
                if (clientId == _networkManager.LocalClientId) outcome.TrySetResult(ConnectionResult.Ok());
            }

            // Also fires when the server rejects us during approval, which is why a refusal and a
            // dropped connection arrive through the same callback and are reported the same way.
            void OnDisconnected(ulong clientId)
            {
                if (clientId == _networkManager.LocalClientId)
                    outcome.TrySetResult(ConnectionResult.Failed(
                        ConnectionFailure.Rejected, _networkManager.DisconnectReason));
            }

            _networkManager.OnClientConnectedCallback += OnConnected;
            _networkManager.OnClientDisconnectCallback += OnDisconnected;

            try
            {
                if (!_networkManager.StartClient())
                    return ConnectionResult.Failed(ConnectionFailure.Error, "StartClient refused");

                Task finished = await Task.WhenAny(
                    outcome.Task,
                    Task.Delay(TimeSpan.FromSeconds(JoinTimeoutSeconds), cancellationToken));

                if (finished == outcome.Task) return await outcome.Task;

                // Timed out, or the player cancelled. Either way we are half-connected and must not
                // be left that way — a client stuck mid-handshake cannot start a new attempt.
                await LeaveAsync();

                return cancellationToken.IsCancellationRequested
                    ? ConnectionResult.Failed(ConnectionFailure.Cancelled)
                    : ConnectionResult.Failed(ConnectionFailure.TimedOut, $"no answer within {JoinTimeoutSeconds:F0}s");
            }
            catch (OperationCanceledException)
            {
                await LeaveAsync();
                return ConnectionResult.Failed(ConnectionFailure.Cancelled);
            }
            finally
            {
                _networkManager.OnClientConnectedCallback -= OnConnected;
                _networkManager.OnClientDisconnectCallback -= OnDisconnected;
            }
        }

        /// <remarks>
        /// <see cref="NetworkManager.Shutdown"/> does not close anything by the time it returns — it
        /// defers the teardown to the next update. Returning immediately would make this method lie
        /// about its own contract: callers use it to get back to a state where hosting or joining is
        /// possible, and a host that is still listening refuses both. So it waits for the shutdown
        /// to actually land.
        /// </remarks>
        public async Task LeaveAsync()
        {
            // Unhooked even when nothing is running: Enable() may have been called by a HostAsync
            // whose StartHost then failed, and a stale callback would vet the next session with a
            // policy nobody chose.
            _approval?.Disable();

            if (_networkManager == null || !_networkManager.IsListening) return;

            _networkManager.Shutdown();

            float deadline = Time.realtimeSinceStartup + ShutdownTimeoutSeconds;
            while (_networkManager.IsListening && Time.realtimeSinceStartup < deadline)
                await Task.Yield();

            if (_networkManager.IsListening)
                Debug.LogWarning($"[Snackdown] Shutdown did not complete within {ShutdownTimeoutSeconds:F0}s.");
        }

        bool TryGetTransport(out UnityTransport transport)
        {
            transport = _networkManager.NetworkConfig?.NetworkTransport as UnityTransport;
            return transport != null;
        }

        /// <summary>
        /// Turns whatever the player typed into something the transport will accept, or reports
        /// that it cannot.
        /// </summary>
        /// <remarks>
        /// <para>Validation has to use the transport's own parser, because the obvious alternatives
        /// disagree with it. <c>10.0.01</c> is accepted by both <c>IPAddress.TryParse</c> and
        /// <c>Uri.CheckHostName</c> — .NET reads it as the abbreviated form of <c>10.0.0.1</c> —
        /// and then rejected by <c>NetworkEndpoint.TryParse</c>. Validating with the wrong one lets
        /// a typo through to the transport, which reports it as two console errors and a failed
        /// start rather than as something a menu can explain.</para>
        /// <para>That parser only accepts literal addresses, so anything else gets one DNS lookup
        /// before being called invalid. Otherwise <c>localhost</c> and machine names — which people
        /// on a LAN reasonably type — would be rejected as malformed.</para>
        /// </remarks>
        static async Task<(bool resolved, string address)> ResolveAsync(string target, ushort port)
        {
            if (NetworkEndpoint.TryParse(target, port, out _, NetworkFamily.Ipv4))
                return (true, target);

            // Digits and dots means they meant to type an address, and it did not parse as one. Do
            // not fall through to DNS: the OS resolves "10.0.01" to 10.0.0.1 by an old abbreviation
            // rule, so the typo would be silently corrected, the connection attempt would go
            // somewhere the player never asked for, and ten seconds later they would be told
            // nobody answered instead of that they mistyped.
            if (LooksLikeAnAddress(target)) return (false, null);

            try
            {
                System.Net.IPAddress[] found = await System.Net.Dns.GetHostAddressesAsync(target);

                foreach (System.Net.IPAddress address in found)
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return (true, address.ToString());
            }
            catch (Exception)
            {
                // Not a resolvable name either. Reported as an invalid address, which it is.
            }

            return (false, null);
        }

        /// <summary>
        /// Caps the nickname before it is written into the payload.
        /// </summary>
        /// <remarks>
        /// <c>FixedString32Bytes</c> throws when handed more than it can hold, so an over-long name
        /// typed into the menu would fail on the sender rather than being politely shortened by the
        /// server. The server sanitizes independently — this is not a substitute for that, since a
        /// hostile client does not run this code at all.
        /// </remarks>
        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        /// <summary>True when the text is made only of digits and dots — an attempt at an address
        /// rather than a hostname, however malformed.</summary>
        static bool LooksLikeAnAddress(string target)
        {
            foreach (char c in target)
                if (!char.IsDigit(c) && c != '.')
                    return false;

            return target.Length > 0;
        }

        /// <summary>
        /// The address a peer on the same network would type. Falls back to loopback, which is what
        /// Multiplayer Play Mode uses anyway.
        /// </summary>
        static string LocalAddress()
        {
            try
            {
                foreach (System.Net.IPAddress address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(address))
                        return address.ToString();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Snackdown] Could not resolve a LAN address: {e.Message}");
            }

            return "127.0.0.1";
        }
    }
}
