using System.Collections.Generic;
using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Option B — a strip along the bottom of the screen: portrait, nickname and life bar per player.
    /// </summary>
    /// <remarks>
    /// <para>The fighting-game arrangement, and it trades the same way that one does. Everyone's
    /// state is in one place and readable at a glance, at the cost of the reader having to connect
    /// an entry to a character somewhere on screen. Option A pays the opposite price. Which reads
    /// better with four players moving is what <see cref="LifeBarStyle"/> exists to settle.</para>
    /// <para>Entries are built when the set of players changes and only their text and widths are
    /// rewritten each frame. The obvious version rebuilds the strip every frame, which allocates an
    /// entry per player sixty times a second to redraw a shape that almost never changes.</para>
    /// <para>Replicates nothing: life, names and skins are all already on the wire for their own
    /// reasons, so this is a view.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class LifeBarsController : MonoBehaviour
    {
        [Tooltip("Portraits, so an entry can show the skin its player picked.")]
        [SerializeField] CharacterCatalog _catalog;

        [Tooltip("Life in seconds at which a bar turns red.")]
        [SerializeField] float _lowLifeSeconds = 10f;

        UIDocument _document;
        VisualElement _root;
        VisualElement _strip;

        readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();
        readonly List<ulong> _present = new List<ulong>();
        readonly List<ulong> _removals = new List<ulong>();

        struct Entry
        {
            public VisualElement Container;
            public VisualElement Portrait;
            public Label Name;
            public VisualElement Fill;
            public Label Seconds;
        }

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("lifebars-root");
            _strip = root.Q<VisualElement>("lifebars-row");
        }

        void Update()
        {
            MatchDirector director = MatchDirector.Current;

            bool visible = LifeBarStyle.Placement == LifeBarPlacement.AlongTheBottom
                           && director != null
                           && (director.Phase == MatchPhase.Countdown
                               || director.Phase == MatchPhase.Playing
                               || director.Phase == MatchPhase.Ended);

            _root.EnableInClassList("hidden", !visible);
            if (!visible) return;

            SyncEntries();
            UpdateEntries();
        }

        void SyncEntries()
        {
            _present.Clear();

            for (int i = 0; i < PlayerLife.All.Count; i++)
                _present.Add(PlayerLife.All[i].OwnerClientId);

            for (int i = 0; i < _present.Count; i++)
                if (!_entries.ContainsKey(_present[i]))
                    _entries[_present[i]] = BuildEntry(_present[i]);

            // Collected first: removing from a dictionary while enumerating it throws.
            _removals.Clear();

            foreach (KeyValuePair<ulong, Entry> pair in _entries)
                if (!_present.Contains(pair.Key)) _removals.Add(pair.Key);

            for (int i = 0; i < _removals.Count; i++)
            {
                _entries[_removals[i]].Container.RemoveFromHierarchy();
                _entries.Remove(_removals[i]);
            }
        }

        Entry BuildEntry(ulong clientId)
        {
            var container = new VisualElement();
            container.AddToClassList("lifebar");
            container.pickingMode = PickingMode.Ignore;

            var portrait = new VisualElement();
            portrait.AddToClassList("lifebar__portrait");

            var column = new VisualElement();
            column.AddToClassList("lifebar__column");

            var name = new Label();
            name.AddToClassList("lifebar__name");

            bool isLocal = NetworkManager.Singleton != null
                           && NetworkManager.Singleton.LocalClientId == clientId;

            if (isLocal) name.AddToClassList("lifebar__name--you");

            var track = new VisualElement();
            track.AddToClassList("lifebar__track");

            var fill = new VisualElement();
            fill.AddToClassList("lifebar__fill");

            var seconds = new Label();
            seconds.AddToClassList("lifebar__seconds");

            track.Add(fill);
            column.Add(name);
            column.Add(track);
            container.Add(portrait);
            container.Add(column);
            container.Add(seconds);
            _strip.Add(container);

            return new Entry
            {
                Container = container,
                Portrait = portrait,
                Name = name,
                Fill = fill,
                Seconds = seconds
            };
        }

        void UpdateEntries()
        {
            for (int i = 0; i < PlayerLife.All.Count; i++)
            {
                PlayerLife life = PlayerLife.All[i];
                if (!_entries.TryGetValue(life.OwnerClientId, out Entry entry)) continue;

                entry.Name.text = LifeText.NameOf(life.OwnerClientId);
                entry.Seconds.text = life.IsAlive ? LifeText.Clock(life.Remaining) : "OUT";
                entry.Fill.style.width = Length.Percent(Mathf.Clamp01(life.Fraction) * 100f);

                ApplyPortrait(entry, life.OwnerClientId);

                // A player who is out keeps their entry, dimmed. Removing it would shuffle everyone
                // else along the strip at the exact moment a reader is looking for what changed.
                entry.Container.EnableInClassList("lifebar--out", !life.IsAlive);
                entry.Fill.EnableInClassList("lifebar__fill--low",
                    life.IsAlive && life.Remaining <= _lowLifeSeconds);
            }
        }

        void ApplyPortrait(Entry entry, ulong clientId)
        {
            if (_catalog == null) return;

            Sprite portrait = _catalog.Get(LifeText.CharacterIndexOf(clientId)).Portrait;
            if (portrait == null) return;

            // Assigned every frame rather than once: the roster is replicated, so the skin index can
            // arrive after the entry that shows it. Re-applying costs nothing and removes the race.
            entry.Portrait.style.backgroundImage = new StyleBackground(portrait);
        }
    }
}
