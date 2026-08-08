using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Snackdown.Connection
{
    /// <summary>
    /// Connects players over Unity Relay, so joining needs a six-character code instead of an
    /// address and works across the internet without anyone opening a port.
    /// </summary>
    /// <remarks>
    /// <para>Relay is a rendezvous server: neither peer connects to the other, both connect to
    /// Unity and it forwards between them. That removes the two things that make direct play
    /// impractical outside a LAN — a router that drops unsolicited inbound traffic, and having to
    /// hand out your public IP.</para>
    /// <para>Built on the <b>Sessions</b> API rather than on Relay and Lobby separately. Sessions
    /// wraps both: it allocates the relay, publishes the lobby entry, and hands back a join code,
    /// where doing it by hand means sequencing three services and cleaning up whichever one
    /// succeeded when the next fails.</para>
    /// <para>Two things happen here that never happen on a LAN. Services have to be initialized and
    /// the player signed in — anonymously, so nobody is asked to make an account to play — and both
    /// are slow, fallible and worth cancelling. This is why <see cref="IConnectionProvider"/> is
    /// asynchronous even though <see cref="DirectConnectionProvider"/> had nothing to wait for.</para>
    /// </remarks>
    public class RelayConnectionProvider : IConnectionProvider
    {
        readonly NetworkManager _networkManager;
        readonly ConnectionApproval _approval;
        readonly string _gameVersion;
        readonly int _maxPlayers;

        ISession _session;

        public string DisplayName => "Online / Relay";
        public bool JoinsByCode => true;

        /// <summary>The code other players type to join, once hosting. Empty otherwise.</summary>
        public string JoinCode => _session?.Code ?? string.Empty;

        public RelayConnectionProvider(
            NetworkManager networkManager,
            ConnectionApproval approval,
            string gameVersion,
            int maxPlayers)
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));

            _approval = approval;
            _gameVersion = gameVersion ?? string.Empty;
            _maxPlayers = Mathf.Max(1, maxPlayers);
        }

        public async Task<ConnectionResult> HostAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
        {
            ConnectionResult ready = await PrepareAsync(cancellationToken);
            if (!ready.Success) return ready;

            // Armed before the session starts the transport, for the same reason as on a LAN: NGO
            // reads ConnectionApproval when the server comes up, so enabling it later would let the
            // first joiner through unchecked. The host's own details go with it, since they never
            // travel as a payload.
            _approval?.SetLocalPlayer(request.Nickname, request.CharacterIndex);
            _approval?.Enable();

            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = _maxPlayers
                }.WithRelayNetwork();

                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                return ConnectionResult.Ok(_session.Code);
            }
            catch (OperationCanceledException)
            {
                _approval?.Disable();
                return ConnectionResult.Failed(ConnectionFailure.Cancelled);
            }
            catch (SessionException e)
            {
                _approval?.Disable();
                return ConnectionResult.Failed(Classify(e), e.Message);
            }
            catch (Exception e)
            {
                _approval?.Disable();
                return ConnectionResult.Failed(ConnectionFailure.Error, e.Message);
            }
        }

        public async Task<ConnectionResult> JoinAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
        {
            string code = request.Target?.Trim();

            if (string.IsNullOrEmpty(code))
                return ConnectionResult.Failed(ConnectionFailure.NotFound, "no join code given");

            ConnectionResult ready = await PrepareAsync(cancellationToken);
            if (!ready.Success) return ready;

            // The payload rides the handshake exactly as it does on a LAN. Relay changes how the
            // packets travel, not what approval gets to inspect.
            _networkManager.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = _gameVersion,
                Nickname = Truncate(request.Nickname, ConnectionApproval.MaxNicknameLength),
                CharacterIndex = request.CharacterIndex
            }.ToBytes();

            try
            {
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.ToUpperInvariant());
                return ConnectionResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return ConnectionResult.Failed(ConnectionFailure.Cancelled);
            }
            catch (SessionException e)
            {
                return ConnectionResult.Failed(Classify(e), e.Message);
            }
            catch (Exception e)
            {
                return ConnectionResult.Failed(ConnectionFailure.Error, e.Message);
            }
        }

        public async Task LeaveAsync()
        {
            _approval?.Disable();

            if (_session != null)
            {
                try
                {
                    await _session.LeaveAsync();
                }
                catch (Exception e)
                {
                    // Leaving is best-effort. The session times out on its own, and refusing to
                    // return here would strand the player on a lobby screen they already left.
                    Debug.LogWarning($"[Snackdown] Leaving the session failed: {e.Message}");
                }

                _session = null;
            }

            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
        }

        /// <summary>
        /// Brings up services and signs the player in, once per run.
        /// </summary>
        /// <remarks>
        /// Anonymous sign-in on purpose: Relay needs an authenticated identity, but requiring an
        /// account to play a platformer would be a worse trade than any feature it buys. The token
        /// is cached by the SDK, so a returning player keeps the same id.
        /// </remarks>
        async Task<ConnectionResult> PrepareAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                cancellationToken.ThrowIfCancellationRequested();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                cancellationToken.ThrowIfCancellationRequested();
                return ConnectionResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return ConnectionResult.Failed(ConnectionFailure.Cancelled);
            }
            catch (Exception e)
            {
                // Almost always a project that is not linked, or Relay not enabled on it. Naming
                // that is far more useful than the SDK's own wording.
                return ConnectionResult.Failed(ConnectionFailure.Error,
                    $"Unity Services unavailable — is the project linked and Relay enabled? ({e.Message})");
            }
        }

        /// <summary>Maps the SDK's error codes onto the outcomes the menu knows how to render.</summary>
        /// <remarks>
        /// Only the cases a player can act on are translated. Everything else stays
        /// <see cref="ConnectionFailure.Error"/> deliberately: inventing a friendly message for
        /// <c>InvalidMatchmakerState</c> would tell someone to retry something that will never work.
        /// </remarks>
        static ConnectionFailure Classify(SessionException e) => e.Error switch
        {
            // A mistyped or expired code, which is the overwhelmingly common failure.
            SessionError.SessionNotFound => ConnectionFailure.NotFound,
            SessionError.SessionDeleted => ConnectionFailure.NotFound,

            // Reached it, and it said no.
            SessionError.Forbidden => ConnectionFailure.Rejected,
            SessionError.NotAuthorized => ConnectionFailure.Rejected,

            // Backing off and trying again is genuinely the right move here.
            SessionError.RateLimitExceeded => ConnectionFailure.TimedOut,

            _ => ConnectionFailure.Error
        };

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
