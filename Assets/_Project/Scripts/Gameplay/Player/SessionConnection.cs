using Snackdown.Connection;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Owns the connection for as long as the application runs: the provider, the approval it vets
    /// with, and what other players need in order to reach this session.
    /// </summary>
    /// <remarks>
    /// <para>It lives in the bootstrap scene, which never unloads. That is the whole point. The
    /// lobby scene is taken down when a match starts and built again when one ends, so anything the
    /// menu held privately died with it — and the menu held all of this. Pressing <i>Return to
    /// lobby</i> therefore came back to the host-or-join screen with the session still running
    /// underneath it, the join code gone, and a second <see cref="ConnectionApproval"/> about to be
    /// constructed beside the one that was still vetting arrivals.</para>
    /// <para>Nothing here draws anything, and nothing here is networked. It is the answer to "are we
    /// in a session, whose is it, and how does someone else get in" — three questions the menu used
    /// to answer from its own fields and now asks.</para>
    /// <para>It composes the connection rather than belonging to it, which is why it sits in the
    /// gameplay assembly and not in <c>Snackdown.Connection</c> beside the pieces it builds. It has
    /// to name <see cref="CharacterCatalog"/> to tell approval how many skins there are, and the
    /// catalog is content — it lives here, in the assembly that already depends on the connection
    /// layer. The same move D-014 made for the roster, for the same reason.</para>
    /// <para>The provider is built once, on first use rather than on <c>Awake</c>: it needs
    /// <c>NetworkManager.Singleton</c>, which is assigned in that component's own <c>Awake</c> with
    /// nothing ordering the two.</para>
    /// </remarks>
    public class SessionConnection : MonoBehaviour
    {
        [Tooltip("Port used for direct connections.")]
        [SerializeField] private ushort _port = 7777;

        [Tooltip("Players allowed in one session. Enforced by connection approval.")]
        [SerializeField] private int _maxPlayers = 4;

        [Tooltip("Start on Relay (join by code) instead of direct LAN (join by address).")]
        [SerializeField] private bool _useRelay = true;

        [Tooltip("Skins players can wear. Its length is what approval spreads arrivals across.")]
        [SerializeField] private CharacterCatalog _skins;

        private IConnectionProvider _provider;
        private ConnectionApproval _approval;

        /// <summary>The connection for this application, if the bootstrap scene is loaded.</summary>
        /// <remarks>
        /// The same ambient-static pattern the rest of the project uses, and here for the reason
        /// that pattern exists: the menu is in another scene that comes and goes, so a serialized
        /// reference would be null exactly as often as it would be useful.
        /// </remarks>
        public static SessionConnection Current { get; private set; }

        /// <summary>How this session is joined, or null before a <c>NetworkManager</c> exists.</summary>
        public IConnectionProvider Provider
        {
            get
            {
                if (_provider == null && NetworkManager.Singleton != null)
                {
                    _approval = new ConnectionApproval(
                        NetworkManager.Singleton, GameVersion, _maxPlayers);

                    // Told once, here, because this is the only place that can see both. Approval
                    // decides which skin an arrival gets and cannot name the catalog: the catalog is
                    // content and lives in an assembly that depends on the connection layer, not the
                    // other way round. Left unset it keeps a default that a fifth authored skin
                    // would be silently clamped away by.
                    if (_skins != null) _approval.CharacterCount = _skins.Count;

                    // The one line that knows there is more than one way to connect. Everything
                    // past this point talks to the interface and cannot tell them apart.
                    _provider = _useRelay
                        ? new RelayConnectionProvider(
                            NetworkManager.Singleton, _approval, GameVersion, _maxPlayers)
                        : new DirectConnectionProvider(
                            NetworkManager.Singleton, _port, _approval, GameVersion);
                }

                return _provider;
            }
        }

        /// <summary>Players this session admits.</summary>
        public int MaxPlayers => _maxPlayers;

        /// <summary>Skins a player can be given, straight from the catalog.</summary>
        public int SkinCount => _skins != null ? _skins.Count : 0;

        /// <summary>The skins themselves, for a screen that has to draw them.</summary>
        /// <remarks>
        /// Handed out rather than looked up again by whoever needs it. One reference to the catalog
        /// is one thing to point at a different asset by mistake; the lobby drawing a wardrobe from
        /// a catalog the server never saw would show costumes nobody can be given.
        /// </remarks>
        public CharacterCatalog Skins => _skins;

        /// <summary>
        /// What another player needs in order to reach this session — an address or a share code.
        /// Empty when not hosting.
        /// </summary>
        /// <remarks>
        /// Remembered here rather than by whoever displayed it. The lobby is where it is shown and
        /// the lobby is the thing that keeps being rebuilt, so a value kept there is a value that
        /// survives exactly one match.
        /// </remarks>
        public string JoinTarget { get; private set; } = string.Empty;

        /// <summary>True once this machine is hosting or has joined.</summary>
        public bool InSession
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening;
            }
        }

        /// <summary>True when this machine is the one running the session.</summary>
        public bool IsHosting
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening && manager.IsServer;
            }
        }

        /// <summary>Records what a successful attempt produced, so the lobby can show it again.</summary>
        public void Remember(ConnectionResult result) => JoinTarget = result.JoinTarget ?? string.Empty;

        /// <summary>Forgets the session that has ended.</summary>
        public void Forget() => JoinTarget = string.Empty;

        /// <remarks>
        /// The version every peer is checked against. Taken from the player settings rather than
        /// written down, so a build cannot disagree with itself about what it is.
        /// </remarks>
        private static string GameVersion => Application.version;

        private void Awake() => Current = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
