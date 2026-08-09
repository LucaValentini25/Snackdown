using System.Collections.Generic;
using Snackdown.Connection;
using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// The in-match HUD: the round clock, and how much life everyone has left.
    /// </summary>
    /// <remarks>
    /// <para><b>It puts nothing new on the wire.</b> Every number here already crossed the network
    /// for its own reasons — life because the server owns it, names because approval sanitized them
    /// into the roster, the round deadline because the referee published it once. A scoreboard that
    /// replicated its own copy would be a second version of facts that already exist, free to
    /// disagree with the first. This is a view, and views read.</para>
    /// <para>A reconciler like the other screens: it asks every frame what the match looks like
    /// rather than subscribing to changes, so a client that arrives mid-match is right immediately
    /// instead of right from the next event onward.</para>
    /// <para>Rows are built when the set of players changes and only their text is rewritten each
    /// frame. Rebuilding the list every frame would allocate a fresh row per player thirty times a
    /// second for a display whose shape almost never changes.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class ScoreboardController : MonoBehaviour
    {
        [Tooltip("Seconds remaining below which the clock turns red.")]
        [SerializeField] float _urgentBelowSeconds = 30f;

        [Tooltip("Seconds of life below which a player's number turns red.")]
        [SerializeField] float _lowLifeSeconds = 10f;

        UIDocument _document;
        VisualElement _root;
        VisualElement _rows;
        Label _clock;

        /// <summary>Row widgets by owning client, so a rebuild is only needed when the set changes.</summary>
        readonly Dictionary<ulong, Row> _rowsByClient = new Dictionary<ulong, Row>();

        /// <summary>Scratch for the current set of players, reused so the check allocates nothing.</summary>
        readonly List<ulong> _present = new List<ulong>();

        /// <summary>Clients whose row is due for removal, collected before the dictionary is edited.</summary>
        readonly List<ulong> _removals = new List<ulong>();

        struct Row
        {
            public VisualElement Container;
            public Label Name;
            public Label Life;
        }

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("scoreboard-root");
            _rows = root.Q<VisualElement>("scoreboard-rows");
            _clock = root.Q<Label>("round-clock");
        }

        void Update()
        {
            MatchDirector director = MatchDirector.Current;
            bool visible = director != null
                           && (director.Phase == MatchPhase.Playing || director.Phase == MatchPhase.Ended);

            _root.EnableInClassList("hidden", !visible);
            if (!visible) return;

            UpdateClock();
            SyncRows();
            UpdateRows();
        }

        void UpdateClock()
        {
            RoundReferee referee = RoundReferee.Current;

            if (referee == null)
            {
                _clock.text = string.Empty;
                return;
            }

            float remaining = referee.RoundRemaining;
            _clock.text = Format(remaining);
            _clock.EnableInClassList("round-clock--urgent", remaining <= _urgentBelowSeconds);
        }

        /// <summary>Adds and removes row widgets so they match the players actually present.</summary>
        void SyncRows()
        {
            _present.Clear();

            for (int i = 0; i < PlayerLife.All.Count; i++)
                _present.Add(PlayerLife.All[i].OwnerClientId);

            for (int i = 0; i < _present.Count; i++)
                if (!_rowsByClient.ContainsKey(_present[i]))
                    _rowsByClient[_present[i]] = BuildRow(_present[i]);

            // Anyone who disconnected mid-match loses their row. Collected first because removing
            // from a dictionary while enumerating it throws.
            _removals.Clear();

            foreach (KeyValuePair<ulong, Row> pair in _rowsByClient)
                if (!_present.Contains(pair.Key)) _removals.Add(pair.Key);

            for (int i = 0; i < _removals.Count; i++)
            {
                _rowsByClient[_removals[i]].Container.RemoveFromHierarchy();
                _rowsByClient.Remove(_removals[i]);
            }
        }

        Row BuildRow(ulong clientId)
        {
            var container = new VisualElement();
            container.AddToClassList("score-row");
            container.pickingMode = PickingMode.Ignore;

            var name = new Label();
            name.AddToClassList("score-row__name");

            var life = new Label();
            life.AddToClassList("score-row__life");

            bool isLocal = NetworkManager.Singleton != null
                           && NetworkManager.Singleton.LocalClientId == clientId;

            if (isLocal) name.AddToClassList("score-row__you");

            container.Add(name);
            container.Add(life);
            _rows.Add(container);

            return new Row { Container = container, Name = name, Life = life };
        }

        void UpdateRows()
        {
            SessionRoster roster = SessionRoster.Current;

            for (int i = 0; i < PlayerLife.All.Count; i++)
            {
                PlayerLife life = PlayerLife.All[i];
                if (!_rowsByClient.TryGetValue(life.OwnerClientId, out Row row)) continue;

                row.Name.text = NameOf(roster, life.OwnerClientId);
                row.Life.text = life.IsAlive ? Format(life.Remaining) : "OUT";

                row.Container.EnableInClassList("score-row--out", !life.IsAlive);
                row.Life.EnableInClassList("score-row__life--low",
                    life.IsAlive && life.Remaining <= _lowLifeSeconds);
            }
        }

        static string NameOf(SessionRoster roster, ulong clientId)
        {
            if (roster == null) return $"Player {clientId}";

            for (int i = 0; i < roster.Count; i++)
                if (roster[i].ClientId == clientId)
                    return roster[i].Nickname.ToString();

            return $"Player {clientId}";
        }

        /// <summary>Seconds as <c>m:ss</c>, because a bare count of seconds stops being readable past a minute.</summary>
        static string Format(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }
    }
}
