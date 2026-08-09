using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Option A — the nickname and life bar floating above a character, drawn in world space.
    /// </summary>
    /// <remarks>
    /// <para>A world-space UI Toolkit panel rather than a screen-space element reprojected onto the
    /// character's head. Reprojection means every bar re-reads a transform and recomputes a screen
    /// position every frame, and gets it subtly wrong whenever the camera moves between the read
    /// and the draw. Attaching the panel to the character makes the bar follow because it is
    /// genuinely there.</para>
    /// <para>It hangs off the same child the sprite does, so it inherits the correction smoothing
    /// rather than jittering independently of the character it labels — and disappears with them
    /// when they go out, which is the behaviour wanted anyway.</para>
    /// <para>Reads life; replicates nothing. The number already crossed the wire because the server
    /// owns it.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class PlayerNameplate : MonoBehaviour
    {
        [Tooltip("Life this player has, in seconds, at which the bar turns red.")]
        [SerializeField] float _lowLifeSeconds = 10f;

        UIDocument _document;
        VisualElement _root;
        Label _name;
        VisualElement _fill;

        PlayerLife _life;

        void Awake()
        {
            _document = GetComponent<UIDocument>();
            _life = GetComponentInParent<PlayerLife>();
        }

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("nameplate-root");
            _name = root.Q<Label>("nameplate-name");
            _fill = root.Q<VisualElement>("nameplate-fill");
        }

        void LateUpdate()
        {
            if (_root == null || _life == null) return;

            MatchDirector director = MatchDirector.Current;

            bool visible = LifeBarStyle.Placement == LifeBarPlacement.OverTheCharacter
                           && _life.IsAlive
                           && director != null
                           && (director.Phase == MatchPhase.Countdown
                               || director.Phase == MatchPhase.Playing);

            _root.EnableInClassList("hidden", !visible);
            if (!visible) return;

            _name.text = LifeText.NameOf(_life.OwnerClientId);
            _fill.style.width = Length.Percent(Mathf.Clamp01(_life.Fraction) * 100f);
            _fill.EnableInClassList("nameplate__fill--low", _life.Remaining <= _lowLifeSeconds);
        }
    }
}
