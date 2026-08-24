// Editor-only for the same reason as PlayerSessionTests: these load the real prefabs out of the
// asset database, and a player build has none. See the note at the top of that file for why the
// assembly itself stays a Play mode one.
#if UNITY_EDITOR

using System.Collections;
using NUnit.Framework;
using Snackdown.Connection;
using Snackdown.Gameplay.Fruits;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Snackdown.Tests
{
    /// <summary>
    /// Fruit is credited to the player, not to the body that walked into it — and the server is the
    /// only one who decides.
    /// </summary>
    /// <remarks>
    /// <para>Life and the fruit counter moved off the avatar and onto <see cref="PlayerSession"/> in
    /// ps-3, which means a pickup now crosses from one networked object to another: the fruit sees a
    /// character and has to end up writing to that character's owner's session. Every way of getting
    /// that wrong is silent — crediting nobody, crediting the host, or writing to a copy of the
    /// session that belongs to a different peer, which NGO refuses and nothing notices.</para>
    /// <para>Run against the shipped prefabs, so what is checked is the object that will actually be
    /// in the arena, wiring included.</para>
    /// </remarks>
    public class FruitPickupTests : NetworkedFixture
    {
        private const string GameVersion = "test";

        /// <summary>
        /// Which fruit to drop. Anything but the first entry, deliberately.
        /// </summary>
        /// <remarks>
        /// Index 0 is the <see cref="NetworkVariable{T}"/>'s default, so a fruit of the first kind
        /// would replicate its kind by not changing it — and a bug that dropped the kind entirely
        /// would pass. The seconds it is worth are read from the table rather than written here: a
        /// number copied out of a content asset is a number that goes stale the first time someone
        /// tunes it.
        /// </remarks>
        private const int FruitKind = 1;

        /// <summary>
        /// Where the pickup happens, away from the origin.
        /// </summary>
        /// <remarks>
        /// Both avatars spawn at the origin — NGO places a player object there and no arena has
        /// loaded to move them — so a fruit dropped there would be standing on two players at once
        /// and this test would be asserting whichever the overlap happened to return first.
        /// </remarks>
        private static readonly Vector2 PickupSpot = new Vector2(6f, 0f);

        /// <summary>Somewhere no avatar is, for the fruit that should not be collected.</summary>
        private static readonly Vector2 EmptySpot = new Vector2(-20f, 0f);

        private GameObject _avatarPrefab;
        private GameObject _sessionPrefab;
        private GameObject _simulationPrefab;
        private GameObject _fruitPrefab;
        private FruitTable _table;

        private ConnectionApproval _approval;

        [SetUp]
        public void LoadAssets()
        {
            _avatarPrefab = LoadPrefab("Assets/_Project/Prefabs/Player.prefab");
            _sessionPrefab = LoadPrefab("Assets/_Project/Prefabs/PlayerSession.prefab");
            _simulationPrefab = LoadPrefab("Assets/_Project/Prefabs/NetworkSimulation.prefab");
            _fruitPrefab = LoadPrefab("Assets/_Project/Prefabs/Fruit.prefab");

            _table = AssetDatabase.LoadAssetAtPath<FruitTable>("Assets/_Project/Settings/FruitTable.asset");
            Assert.IsNotNull(_table, "The fruit table is missing; there is nothing to say what a fruit is worth.");
            Assert.Greater(_table.Count, FruitKind, $"The fruit table has no entry {FruitKind}.");
            Assert.Greater(_table.Get(FruitKind).LifeSeconds, 0f,
                $"Entry {FruitKind} of the fruit table is worth no life, so this test could not tell a pickup from a no-op.");
        }

        protected override void Configure(NetworkManager peer, bool isHost)
        {
            peer.NetworkConfig.PlayerPrefab = _avatarPrefab;
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _sessionPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _simulationPrefab });
            peer.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _fruitPrefab });

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
                Nickname = "Guest",
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
        public IEnumerator AFruitTakenByTheClient_AddsLifeOnTheServerAndReachesBothPeers()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return WaitFor(
                () => LifeOn(Host, clientId) != null
                      && LifeOn(Clients[0], clientId) != null
                      && AvatarOn(Host, clientId) != null,
                "both peers to hold the client's life, and the server its avatar");

            float startedAt = LifeOn(Host, clientId).Remaining;
            float worth = _table.Get(FruitKind).LifeSeconds;

            // Parked away from the host, who is standing on the origin this fruit would otherwise
            // be dropped on.
            AvatarOn(Host, clientId).ServerTeleport(PickupSpot);
            SpawnFruit(PickupSpot);

            yield return WaitFor(
                () => FruitEatenOn(Host, clientId) == 1,
                "the server to bank the fruit against the client");

            Assert.AreEqual(startedAt + worth, LifeOn(Host, clientId).Remaining, 0.01f,
                "The server credited the fruit but not the life it is worth.");

            // The point of the test: the number the HUD draws is the replicated one, and both HUDs
            // read the same session. A pickup the server banked and nobody else saw would look, on
            // the client that made it, exactly like a fruit that failed to register.
            yield return WaitFor(
                () => FruitEatenOn(Clients[0], clientId) == 1
                      && LifeOn(Clients[0], clientId).Remaining >= startedAt + worth - 0.01f,
                "the client to see the life and the count it just gained");

            // Standing next to a fruit is not collecting it, and neither is being the server.
            Assert.AreEqual(0, FruitEatenOn(Host, NetworkManager.ServerClientId),
                "The host was credited for a fruit the client walked into.");
        }

        [UnityTest]
        public IEnumerator AFruitNobodyIsStandingOn_StaysWhereItIs()
        {
            yield return StartSession(clientCount: 1);

            ulong clientId = Clients[0].LocalClientId;

            yield return WaitFor(
                () => LifeOn(Host, clientId) != null && AvatarOn(Host, clientId) != null,
                "the server to hold the client's life and avatar");

            // The overlap query includes triggers and the fruit's own collider is inside its own
            // pickup radius. Before the session move, collection matched on any PlayerLife in the
            // parents and could not see the fruit; now it matches on a character and could, which
            // would hand every fruit in the arena to whoever owns it — the server — on the frame it
            // spawned.
            NetworkObject fruit = SpawnFruit(EmptySpot);

            for (int frame = 0; frame < 10; frame++) yield return null;

            Assert.IsTrue(fruit != null && fruit.IsSpawned, "A fruit nobody touched collected itself.");
            Assert.AreEqual(0, FruitEatenOn(Host, NetworkManager.ServerClientId), "the host's count");
            Assert.AreEqual(0, FruitEatenOn(Host, clientId), "the client's count");

            // Spawning a fruit is expected to be silent. It was not: the spawner used to write the
            // kind into its NetworkVariable before Spawn, which NGO answers with "NetworkVariable is
            // written to, but doesn't know its NetworkBehaviour yet" — a warning per fruit, forever,
            // in a game that spawns fruit on a timer. The test runner fails on unexpected errors but
            // not on warnings, so without this line that noise is nobody's regression.
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Drops one fruit of <see cref="FruitKind"/> on the server, the way the spawner does.</summary>
        private NetworkObject SpawnFruit(Vector2 position)
        {
            GameObject instance = Object.Instantiate(_fruitPrefab, position, Quaternion.identity);

            instance.GetComponent<Fruit>().ServerSetKind(FruitKind);

            NetworkObject spawned = instance.GetComponent<NetworkObject>();
            spawned.Spawn();

            return spawned;
        }

        /// <summary>
        /// A client's session as one peer holds it.
        /// </summary>
        /// <remarks>
        /// Read off that peer's <c>SpawnManager</c> rather than <see cref="PlayerSession.All"/>,
        /// which is a static holding both peers' copies at once inside this one process. Telling the
        /// two apart is the entire point of asserting on each side of the wire.
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

        private static PredictedPlayer AvatarOn(NetworkManager peer, ulong ownerClientId)
        {
            foreach (NetworkObject spawned in peer.SpawnManager.SpawnedObjectsList)
            {
                if (spawned.OwnerClientId != ownerClientId) continue;

                var avatar = spawned.GetComponent<PredictedPlayer>();
                if (avatar != null) return avatar;
            }

            return null;
        }

        private static PlayerLife LifeOn(NetworkManager peer, ulong ownerClientId)
            => SessionOn(peer, ownerClientId)?.Life;

        private static int FruitEatenOn(NetworkManager peer, ulong ownerClientId)
        {
            PlayerSession session = SessionOn(peer, ownerClientId);
            return session == null ? -1 : session.FruitEaten;
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
