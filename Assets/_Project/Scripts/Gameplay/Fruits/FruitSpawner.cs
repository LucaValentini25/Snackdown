using System.Collections.Generic;
using Snackdown.Gameplay.Match;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Fruits
{
    /// <summary>
    /// Keeps fruit appearing around the arena while a match runs.
    /// </summary>
    /// <remarks>
    /// <para>Server-only. Everything about a spawn is a decision that affects the match — where,
    /// when, and which fruit — so clients learn about it the same way they learn about anything
    /// else: the object shows up.</para>
    /// <para>Spawn points are authored rather than random within a rectangle. A fruit that rolls
    /// inside geometry is unreachable, and one that rolls into a corner nobody visits is a fruit
    /// that never existed. Placing them by hand is also what lets a level designer control the
    /// risk of going after one.</para>
    /// </remarks>
    public class FruitSpawner : NetworkBehaviour
    {
        [SerializeField] FruitTable _table;

        [Tooltip("Fruit prefab. Must be registered in the network prefab list.")]
        [SerializeField] NetworkObject _fruitPrefab;

        [Tooltip("Where fruit may appear. Authored, not random within an area.")]
        [SerializeField] Transform[] _spawnPoints;

        [Tooltip("Seconds between spawn attempts.")]
        [SerializeField] float _interval = 4f;

        [Tooltip("Most fruit allowed in the arena at once.")]
        [SerializeField] int _maxActive = 6;

        readonly List<NetworkObject> _active = new List<NetworkObject>();
        float _sinceLastSpawn;

        public int ActiveCount
        {
            get
            {
                Prune();
                return _active.Count;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }

            string problem = Describe();
            if (problem != null)
            {
                Debug.LogError($"[Snackdown] Fruit spawner disabled: {problem}", this);
                enabled = false;
            }
        }

        string Describe()
        {
            if (_table == null) return "no fruit table assigned.";
            if (_fruitPrefab == null) return "no fruit prefab assigned.";
            if (_spawnPoints == null || _spawnPoints.Length == 0) return "no spawn points assigned.";
            return _table.Validate();
        }

        void Update()
        {
            MatchDirector director = MatchDirector.Current;
            if (director == null || !director.IsPlaying) return;

            _sinceLastSpawn += Time.deltaTime;
            if (_sinceLastSpawn < _interval) return;

            _sinceLastSpawn = 0f;
            TrySpawn();
        }

        void TrySpawn()
        {
            Prune();
            if (_active.Count >= _maxActive) return;

            Transform point = FreeSpawnPoint();
            if (point == null) return;

            int kind = _table.Roll(Random.value);
            if (kind < 0) return;

            NetworkObject instance = Instantiate(_fruitPrefab, point.position, Quaternion.identity);

            // Kind is set before Spawn so it is part of the initial state every client receives.
            // Setting it afterwards would replicate a second message and let clients briefly draw
            // the wrong fruit.
            Fruit fruit = instance.GetComponent<Fruit>();
            if (fruit != null) fruit.ServerSetKind(kind);

            instance.Spawn();
            _active.Add(instance);
        }

        /// <summary>
        /// A spawn point with no fruit already on it, or null if every point is taken.
        /// </summary>
        /// <remarks>
        /// Chosen at random among the free ones rather than in order, so fruit does not appear in a
        /// predictable rotation that players can camp.
        /// </remarks>
        Transform FreeSpawnPoint()
        {
            var free = new List<Transform>(_spawnPoints.Length);

            foreach (Transform point in _spawnPoints)
            {
                if (point == null) continue;

                bool taken = false;
                foreach (NetworkObject active in _active)
                    if (active != null && (active.transform.position - point.position).sqrMagnitude < 0.01f)
                    {
                        taken = true;
                        break;
                    }

                if (!taken) free.Add(point);
            }

            return free.Count == 0 ? null : free[Random.Range(0, free.Count)];
        }

        /// <summary>Drops references to fruit that has been collected or despawned.</summary>
        void Prune() => _active.RemoveAll(o => o == null || !o.IsSpawned);

        /// <summary>Clears the arena between rounds. Server-only.</summary>
        public void ServerDespawnAll()
        {
            if (!IsServer) return;

            foreach (NetworkObject fruit in _active)
                if (fruit != null && fruit.IsSpawned) fruit.Despawn();

            _active.Clear();
            _sinceLastSpawn = 0f;
        }
    }
}
