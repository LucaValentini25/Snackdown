// Editor-only for the same reason as PlayerSessionTests: these load the real prefabs out of the
// asset database, and a player build has none. See the note at the top of that file for why the
// assembly itself stays a Play mode one.
#if UNITY_EDITOR

using System.Collections;
using NUnit.Framework;
using Snackdown.Connection;
using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Snackdown.Tests
{
    /// <summary>
    /// The numbers a match runs on are the host's to set and everybody's to read.
    /// </summary>
    /// <remarks>
    /// <para>They used to be a <see cref="MatchConfig"/> asset each peer read off its own disk,
    /// which works only while nobody changes them. Two of them are read by clients rather than
    /// merely applied by the server — the drain a client counts down with and the ceiling every life
    /// bar is a fraction of — so a host lowering either on a session where clients kept the old
    /// value would have every other screen disagreeing about numbers that decide the match.</para>
    /// <para>What is not reachable here is the round actually starting under them: that needs a
    /// phase past Lobby and the harness cannot get there — see D-019. So these assert on the value
    /// crossing the wire and on the server refusing what it should, which is where the netcode
    /// lives; the round reading it is one field access away and covered in the sandbox.</para>
    /// </remarks>
    public class MatchSettingsTests : NetworkedFixture
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
        public IEnumerator TheHost_LowersTheStartingLife_AndTheClientSeesIt()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => DirectorOn(Clients[0]) != null,
                "the client to receive the match director");

            // The number the board's test names. It is the server's to apply, but the client holds
            // it too, because a player readying up should be able to read what they are agreeing to.
            MatchSettings wanted = DirectorOn(Host).Rules;
            wanted.StartingLife = 30f;

            DirectorOn(Host).RequestSettings(wanted);

            yield return WaitFor(
                () => Mathf.Approximately(DirectorOn(Clients[0]).Rules.StartingLife, 30f),
                "the client to see the lowered starting life");

            Assert.AreEqual(30f, DirectorOn(Host).Rules.StartingLife, 0.01f, "the server's copy");

            // The one a round actually begins on. Nothing here starts a round — that needs a phase
            // the harness cannot reach — so this checks the value the reset would read.
            yield return WaitFor(
                () => SessionOn(Host, Clients[0].LocalClientId) != null,
                "the client's session");

            SessionOn(Host, Clients[0].LocalClientId).Life.ServerReset();

            Assert.AreEqual(30f, SessionOn(Host, Clients[0].LocalClientId).Life.Remaining, 0.01f,
                "A round reset did not use the settings the host had changed.");
        }

        [UnityTest]
        public IEnumerator AClient_CannotChangeTheRules()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => DirectorOn(Clients[0]) != null,
                "the client to receive the match director");

            float before = DirectorOn(Host).Rules.DrainPerSecond;

            MatchSettings cheated = DirectorOn(Clients[0]).Rules;
            cheated.DrainPerSecond = MatchSettings.MinDrain;

            // The lobby greys these fields for a client. That is a control, not a rule, and this is
            // the request a modified client would send anyway.
            DirectorOn(Clients[0]).RequestSettings(cheated);
            DirectorOn(Clients[0]).RequestPreset(0);

            for (int frame = 0; frame < 20; frame++) yield return null;

            Assert.AreEqual(before, DirectorOn(Host).Rules.DrainPerSecond, 0.001f,
                "A client changed the rules everyone plays by.");
        }

        [UnityTest]
        public IEnumerator NumbersOutOfRange_AreClampedRatherThanRefused()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(() => DirectorOn(Host) != null, "the match director");

            // A host typing into a field, not an attack. Zero starting life is a match everybody
            // loses on the first frame; a negative drain heals forever. Both are worth not being
            // reachable rather than worth a rule apiece.
            DirectorOn(Host).RequestSettings(new MatchSettings
            {
                StartingLife = 0f,
                MaxLife = 0f,
                DrainPerSecond = -5f,
                RoundSeconds = -1f,
                LifeReplicationHz = 0f
            });

            yield return WaitFor(
                () => DirectorOn(Host).Rules.StartingLife >= MatchSettings.MinLife,
                "the server to clamp what it was sent");

            MatchSettings applied = DirectorOn(Host).Rules;

            Assert.AreEqual(MatchSettings.MinLife, applied.MaxLife, 0.01f, "the ceiling");
            Assert.AreEqual(MatchSettings.MinDrain, applied.DrainPerSecond, 0.01f, "the drain");
            Assert.LessOrEqual(applied.StartingLife, applied.MaxLife,
                "A player would start above the ceiling and drain from full.");

            // Zero is the sentinel for no clock at all, which the sandbox runs on, so it survives.
            Assert.AreEqual(0f, applied.RoundSeconds, 0.01f, "the round length");
        }

        [UnityTest]
        public IEnumerator APreset_ReplacesEveryNumberAtOnce()
        {
            yield return StartSession(clientCount: 1);

            yield return WaitFor(
                () => DirectorOn(Clients[0]) != null && DirectorOn(Host).PresetCount > 0,
                "the client to receive a director with presets");

            var catalog = AssetDatabase.LoadAssetAtPath<DifficultyCatalog>(
                "Assets/_Project/Settings/DifficultyCatalog.asset");

            Assert.IsNotNull(catalog, "The difficulty catalog is missing.");
            Assert.IsNull(catalog.Validate(), "The difficulty catalog does not validate.");

            // The last one, so a preset that quietly did nothing cannot pass by matching the
            // numbers the session already started on.
            int last = catalog.Count - 1;
            MatchSettings expected = catalog.SettingsFor(last).Clamped();

            DirectorOn(Host).RequestPreset(last);

            yield return WaitFor(
                () => DirectorOn(Clients[0]).Rules.Equals(expected),
                $"the client to receive every number of the \"{catalog.Get(last).DisplayName}\" preset");
        }

        private static MatchDirector DirectorOn(NetworkManager peer)
        {
            foreach (NetworkObject spawned in peer.SpawnManager.SpawnedObjectsList)
            {
                var director = spawned.GetComponent<MatchDirector>();
                if (director != null) return director;
            }

            return null;
        }

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

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing. The test cannot check a prefab that is not there.");

            return prefab;
        }
    }
}

#endif
