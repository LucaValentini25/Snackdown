// Editor-only for the same reason as PlayerSessionTests: these load the real prefabs out of the
// asset database, and a player build has none. See the note at the top of that file for why the
// assembly itself stays a Play mode one.
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
    /// Removing a player from the session is the host's to do, and the server is where that is
    /// enforced rather than where the button happens to be drawn.
    /// </summary>
    /// <remarks>
    /// The lobby only offers the button to the host, which is worth nothing on its own — a client
    /// that sends the Rpc anyway is a modified client, and it is the only kind that would. So the
    /// test that matters is the one where a client asks and the server declines, and it has to be
    /// asked over a real connection because the refusal is a comparison against an id NGO writes on
    /// arrival, not against anything visible on the sending side.
    /// </remarks>
    public class KickTests : NetworkedFixture
    {
        private const string GameVersion = "test";

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
                _approval.SetLocalPlayer("Host", characterIndex: 0);
                _approval.Enable();
                return;
            }

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = $"Guest {Clients.Count + 1}",
                CharacterIndex = 0
            }.ToBytes();
        }

        protected override void OnHostStarted()
        {
            Object.Instantiate(_simulationPrefab).GetComponent<NetworkObject>().Spawn();
        }

        [UnityTearDown]
        public IEnumerator DisableApproval()
        {
            _approval?.Disable();
            _approval = null;
            yield break;
        }

        [UnityTest]
        public IEnumerator ANonHost_AskingForAKick_IsIgnoredByTheServer()
        {
            yield return StartSession(clientCount: 2);

            ulong victim = Clients[1].LocalClientId;

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 3,
                "the server to see all three players");

            // A client asking the server to remove somebody else. The lobby never offers this, so
            // the only way to reach it is the way a modified client would.
            RosterOn(Clients[0]).RequestKick(victim);

            // An absence needs a window long enough for the request to have landed if it had been
            // honoured. A loopback round trip is a handful of frames.
            for (int frame = 0; frame < 20; frame++) yield return null;

            Assert.IsTrue(Clients[1].IsConnectedClient, "A client kicked another client.");
            Assert.AreEqual(3, RosterOn(Host).Count, "the server's roster");
        }

        [UnityTest]
        public IEnumerator TheHost_KicksAClient_AndTellsItWhy()
        {
            yield return StartSession(clientCount: 1);

            ulong victim = Clients[0].LocalClientId;

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            RosterOn(Host).RequestKick(victim);

            yield return WaitFor(
                () => !Clients[0].IsConnectedClient,
                "the kicked client to be disconnected");

            // Being dropped and being removed look identical from the client's side without this,
            // and a player who cannot tell them apart blames the network.
            Assert.AreEqual(SessionRoster.KickReason, Clients[0].DisconnectReason,
                "The kicked client was not told why.");

            // Nothing removes the player from the roster by hand: NGO despawns what the departing
            // client owned and the roster is rebuilt from what is spawned.
            yield return WaitFor(
                () => RosterOn(Host).Count == 1,
                "the server's roster to drop the player who was removed");
        }

        [UnityTest]
        public IEnumerator TheHost_CannotKickItself()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            // Not a guard against the host misclicking — the lobby never draws the button on its own
            // row. It is a guard against the request arriving anyway, where honouring it would shut
            // the whole session down and read to everyone else as the host crashing.
            RosterOn(Host).RequestKick(NetworkManager.ServerClientId);

            for (int frame = 0; frame < 20; frame++) yield return null;

            Assert.IsTrue(Host.IsListening, "The host shut its own session down.");
            Assert.AreEqual(2, RosterOn(Host).Count, "the server's roster");
        }

        /// <summary>
        /// The roster belonging to one peer, out of that peer's own spawned objects.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>SessionRoster.Current</c>: this process runs a roster per peer and
        /// that static answers with whichever spawned last. Asking a client's roster to send the
        /// Rpc is the whole point of the first test — it has to be that peer's object.
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

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing. The test cannot check a prefab that is not there.");

            return prefab;
        }
    }
}

#endif
