using Snackdown.Gameplay.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// The round clock, shown while a match is running.
    /// </summary>
    /// <remarks>
    /// <para>It puts nothing new on the wire. The referee publishes the round's end as a single
    /// deadline against the clock NGO already keeps synchronized, so every peer derives the same
    /// number locally. Replicating the seconds instead would be a message a frame for a value
    /// everyone could already compute — the same mistake <c>docs/00</c> records against the
    /// original's life timer.</para>
    /// <para>A reconciler like the other screens: it asks every frame what the phase is rather than
    /// subscribing to changes, so a client that joins mid-match is right immediately.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class RoundClockController : MonoBehaviour
    {
        [Tooltip("Seconds remaining below which the clock turns red.")]
        [SerializeField] float _urgentBelowSeconds = 30f;

        UIDocument _document;
        VisualElement _root;
        Label _clock;

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("clock-root");
            _clock = root.Q<Label>("round-clock");
        }

        void Update()
        {
            MatchDirector director = MatchDirector.Current;
            RoundReferee referee = RoundReferee.Current;

            bool visible = director != null
                           && referee != null
                           && (director.Phase == MatchPhase.Playing || director.Phase == MatchPhase.Ended);

            _root.EnableInClassList("hidden", !visible);
            if (!visible) return;

            float remaining = referee.RoundRemaining;
            _clock.text = LifeText.Clock(remaining);
            _clock.EnableInClassList("round-clock--urgent", remaining <= _urgentBelowSeconds);
        }
    }
}
