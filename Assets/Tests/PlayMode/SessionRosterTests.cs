// Editor-only for the same reason as PlayerSessionTests: these load the real prefabs out of the
// asset database, and a player build has none. See the note at the top of that file for why the
// assembly itself stays a Play mode one.
#if UNITY_EDITOR

using System.Collections;
using System.Text;
using NUnit.Framework;
using Snackdown.Connection;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Snackdown.Tests
{
    /// <summary>
    /// The roster shows every player in the session — including, to someone who arrives late, the
    /// ones who were already there.
    /// </summary>
    /// <remarks>
    /// This is the property the roster used to get from replicating a <c>NetworkList</c>, which NGO
    /// sends in full on spawn. It now gets it from the session objects being spawned
    /// <see cref="NetworkObject"/>s, which NGO synchronizes on join for the same reason — and
    /// "still true after the mechanism changed" is exactly the kind of claim that is worth a test
    /// rather than an argument.
    /// </remarks>
    public class SessionRosterTests : NetworkedFixture
    {
        private const string GameVersion = "test";
        private const string HostNickname = "Luca";

        /// <summary>Names and skins per joining client, in join order.</summary>
        /// <remarks>
        /// Distinct on both axes so that a roster which found the right number of players but paired
        /// them with the wrong details still fails. Three, because approval admits four and the host
        /// is one of them.
        /// </remarks>
        private static readonly string[] ClientNicknames = { "Ana", "Beto", "Caro" };
        private static readonly int[] ClientCharacters = { 1, 2, 3 };

        private GameObject _avatarPrefab;
        private GameObject _sessionPrefab;
        private GameObject _simulationPrefab;

        private ConnectionApproval _approval;

        [SetUp]
        public void LoadPrefabs()
        {
            _avatarPrefab = LoadPrefab("Assets/_Project/Prefabs/Player.prefab");
            _sessionPrefab = LoadPrefab("Assets/_Project/Prefabs/PlayerSession.prefab");
            _simulationPrefab = LoadPrefab("Assets/_Project/Prefabs/NetworkSimulation.prefab");
        }

        protected override void Configure(NetworkManager peer, bool isHost)
        {
            // The player object is the session, not the character — see ADR D-004. The character is
            // an ordinary prefab the session spawns per round, so it has to be registered like one.
            peer.NetworkConfig.PlayerPrefab = _sessionPrefab;
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _avatarPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _simulationPrefab });

            if (isHost)
            {
                _approval = new ConnectionApproval(peer, GameVersion, maxPlayers: 4);
                _approval.SetLocalPlayer(HostNickname, characterIndex: 0);
                _approval.Enable();
                return;
            }

            // Configure runs before the peer is added to Clients, so the count is this client's
            // index — which is what gives each of them a name and a skin of its own.
            int index = Clients.Count;

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = ClientNicknames[index],
                CharacterIndex = ClientCharacters[index]
            }.ToBytes();
        }

        protected override void OnHostStarted()
        {
            // The roster is a scene object in a real session, standing before anyone arrives.
            Object.Instantiate(_simulationPrefab).GetComponent<NetworkObject>().Spawn();
        }

        [UnityTearDown]
        public IEnumerator DisableApproval()
        {
            // ConnectionApproval.Current is a single static shared by every peer in this process.
            _approval?.Disable();
            _approval = null;
            yield break;
        }

        [UnityTest]
        public IEnumerator AThirdClient_SeesThePlayersAlreadyPresent()
        {
            yield return StartSession(clientCount: 2);

            ulong first = Clients[0].LocalClientId;
            ulong second = Clients[1].LocalClientId;

            yield return ReadyUp(Clients[0]);

            yield return JoinAnotherClient();

            NetworkManager late = Clients[2];

            // Four, not three: the host is playing too, and a roster that quietly left it out would
            // start a match one player short of the one on screen.
            yield return WaitFor(
                () => CountOn(late) == 4,
                "the late joiner to see all four players");

            Assert.AreEqual(HostNickname, NicknameOn(late, NetworkManager.ServerClientId), "the host's name");

            yield return WaitFor(
                () => NicknameOn(late, first) == ClientNicknames[0]
                      && NicknameOn(late, second) == ClientNicknames[1],
                "the names of the two players who were already there");

            Assert.AreEqual(ClientCharacters[0], SessionOn(late, first).CharacterIndex, "first player's skin");
            Assert.AreEqual(ClientCharacters[1], SessionOn(late, second).CharacterIndex, "second player's skin");

            // The state the old NetworkList carried in its initial send. A late joiner that saw the
            // right people with everyone's ready flag cleared would let the host press Start on a
            // lobby that had already agreed to begin, or block one that had.
            yield return WaitFor(
                () => SessionOn(late, first).IsReady,
                "the ready flag of the player who readied up before the join");

            Assert.IsFalse(SessionOn(late, second).IsReady, "the player who never readied up");
        }

        [UnityTest]
        public IEnumerator EveryPeer_OrdersTheRosterTheSameWay()
        {
            yield return StartSession(clientCount: 2);
            yield return JoinAnotherClient();

            foreach (NetworkManager peer in Clients)
            {
                NetworkManager waited = peer;
                yield return WaitFor(() => CountOn(waited) == 4, $"{waited.name} to see four players");
            }

            // Spawn order is not something a peer can rely on matching anyone else's — a late joiner
            // receives the sessions in whatever order synchronization hands them over. The lobby
            // rows have to line up anyway, so the roster sorts, and this is that promise.
            string onHost = OrderOn(Host);

            foreach (NetworkManager client in Clients)
            {
                Assert.AreEqual(onHost, OrderOn(client), $"{client.name} disagrees about the order");
            }
        }

        [UnityTest]
        public IEnumerator AClient_CannotReadyUpSomebodyElse()
        {
            yield return StartSession(clientCount: 2);

            ulong first = Clients[0].LocalClientId;

            yield return WaitFor(() => SessionOn(Clients[1], first) != null,
                "the second client to see the first client's session");

            // The lobby only ever calls this on the local player's own session. Asking it of someone
            // else's copy is what a modified client would do, and the object it is asking is not the
            // one the server takes orders from.
            SessionOn(Clients[1], first).ToggleReady();

            // Nothing to wait for, which is the difficulty: proving an absence needs a window long
            // enough for the request to have arrived if one had been sent. A loopback round trip is
            // a handful of frames, so ten is generous and still instant.
            for (int frame = 0; frame < 10; frame++) yield return null;

            Assert.IsFalse(SessionOn(Host, first).IsReady,
                "A client readied up a player it does not own.");
        }

        /// <summary>Toggles a client's own ready flag and waits until the server has applied it.</summary>
        private IEnumerator ReadyUp(NetworkManager client)
        {
            ulong clientId = client.LocalClientId;

            yield return WaitFor(() => SessionOn(client, clientId) != null,
                $"{client.name} to receive its own session before readying up");

            SessionOn(client, clientId).ToggleReady();

            yield return WaitFor(() => SessionOn(Host, clientId).IsReady,
                $"the server to accept the ready request from {client.name}");
        }

        /// <summary>
        /// The roster belonging to one peer, out of that peer's own spawned objects.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>SessionRoster.Current</c>. That static answers with whichever roster
        /// spawned last, and this process is running four of them — one per peer. Asking a
        /// <c>SpawnManager</c> is asking a specific side of the wire, which is the whole point of
        /// every assertion in this file.
        /// </remarks>
        private static SessionRoster RosterOn(NetworkManager peer)
        {
            foreach (NetworkObject spawned in peer.SpawnManager.SpawnedObjectsList)
            {
                var roster = spawned.GetComponent<SessionRoster>();
                if (roster != null) return roster;
            }

            return null;
        }

        private static int CountOn(NetworkManager peer)
        {
            SessionRoster roster = RosterOn(peer);
            return roster == null ? 0 : roster.Count;
        }

        private static PlayerSession SessionOn(NetworkManager peer, ulong ownerClientId)
        {
            SessionRoster roster = RosterOn(peer);
            return roster == null ? null : roster.Of(ownerClientId);
        }

        private static string NicknameOn(NetworkManager peer, ulong ownerClientId)
            => SessionOn(peer, ownerClientId)?.Nickname;

        /// <summary>The owner ids in roster order, as a string so a mismatch reads in the failure.</summary>
        private static string OrderOn(NetworkManager peer)
        {
            SessionRoster roster = RosterOn(peer);
            if (roster == null) return "<no roster>";

            var order = new StringBuilder();
            for (int i = 0; i < roster.Count; i++)
            {
                if (i > 0) order.Append(", ");
                order.Append(roster[i].OwnerClientId);
            }

            return order.ToString();
        }

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing. The test cannot check a prefab that is not there.");

            return prefab;
        }
    }
}

#endif
