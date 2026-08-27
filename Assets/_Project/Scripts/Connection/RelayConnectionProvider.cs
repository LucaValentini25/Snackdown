using System;
using System.Collections.Generic;
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
        public bool CanBrowse => true;

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

            NetworkConfigReport.Log(_networkManager, "host");

            try
            {
                var options = new SessionOptions
                {
                    // Named after whoever started it, because the browser shows this and the SDK's
                    // default is a GUID — a list of those is a list of nothing.
                    Name = SessionNameFor(request.Nickname),
                    MaxPlayers = _maxPlayers,

                    // Public, so it appears in the browser. The join code still works and is still
                    // the way to reach a specific game; being listed is not a second kind of
                    // session, it is the same one with a door somebody can find. A game nobody can
                    // find is what the code is for, and adding that switch is a menu decision, not
                    // a connection one.
                    IsPrivate = false
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

        /// <summary>
        /// Asks the service what is open to anybody right now.
        /// </summary>
        /// <remarks>
        /// <para>Signs in first, like every other call here: a query is an authenticated request,
        /// and a player who opens the browser before ever hosting has not signed in yet.</para>
        /// <para>Full games are filtered out by the service rather than by this method. Asking for
        /// only what has room is one fewer round trip's worth of rows over the wire, and it is the
        /// question the player is actually asking.</para>
        /// <para>Nothing polls. <c>QuerySessionsResults</c> offers <c>StartPolling</c>, and taking
        /// it would mean a background timer writing into a list a screen may no longer be showing,
        /// against a service that rate-limits. A Refresh button says what it costs.</para>
        /// </remarks>
        public async Task<BrowseResult> BrowseAsync(CancellationToken cancellationToken = default)
        {
            ConnectionResult ready = await PrepareAsync(cancellationToken);
            if (!ready.Success) return BrowseResult.Failed(ready.Failure, ready.Diagnostic);

            try
            {
                var options = new QuerySessionsOptions
                {
                    Count = MaxListedSessions,
                    FilterOptions = new List<FilterOption>
                    {
                        new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater)
                    }
                };

                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(options);
                cancellationToken.ThrowIfCancellationRequested();

                var listings = new List<SessionListing>(results.Sessions.Count);

                for (int i = 0; i < results.Sessions.Count; i++)
                {
                    ISessionInfo session = results.Sessions[i];
                    if (session.IsLocked || session.HasPassword) continue;

                    listings.Add(new SessionListing(
                        session.Id,
                        session.Name,
                        session.MaxPlayers - session.AvailableSlots,
                        session.MaxPlayers));
                }

                return BrowseResult.Ok(listings);
            }
            catch (OperationCanceledException)
            {
                return BrowseResult.Failed(ConnectionFailure.Cancelled);
            }
            catch (SessionException e)
            {
                return BrowseResult.Failed(Classify(e), e.Message);
            }
            catch (Exception e)
            {
                return BrowseResult.Failed(ConnectionFailure.Error, e.Message);
            }
        }

        /// <summary>How many games the browser will ask for at once.</summary>
        /// <remarks>
        /// The service defaults to a hundred and supports paging. A menu that needs a second page
        /// of a game this size is a problem worth having and not one worth building for now — the
        /// list is sorted by nothing in particular, so a longer one would not be more useful.
        /// </remarks>
        const int MaxListedSessions = 25;

        public async Task<ConnectionResult> JoinAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
        {
            string target = request.Target?.Trim();

            if (string.IsNullOrEmpty(target))
                return ConnectionResult.Failed(ConnectionFailure.NotFound, "no join target given");

            ConnectionResult ready = await PrepareAsync(cancellationToken);
            if (!ready.Success) return ready;

            // Must match the host, or the request message it sends and the one the server expects
            // to read are different shapes. See ConnectionApproval.EnableOnClient.
            ConnectionApproval.EnableOnClient(_networkManager);

            // The payload rides the handshake exactly as it does on a LAN. Relay changes how the
            // packets travel, not what approval gets to inspect.
            _networkManager.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = _gameVersion,
                Nickname = Truncate(request.Nickname, ConnectionApproval.MaxNicknameLength),
                CharacterIndex = request.CharacterIndex
            }.ToBytes();

            NetworkConfigReport.Log(_networkManager, "client");

            try
            {
                // A code is uppercased because a player typed it; an id is not, because nobody did.
                _session = request.TargetKind == JoinTargetKind.Listing
                    ? await MultiplayerService.Instance.JoinSessionByIdAsync(target)
                    : await MultiplayerService.Instance.JoinSessionByCodeAsync(target.ToUpperInvariant());

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
                {
                    AuthenticationService.Instance.SwitchProfile(LocalProfileName());
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

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

        /// <summary>
        /// An authentication profile unique to this running instance.
        /// </summary>
        /// <remarks>
        /// <para>Anonymous sign-in caches one identity per <i>profile</i>, and the default profile
        /// is shared. Two peers on one machine — which is every Multiplayer Play Mode session, and
        /// the only way this project is developed — therefore signed in as the same player, and
        /// Sessions refuses to admit a player who is already in the session. The failure surfaced
        /// as "Unexpected exception processing network metadata", which says nothing about identity
        /// at all.</para>
        /// <para>Derived from <c>Application.dataPath</c> because a virtual player runs out of its
        /// own cloned project folder, so the path differs per instance and stays the same across
        /// restarts — a random profile would work too, but would make every run a brand new player
        /// and throw away the cached credentials each time.</para>
        /// </remarks>
        static string LocalProfileName()
        {
            int hash = Application.dataPath.GetHashCode() & 0x7FFFFFFF;
            return "peer_" + hash.ToString("x8");
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

        /// <summary>What the browser will show for a game this player is hosting.</summary>
        /// <remarks>
        /// The nickname is the only thing anybody has typed by this point, and it is already capped
        /// and trimmed by <see cref="ConnectionApproval"/> on the way in. A blank one still has to
        /// produce something readable, because the name is what a stranger clicks.
        /// </remarks>
        static string SessionNameFor(string nickname)
        {
            string trimmed = nickname?.Trim();
            return string.IsNullOrEmpty(trimmed) ? "A Snackdown game" : $"{trimmed}'s game";
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
