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
    /// Everybody in a session is called something different, whatever they asked for.
    /// </summary>
    /// <remarks>
    /// <para>Two players with the same name is not a cosmetic problem. The roster, the life bars,
    /// the nameplates and the end screen all identify a player to a human by their name — so a match
    /// that ends with the scoreboard naming a winner both of them answer to is worse than one with
    /// no names at all.</para>
    /// <para>Over a real connection because the numbering happens at admission and reads the set of
    /// names already admitted, which only exists on a server that has admitted somebody. The
    /// sanitising underneath it is covered in Edit mode, where it belongs — it is a pure function.
    /// </para>
    /// </remarks>
    public class NicknameTests : NetworkedFixture
    {
        private const string GameVersion = "test";

        /// <summary>The name everybody asks for. Short, so the cap is not what is being tested.</summary>
        private const string Contested = "Ana";

        /// <summary>Exactly the cap, for the test that is about the cap.</summary>
        private static readonly string FullLength = new string('a', ConnectionApproval.MaxNicknameLength);

        /// <summary>
        /// What every peer in the next session asks to be called. Reset per test.
        /// </summary>
        /// <remarks>
        /// NUnit builds one instance of a fixture and runs every test in it, so a field a test
        /// writes is a field the next test inherits — the cap test set this to a sixteen-character
        /// name and three others then failed comparing against "Ana". Reset in SetUp rather than
        /// trusted to be left alone.
        /// </remarks>
        private string _wanted;

        private GameObject _avatarPrefab;
        private GameObject _sessionPrefab;
        private GameObject _simulationPrefab;

        private ConnectionApproval _approval;

        [SetUp]
        public void LoadPrefabs()
        {
            _wanted = Contested;

            _avatarPrefab = LoadPrefab("Assets/_Project/Prefabs/Player.prefab");
            _sessionPrefab = LoadPrefab("Assets/_Project/Prefabs/PlayerSession.prefab");
            _simulationPrefab = LoadPrefab("Assets/_Project/Prefabs/NetworkSimulation.prefab");
        }

        protected override void Configure(NetworkManager peer, bool isHost)
        {
            peer.NetworkConfig.PlayerPrefab = _sessionPrefab;
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _avatarPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _simulationPrefab });

            if (isHost)
            {
                _approval = new ConnectionApproval(peer, GameVersion, maxPlayers: 4);

                // The host asks for the contested name too. Whoever is first keeps it plain, and
                // the host being first is not a special case — it is just who arrived first.
                _approval.SetLocalPlayer(_wanted, characterIndex: 0);
                _approval.Enable();
                return;
            }

            ConnectionApproval.EnableOnClient(peer);
            peer.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                GameVersion = GameVersion,
                Nickname = _wanted,
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
        public IEnumerator ThreePlayers_AskingForOneName_AreToldApart()
        {
            yield return StartSession(clientCount: 2);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 3,
                "the server to see all three players");

            Assert.AreEqual(Contested, NameOf(NetworkManager.ServerClientId),
                "The player who was already using the name lost it to somebody who arrived later.");

            Assert.AreEqual($"{Contested} (2)", NameOf(Clients[0].LocalClientId), "the second arrival");
            Assert.AreEqual($"{Contested} (3)", NameOf(Clients[1].LocalClientId), "the third arrival");
        }

        [UnityTest]
        public IEnumerator EveryPeer_SeesTheSameNames()
        {
            yield return StartSession(clientCount: 2);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 3
                      && RosterOn(Clients[1]) != null && RosterOn(Clients[1]).Count == 3,
                "both peers to see all three players");

            ulong second = Clients[0].LocalClientId;

            // Numbering that only the server knew about would leave every other screen showing two
            // players called the same thing, which is the bug with an extra step.
            yield return WaitFor(
                () => RosterOn(Clients[1]).Of(second) != null
                      && RosterOn(Clients[1]).Of(second).Nickname == $"{Contested} (2)",
                "the third player to see the second one numbered");
        }

        [UnityTest]
        public IEnumerator ANameFreedByLeaving_GoesBackToTheNextArrival()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            Assert.AreEqual($"{Contested} (2)", NameOf(Clients[0].LocalClientId), "the second arrival");

            Clients[0].Shutdown();

            yield return WaitFor(
                () => RosterOn(Host).Count == 1,
                "the server to drop the player who left");

            yield return JoinAnotherClient();

            // Approval forgets a name on disconnect, so the numbering describes who is here rather
            // than tallying everyone who ever was. Without that a session would count upwards
            // forever and the fourth "Ana" of the evening would be Ana (4) beside nobody.
            yield return WaitFor(
                () => NameOf(Clients[1].LocalClientId) == $"{Contested} (2)",
                "the arrival to be given the number the player who left had freed");
        }

        [UnityTest]
        public IEnumerator ANameAtTheCap_StaysInsideItOnceNumbered()
        {
            _wanted = FullLength;

            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => RosterOn(Host) != null && RosterOn(Host).Count == 2,
                "the server to see both players");

            string numbered = NameOf(Clients[0].LocalClientId);

            // The name gives up its last letters, not the number. Running over would hand the rest
            // of the project a string longer than MaxNicknameLength promises, and the session
            // carries it in a FixedString32Bytes.
            Assert.LessOrEqual(numbered.Length, ConnectionApproval.MaxNicknameLength,
                $"\"{numbered}\" is longer than a nickname is allowed to be.");

            Assert.IsTrue(numbered.EndsWith(" (2)"),
                $"\"{numbered}\" was cut without keeping what tells it apart.");
        }

        /// <summary>The name the server admitted a client under, or null before it has one.</summary>
        private string NameOf(ulong clientId)
        {
            SessionRoster roster = RosterOn(Host);
            PlayerSession session = roster != null ? roster.Of(clientId) : null;

            return session != null ? session.Nickname : null;
        }

        /// <remarks>
        /// Read off the peer's own <c>SpawnManager</c> rather than <c>SessionRoster.Current</c>:
        /// this process runs a roster per peer and that static answers with whichever spawned last.
        /// </remarks>
        private static SessionRoster RosterOn(NetworkManager peer)
        {
            if (peer == null) return null;

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
