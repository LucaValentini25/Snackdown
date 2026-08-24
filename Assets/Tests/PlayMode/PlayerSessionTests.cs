// Editor-only, because these load the real prefabs out of the asset database and a player build
// has no asset database to load them from. The assembly itself stays a Play mode one: Netcode for
// GameObjects does not initialise its message table outside Play mode, so an editor-only *assembly*
// would have its tests reclassified as Edit mode and every one of them would fail on the first
// message sent. This is the only combination that gets both — a real Play mode loop and the real
// prefabs.
#if UNITY_EDITOR

using System.Collections;
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
    /// A player joins and gets an identity of their own, separate from the character they are
    /// driving — checked over a real connection, on both sides of it.
    /// </summary>
    /// <remarks>
    /// Everything here runs against the prefabs the game actually ships, loaded from the asset
    /// database rather than assembled in the test. That is the difference between proving that
    /// <see cref="PlayerSession"/> behaves and proving that the object which will be spawned in a
    /// real session behaves — including that it is wired up, which is the half that breaks silently
    /// and is not noticed until someone tries to join.
    /// </remarks>
    public class PlayerSessionTests : NetworkedFixture
    {
        private const string GameVersion = "test";
        private const string HostNickname = "Luca";

        /// <summary>The name the client asks for, chosen so that sanitation has work to do.</summary>
        /// <remarks>
        /// Control characters and padding, because the assertion worth making is not that a name
        /// survives the trip — it is that the name the session carries is the *checked* one. A test
        /// that sent "Guest" would pass identically whether approval ran or was skipped entirely.
        /// </remarks>
        private const string ClientRequestedNickname = "  Gu\nest  ";
        private const string ClientAdmittedNickname = "Guest";

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
            // Both halves of the wire have to agree on the prefab list and on whether a connection
            // request carries a payload, or the request cannot even be deserialized.
            peer.NetworkConfig.PlayerPrefab = _avatarPrefab;
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _sessionPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _simulationPrefab });

            if (isHost)
            {
                _approval = new ConnectionApproval(peer, GameVersion, maxPlayers: 4);
                _approval.SetLocalPlayer(HostNickname, characterIndex: 0);
                _approval.Enable();
                return;
            }

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = ClientRequestedNickname,
                CharacterIndex = 0
            }.ToBytes();
        }

        protected override void OnHostStarted()
        {
            // The roster is a scene object in a real session, so it is standing before anyone
            // arrives. Spawning it here rather than after the clients connect is what makes this a
            // test of joining rather than a test of what happens when the handler shows up late.
            Object.Instantiate(_simulationPrefab).GetComponent<NetworkObject>().Spawn();
        }

        [UnityTearDown]
        public IEnumerator DisableApproval()
        {
            // ConnectionApproval.Current is a single static shared by every peer in this process.
            // Left set, it would answer for a session that no longer exists and hand the next test
            // the names admitted by this one.
            _approval?.Disable();
            _approval = null;
            yield break;
        }

        [UnityTest]
        public IEnumerator AJoiningClient_GetsASessionOfItsOwn()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return WaitFor(
                () => SessionOn(Host, clientId) != null,
                "the server to spawn a session for the client");

            Assert.AreEqual(clientId, SessionOn(Host, clientId).OwnerClientId,
                "The session was spawned but does not belong to the client that joined.");
        }

        [UnityTest]
        public IEnumerator BothPeers_SeeTheSanitizedNickname()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return WaitFor(
                () => NicknameOn(Host, clientId) == ClientAdmittedNickname
                      && NicknameOn(Clients[0], clientId) == ClientAdmittedNickname,
                $"both peers to carry the admitted name \"{ClientAdmittedNickname}\"");

            // Asserted rather than left to the wait so a timeout says which side disagreed.
            Assert.AreEqual(ClientAdmittedNickname, NicknameOn(Host, clientId), "server's copy");
            Assert.AreEqual(ClientAdmittedNickname, NicknameOn(Clients[0], clientId), "client's copy");
        }

        [UnityTest]
        public IEnumerator TheHostsOwnSession_CarriesTheNameItChose()
        {
            yield return StartSession(clientCount: 1);

            // The host has no payload — its details never cross a wire — so this is the path where
            // the name is handed to approval directly rather than read off the network.
            yield return WaitFor(
                () => NicknameOn(Host, NetworkManager.ServerClientId) == HostNickname,
                "the host's own session to carry the name it started with");
        }

        [UnityTest]
        public IEnumerator ALeavingClient_TakesItsSessionWithIt()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return WaitFor(
                () => SessionOn(Host, clientId) != null,
                "the client's session to exist before it disconnects");

            Clients[0].Shutdown();

            // A session lasts exactly as long as the connection. If it outlived one, a player who
            // left would keep a place in the round and the match could never end.
            yield return WaitFor(
                () => SessionOn(Host, clientId) == null,
                "the server to drop the session of the client that left");
        }

        /// <summary>
        /// Finds a client's session in one peer's own spawned objects.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="PlayerSession.All"/>. That registry is a static, and with two
        /// peers running inside a single Play mode session it holds the sessions of both at once —
        /// correct in a shipped game, where a process is one peer, and useless for telling apart
        /// what each side of this test can see. <c>SpawnManager</c> belongs to a NetworkManager, so
        /// asking it is asking that peer.
        /// </remarks>
        private static PlayerSession SessionOn(NetworkManager peer, ulong ownerClientId)
        {
            foreach (NetworkObject spawned in peer.SpawnManager.SpawnedObjectsList)
            {
                if (spawned.OwnerClientId != ownerClientId) continue;

                var session = spawned.GetComponent<PlayerSession>();
                if (session != null) return session;
            }

            return null;
        }

        private static string NicknameOn(NetworkManager peer, ulong ownerClientId)
            => SessionOn(peer, ownerClientId)?.Nickname;

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing. The test cannot check a prefab that is not there.");

            return prefab;
        }
    }
}

#endif
