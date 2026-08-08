using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Connection
{
    /// <summary>
    /// The server's doorman: decides who gets in, under what name, and refuses everyone else.
    /// </summary>
    /// <remarks>
    /// <para>This is the only place a connection can still be turned away cheaply. Once a client is
    /// admitted it has a player object, a place in the tick loop and bandwidth; rejecting it later
    /// means despawning and disconnecting something that already exists. So version checks, the
    /// player cap and name sanitation all happen here, before any of that is allocated.</para>
    /// <para>Implements rule 5 of <c>docs/01</c> — a connecting peer is hostile until checked. The
    /// payload is a byte array from the network: it may be absent, truncated, or carry a 10,000
    /// character name with control codes in it. Every field is validated or replaced, and nothing
    /// is echoed back to other clients unmodified.</para>
    /// </remarks>
    public class ConnectionApproval
    {
        /// <summary>Longest name that will be kept. Anything past this is cut, not rejected.</summary>
        /// <remarks>
        /// Truncating beats refusing: a name that is merely too long is not an attack, and kicking
        /// someone over it is a worse experience than shortening it. The cap exists so a name
        /// cannot become a payload — for the UI to overflow, or for another player's screen.
        /// </remarks>
        public const int MaxNicknameLength = 16;

        /// <summary>Used when a name is empty, blank, or nothing survives sanitation.</summary>
        public const string FallbackNicknameFormat = "Player {0}";

        readonly NetworkManager _networkManager;
        readonly string _gameVersion;
        readonly int _maxPlayers;

        /// <summary>Approved names by client id, so the rest of the game reads the checked value.</summary>
        readonly Dictionary<ulong, string> _approvedNicknames = new Dictionary<ulong, string>();

        /// <summary>Approved skin index by client id.</summary>
        readonly Dictionary<ulong, int> _approvedCharacters = new Dictionary<ulong, int>();

        bool _listening;

        string _localNickname = string.Empty;
        int _localCharacterIndex;

        /// <summary>
        /// Tells approval who the local player is, for the case where they are the host.
        /// </summary>
        /// <remarks>
        /// A host has no payload to read — its details never travel — so they have to be handed
        /// over directly. Sanitation still applies: the host is not hostile, but a name that is too
        /// long or blank breaks a lobby row just as thoroughly whoever typed it.
        /// </remarks>
        public void SetLocalPlayer(string nickname, int characterIndex)
        {
            _localNickname = nickname ?? string.Empty;
            _localCharacterIndex = characterIndex;
        }

        /// <summary>
        /// The approval currently vetting connections on this peer, or null when none is.
        /// </summary>
        /// <remarks>
        /// Scene objects like <see cref="SessionRoster"/> spawn long after whoever constructed this,
        /// and have no reference to hand. A static pointer is the smallest thing that closes that
        /// gap: it is set while vetting is active and cleared when it stops, so it cannot outlive
        /// the session it describes — unlike the <c>DontDestroyOnLoad</c> singletons in the original
        /// project, which survived scene loads and answered for sessions that no longer existed.
        /// </remarks>
        public static ConnectionApproval Current { get; private set; }

        /// <summary>How many character skins exist. Requests outside this range are clamped.</summary>
        public int CharacterCount { get; set; } = 4;

        public ConnectionApproval(NetworkManager networkManager, string gameVersion, int maxPlayers)
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));

            _gameVersion = gameVersion ?? string.Empty;
            _maxPlayers = Mathf.Max(1, maxPlayers);
        }

        /// <summary>Name this client was admitted under, or null if it was never approved.</summary>
        public string NicknameOf(ulong clientId)
            => _approvedNicknames.TryGetValue(clientId, out string nickname) ? nickname : null;

        /// <summary>Skin this client was admitted with.</summary>
        public int CharacterOf(ulong clientId)
            => _approvedCharacters.TryGetValue(clientId, out int index) ? index : 0;

        /// <summary>Starts vetting connections. Must be called before the server starts listening.</summary>
        public void Enable()
        {
            if (_listening) return;

            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.ConnectionApprovalCallback += Approve;
            _networkManager.OnClientDisconnectCallback += Forget;
            _listening = true;
            Current = this;
        }

        public void Disable()
        {
            if (!_listening) return;

            _networkManager.ConnectionApprovalCallback -= Approve;
            _networkManager.OnClientDisconnectCallback -= Forget;
            _approvedNicknames.Clear();
            _approvedCharacters.Clear();
            _listening = false;

            // Only clear the pointer if it still refers to us; a newer approval may already own it.
            if (ReferenceEquals(Current, this)) Current = null;
        }

        void Forget(ulong clientId)
        {
            _approvedNicknames.Remove(clientId);
            _approvedCharacters.Remove(clientId);
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // The host approves itself. Its details never crossed a wire, so there is nothing to
            // validate against a hostile sender — but they still have to be its own. Admitting it
            // as a fixed "Host" playing character 0 meant whoever started the game silently lost
            // the name and skin they had chosen.
            if (request.ClientNetworkId == _networkManager.LocalClientId)
            {
                Admit(response,
                    request.ClientNetworkId,
                    SanitizeNickname(_localNickname, request.ClientNetworkId),
                    Mathf.Clamp(_localCharacterIndex, 0, Mathf.Max(0, CharacterCount - 1)));
                return;
            }

            if (_networkManager.ConnectedClientsIds.Count >= _maxPlayers)
            {
                Refuse(response, "This game is full.");
                return;
            }

            if (!ConnectionPayload.TryRead(request.Payload, out ConnectionPayload payload))
            {
                Refuse(response, "Couldn't read your connection details.");
                return;
            }

            if (payload.GameVersion.ToString() != _gameVersion)
            {
                // Naming both versions is deliberate: "you have 0.2.0, this game is 0.3.0" tells the
                // player what to do, where "version mismatch" tells them only that they failed.
                Refuse(response, $"Different game version — yours is {Describe(payload.GameVersion.ToString())}, this game is {_gameVersion}.");
                return;
            }

            Admit(response,
                request.ClientNetworkId,
                SanitizeNickname(payload.Nickname.ToString(), request.ClientNetworkId),
                Mathf.Clamp(payload.CharacterIndex, 0, Mathf.Max(0, CharacterCount - 1)));
        }

        void Admit(NetworkManager.ConnectionApprovalResponse response, ulong clientId, string nickname, int characterIndex)
        {
            _approvedNicknames[clientId] = nickname;
            _approvedCharacters[clientId] = characterIndex;

            response.Approved = true;
            response.CreatePlayerObject = true;
        }

        static void Refuse(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;

            // Travels to the rejected client and is shown to them, so it is written for a player
            // rather than for a log.
            response.Reason = reason;
        }

        static string Describe(string version) => string.IsNullOrWhiteSpace(version) ? "unknown" : version;

        /// <summary>
        /// Turns a requested name into one that is safe to display.
        /// </summary>
        /// <remarks>
        /// Control characters go first — they can break layout and, in a chat or scoreboard, be used
        /// to impersonate. What remains is trimmed and capped. If nothing usable survives, the
        /// player gets a generated name instead of an empty one, because an unnamed player is a gap
        /// in every list that shows them.
        /// </remarks>
        public static string SanitizeNickname(string requested, ulong clientId)
        {
            if (string.IsNullOrWhiteSpace(requested)) return string.Format(FallbackNicknameFormat, clientId);

            var clean = new System.Text.StringBuilder(MaxNicknameLength);

            foreach (char c in requested)
            {
                if (char.IsControl(c)) continue;

                clean.Append(c);
                if (clean.Length >= MaxNicknameLength) break;
            }

            string result = clean.ToString().Trim();
            return result.Length > 0 ? result : string.Format(FallbackNicknameFormat, clientId);
        }
    }
}
